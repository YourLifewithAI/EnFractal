using Godot;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using FileAccess = Godot.FileAccess;

namespace EnFractal.Native.Room;

/// <summary>A room.json could not be loaded. The message is safe to show and names the failing rule.</summary>
public sealed class RoomLoadException(string message) : Exception(message);

public sealed record ShellPart(
    string Id, string Role, Vector3[] Points, float ThicknessM, string? MeshPath,
    bool Collides, string MaterialRole, Color BaseColor);

public sealed record MaterialSlot(string Slot, string Role, Color? BaseColor);

public sealed record AssetInfo(
    string AssetId, string Directory, string DisplayName, string Category, string CategoryGroup, string Tier,
    string ProvenanceKind, Vector3 DimensionsM, string GeometryKind, string? Primitive, string? MeshPath,
    string CollisionKind, string? CollisionFile, bool Movable, float MassKg, MaterialSlot[] Materials,
    string[] Affordances, string ReviewStatus);

public sealed record ObjectInstance(
    string Id, AssetInfo Asset, Vector3 PositionM, Quaternion Rotation, float Scale,
    string SupportKind, string? SupportTarget, string? DisplayName);

public sealed record SpawnPoint(string Id, string Role, Vector3 PositionM, float YawDeg);

public sealed record LightHint(string Id, string Kind, Vector3? PositionM, Vector3? Direction, Color Color, float RelativeIntensity);

public sealed record StylePin(string PresetId, int PresetVersion, string PresetSha256);

/// <summary>
/// Loads contracts/room-manifest.schema.json documents with integrity checks: every referenced file
/// must be listed with a matching SHA-256 and size, paths stay inside the room, and pinned bytes are
/// UTF-8 with LF line endings. Full schema validation is contracts/validate.py; this loader enforces
/// what the game relies on and fails closed.
/// </summary>
public sealed class RoomData
{
    public const int SchemaVersion = 1;
    // Same rules as contracts/common.schema.json: one spelling per file, no ".", ".." or empty segments.
    private static readonly Regex RelativePath = new(@"^(?!.*(?:^|/)\.\.?(?:/|$))(?!.*//)(?!.*/$)[A-Za-z0-9_][A-Za-z0-9_./-]{0,199}$", RegexOptions.Compiled);
    private static readonly Regex AssetPath = new(@"^objects/[a-z][a-z0-9_-]{0,63}/asset\.json$", RegexOptions.Compiled);
    // Control, line-separator, zero-width and bidirectional-override characters cannot appear in display text.
    private static readonly Regex UnsafeText = new(@"[\u0000-\u001F\u007F-\u009F\u200B-\u200F\u2028\u2029\u202A-\u202E\u2060-\u2069\uFEFF]", RegexOptions.Compiled);
    public const float CoordinateLimitM = 1000f;
    private static readonly Regex Token = new(@"^[a-z][a-z0-9_-]{0,63}$", RegexOptions.Compiled);
    private static readonly JsonDocumentOptions Strict = new() { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 64 };

    public string Directory { get; private init; } = "";
    public string RoomId { get; private init; } = "";
    public string DisplayName { get; private init; } = "";
    public string SourceKind { get; private init; } = "";
    public string ManifestSha256 { get; private init; } = "";
    public Aabb Bounds { get; private init; }
    public IReadOnlyList<ShellPart> Shell { get; private init; } = Array.Empty<ShellPart>();
    public IReadOnlyList<ObjectInstance> Objects { get; private init; } = Array.Empty<ObjectInstance>();
    public IReadOnlyList<SpawnPoint> Spawns { get; private init; } = Array.Empty<SpawnPoint>();
    public IReadOnlyList<LightHint> LightHints { get; private init; } = Array.Empty<LightHint>();
    public StylePin? DefaultStyle { get; private init; }
    /// <summary>Mesh bytes exactly as hash-verified, keyed by full path; the builder never reads them from disk again.</summary>
    public IReadOnlyDictionary<string, byte[]> VerifiedMeshes { get; private init; } = new Dictionary<string, byte[]>();

    public SpawnPoint SpawnFor(string role, SpawnPoint? except = null) =>
        Spawns.FirstOrDefault(s => s.Role == role && s != except) ??
        Spawns.FirstOrDefault(s => s.Role == "any" && s != except) ??
        throw new RoomLoadException($"Room {RoomId} has no spawn for {role}.");

    public static RoomData Load(string directory)
    {
        directory = directory.TrimEnd('/');
        var manifestBytes = ReadPinned(directory + "/room.json", "room.json");
        using var document = Parse(manifestBytes, "room.json");
        var root = document.RootElement;
        Expect(Str(root, "schema") == "enfractal.room" && Int(root, "version") == SchemaVersion, "room.json is not an enfractal.room version 1 manifest");
        Expect(Str(root, "units") == "m" && Str(root, "axes") == "y_up_neg_z_forward", "room.json must use metres and Godot axes");
        var roomId = Str(root, "room_id");
        Expect(Token.IsMatch(roomId), "room_id is not a valid token");
        var folder = directory[(directory.LastIndexOf('/') + 1)..];
        Expect(folder == roomId, $"room_id {roomId} must match its directory name {folder}");

        var meshes = new Dictionary<string, byte[]>();
        var listed = new Dictionary<string, (string Sha, long Bytes)>();
        foreach (var entry in Arr(root, "files"))
        {
            var path = SafePath(Str(entry, "path"));
            Expect(listed.TryAdd(path, (Str(entry, "sha256"), entry.GetProperty("bytes").GetInt64())), $"file {path} is listed twice");
        }
        var assets = new Dictionary<string, AssetInfo>();
        AssetInfo AssetAt(string path)
        {
            if (assets.TryGetValue(path, out var cached)) return cached;
            Expect(AssetPath.IsMatch(path), $"asset path {path} must be objects/<asset_id>/asset.json");
            var bytes = ReadListed(directory, listed, path, "room.json");
            var asset = ParseAsset(directory + "/" + path[..path.LastIndexOf('/')], bytes, path, meshes);
            assets[path] = asset;
            return asset;
        }

        var shell = Arr(root.GetProperty("shell"), "parts").Select(p => ParseShell(directory, listed, p, meshes)).ToArray();
        var objects = Arr(root, "objects").Select(o =>
        {
            var transform = o.GetProperty("transform");
            var support = o.GetProperty("support");
            return new ObjectInstance(
                Str(o, "id"), AssetAt(Str(o, "asset")), Vec3(transform.GetProperty("position_m")), Quat(transform.GetProperty("rotation")),
                transform.TryGetProperty("scale", out var scale) ? scale.GetSingle() : 1f,
                Str(support, "kind"), OptStr(support, "target_id"), OptDisplay(o, "display_name", 80));
        }).ToArray();
        var spawns = Arr(root, "spawns").Select(s => new SpawnPoint(Str(s, "id"), Str(s, "role"), Vec3(s.GetProperty("position_m")), s.GetProperty("yaw_deg").GetSingle())).ToArray();
        var hints = root.TryGetProperty("light_hints", out var h) ? h.EnumerateArray().Select(l => new LightHint(
            Str(l, "id"), Str(l, "kind"),
            l.TryGetProperty("position_m", out var position) ? Vec3(position) : null,
            l.TryGetProperty("direction", out var direction) ? Vec3(direction) : null,
            new Color(Str(l, "color")), l.GetProperty("relative_intensity").GetSingle())).ToArray() : Array.Empty<LightHint>();
        var bounds = root.GetProperty("bounds");
        var min = Vec3(bounds.GetProperty("min_m"));
        var max = Vec3(bounds.GetProperty("max_m"));
        Expect(min.X < max.X && min.Y < max.Y && min.Z < max.Z, "bounds min must be below max");
        var ids = shell.Select(s => s.Id).Concat(objects.Select(o => o.Id)).ToArray();
        Expect(ids.Length == ids.Distinct().Count(), "shell and object ids must be unique");
        Expect(spawns.Any(s => s.Role is "player" or "any") && spawns.Any(s => s.Role is "companion" or "any"), "room needs player and companion spawns");
        StylePin? style = null;
        if (root.TryGetProperty("default_style", out var pin))
            style = new StylePin(Str(pin, "preset_id"), Int(pin, "preset_version"), Str(pin, "preset_sha256"));

        return new RoomData
        {
            Directory = directory, RoomId = roomId, DisplayName = Display(root, "display_name", 80), DefaultStyle = style, VerifiedMeshes = meshes,
            SourceKind = Str(root.GetProperty("source"), "kind"), ManifestSha256 = Sha256Hex(manifestBytes),
            Bounds = new Aabb(min, max - min), Shell = shell, Objects = objects, Spawns = spawns, LightHints = hints,
        };
    }

    private static ShellPart ParseShell(string directory, Dictionary<string, (string Sha, long Bytes)> listed, JsonElement part, Dictionary<string, byte[]> meshes)
    {
        var geometry = part.GetProperty("geometry");
        var kind = Str(geometry, "kind");
        Vector3[] points = Array.Empty<Vector3>();
        string? mesh = null;
        var thickness = 0f;
        if (kind == "polygon")
        {
            points = Arr(geometry, "points_m").Select(Vec3).ToArray();
            thickness = geometry.GetProperty("thickness_m").GetSingle();
            Expect(points.Length >= 3 && thickness > 0, $"{Str(part, "id")} needs at least three points and a positive thickness");
        }
        else
        {
            mesh = SafePath(Str(geometry, "mesh"));
            meshes[directory + "/" + mesh] = ReadMesh(directory, listed, mesh, "room.json");
        }
        if (part.TryGetProperty("texture", out var texture)) ReadListed(directory, listed, SafePath(texture.GetString()!), "room.json");
        return new ShellPart(Str(part, "id"), Str(part, "role"), points, thickness, mesh, part.GetProperty("collides").GetBoolean(),
            Str(part, "material_role"), part.TryGetProperty("base_color", out var color) ? new Color(color.GetString()!) : new Color("b3aea4"));
    }

    private static AssetInfo ParseAsset(string assetDirectory, byte[] bytes, string label, Dictionary<string, byte[]> meshes)
    {
        using var document = Parse(bytes, label);
        var root = document.RootElement;
        Expect(Str(root, "schema") == "enfractal.asset" && Int(root, "version") == 1, $"{label} is not an enfractal.asset version 1 document");
        Expect(Str(root, "pivot") == "bottom_center", $"{label} must use the bottom_center pivot");
        var assetId = Str(root, "asset_id");
        Expect(assetDirectory.EndsWith("/" + assetId, StringComparison.Ordinal), $"{label}: asset_id must match its directory name");
        var listed = new Dictionary<string, (string Sha, long Bytes)>();
        foreach (var entry in Arr(root, "files"))
            Expect(listed.TryAdd(SafePath(Str(entry, "path")), (Str(entry, "sha256"), entry.GetProperty("bytes").GetInt64())), $"{label} lists a file twice");
        var geometry = root.GetProperty("geometry");
        var geometryKind = Str(geometry, "kind");
        string? mesh = null, primitive = null;
        if (geometryKind == "mesh")
        {
            mesh = SafePath(Str(geometry, "mesh"));
            meshes[assetDirectory + "/" + mesh] = ReadMesh(assetDirectory, listed, mesh, label);
        }
        else primitive = Str(geometry, "primitive");
        var collision = root.GetProperty("collision");
        var collisionFile = OptStr(collision, "file");
        if (collisionFile != null) meshes[assetDirectory + "/" + collisionFile] = ReadMesh(assetDirectory, listed, SafePath(collisionFile), label);
        var collisionKind = Str(collision, "kind");
        Expect(geometryKind != "mesh" || collisionKind != "primitive", $"{label}: a mesh asset cannot use primitive collision");
        var physics = root.GetProperty("physics");
        var dimensions = Vec3(root.GetProperty("dimensions_m"));
        Expect(dimensions.X > 0 && dimensions.Y > 0 && dimensions.Z > 0, $"{label} dimensions must be positive");
        var materials = Arr(root, "materials").Select(m => new MaterialSlot(Str(m, "slot"), Str(m, "role"),
            m.TryGetProperty("base_color", out var c) ? new Color(c.GetString()!) : null)).ToArray();
        return new AssetInfo(
            assetId, assetDirectory, Display(root, "display_name", 80), Display(root, "category", 60), Str(root, "category_group"), Str(root, "tier"),
            Str(root.GetProperty("provenance"), "kind"), dimensions, geometryKind, primitive, mesh,
            collisionKind, collisionFile, physics.GetProperty("movable").GetBoolean(), physics.GetProperty("mass_kg").GetSingle(),
            materials, Arr(root, "affordances").Select(a => a.GetString()!).ToArray(), Str(root.GetProperty("review"), "status"));
    }

    /// <summary>Reads a file that a manifest pins: it must be listed, and its size and hash must match.</summary>
    public static byte[] ReadListed(string directory, Dictionary<string, (string Sha, long Bytes)> listed, string path, string label)
    {
        Expect(listed.TryGetValue(path, out var pin), $"{label}: {path} is referenced but not listed in files");
        var bytes = ReadPinned(directory + "/" + path, path);
        Expect(bytes.LongLength == pin.Bytes, $"{label}: {path} is {bytes.LongLength} bytes, files list says {pin.Bytes}");
        Expect(Sha256Hex(bytes) == pin.Sha, $"{label}: {path} hash does not match the files list");
        return bytes;
    }

    private static byte[] ReadPinned(string path, string label)
    {
        if (!FileAccess.FileExists(path)) throw new RoomLoadException($"{label} not found at {path}");
        var bytes = FileAccess.GetFileAsBytes(path);
        Expect(bytes.Length > 0 || FileAccess.GetOpenError() == Error.Ok, $"{label} could not be read ({FileAccess.GetOpenError()})");
        if (path.EndsWith(".json", StringComparison.Ordinal))
        {
            Expect(!(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF), $"{label} must not start with a byte-order mark");
            Expect(Array.IndexOf(bytes, (byte)'\r') < 0, $"{label} must use LF line endings; pins hash the exact bytes");
        }
        return bytes;
    }

    private static byte[] ReadMesh(string directory, Dictionary<string, (string Sha, long Bytes)> listed, string path, string label)
    {
        Expect(path.EndsWith(".glb", StringComparison.Ordinal), $"{label}: mesh {path} must be a self-contained .glb");
        var bytes = ReadListed(directory, listed, path, label);
        RequireSelfContainedGlb(bytes, $"{label}: {path}");
        return bytes;
    }

    /// <summary>A pinned GLB must embed every buffer and image; an external URI would load unhashed bytes, possibly from outside the room.</summary>
    public static void RequireSelfContainedGlb(byte[] bytes, string label)
    {
        Expect(bytes.Length >= 20 && bytes[0] == (byte)'g' && bytes[1] == (byte)'l' && bytes[2] == (byte)'T' && bytes[3] == (byte)'F', $"{label} is not a binary glTF file");
        // GLB is little-endian. Lengths stay unsigned so a hostile chunk length cannot wrap negative.
        var version = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4));
        var length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8));
        Expect(version == 2 && length == bytes.Length, $"{label} has an invalid GLB header");
        var chunkLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12));
        Expect(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16)) == 0x4E4F534A && 20L + chunkLength <= bytes.Length, $"{label} does not start with a JSON chunk");
        // Same tolerance as contracts/validate.py: trailing space or NUL padding is not part of the JSON.
        var json = bytes.AsSpan(20, (int)chunkLength).TrimEnd(stackalloc byte[] { 0x20, 0x00 }).ToArray();
        using var document = Parse(json, label);
        Expect(document.RootElement.ValueKind == JsonValueKind.Object, $"{label} has a GLB JSON chunk that is not an object");
        foreach (var kind in new[] { "buffers", "images" })
        {
            if (!document.RootElement.TryGetProperty(kind, out var items)) continue;
            Expect(items.ValueKind == JsonValueKind.Array, $"{label} has a GLB '{kind}' entry that is not an array");
            foreach (var item in items.EnumerateArray())
                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("uri", out var uri) &&
                    !(uri.ValueKind == JsonValueKind.String && uri.GetString()!.StartsWith("data:", StringComparison.Ordinal)))
                    throw new RoomLoadException($"{label} references external file '{uri}'; meshes must be self-contained");
        }
    }

    /// <summary>JsonDocument keeps the last of duplicate keys; the contract rejects them, so check first.</summary>
    private static void RejectDuplicateKeys(byte[] bytes, string label)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 64 });
        var scopes = new Stack<HashSet<string>>();
        try
        {
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject: scopes.Push(new HashSet<string>(StringComparer.Ordinal)); break;
                    case JsonTokenType.EndObject: scopes.Pop(); break;
                    case JsonTokenType.PropertyName:
                        var name = reader.GetString()!;
                        if (!scopes.Peek().Add(name)) throw new RoomLoadException($"{label} has duplicate key '{name}'");
                        break;
                }
            }
        }
        catch (JsonException error) { throw new RoomLoadException($"{label} is not valid JSON: {error.Message}"); }
    }

    private static string Display(JsonElement element, string name, int maxLength)
    {
        var text = Str(element, name);
        Expect(text.Length <= maxLength && !UnsafeText.IsMatch(text), $"'{name}' must be at most {maxLength} characters without control or bidirectional characters");
        return text;
    }

    private static string? OptDisplay(JsonElement element, string name, int maxLength) =>
        element.TryGetProperty(name, out _) ? Display(element, name, maxLength) : null;

    public static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static JsonDocument Parse(byte[] bytes, string label)
    {
        RejectDuplicateKeys(bytes, label);
        try { return JsonDocument.Parse(bytes, Strict); }
        catch (JsonException error) { throw new RoomLoadException($"{label} is not valid JSON: {error.Message}"); }
    }

    private static string SafePath(string path)
    {
        Expect(RelativePath.IsMatch(path), $"unsafe or invalid relative path '{path}'");
        return path;
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new RoomLoadException(message);
    }

    private static string Str(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new RoomLoadException($"missing or non-string field '{name}'");
        return value.GetString()!;
    }

    private static string? OptStr(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number : throw new RoomLoadException($"missing or non-integer field '{name}'");

    private static IEnumerable<JsonElement> Arr(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray() : throw new RoomLoadException($"missing array '{name}'");

    private static Vector3 Vec3(JsonElement element)
    {
        var values = element.EnumerateArray().Select(v => v.GetDouble()).ToArray();
        Expect(values.Length == 3, "expected three numbers");
        var vector = new Vector3((float)values[0], (float)values[1], (float)values[2]);
        // Check after narrowing to float: a finite double can still overflow the runtime's float32.
        Expect(vector.IsFinite() && Mathf.Abs(vector.X) <= CoordinateLimitM && Mathf.Abs(vector.Y) <= CoordinateLimitM && Mathf.Abs(vector.Z) <= CoordinateLimitM,
            $"coordinates must be finite and within ±{CoordinateLimitM} m");
        return vector;
    }

    private static Quaternion Quat(JsonElement element)
    {
        var v = element.EnumerateArray().Select(x => x.GetDouble()).ToArray();
        Expect(v.Length == 4 && v.All(double.IsFinite), "rotation needs four finite numbers");
        var q = new Quaternion((float)v[0], (float)v[1], (float)v[2], (float)v[3]);
        Expect(Mathf.Abs(q.Length() - 1f) <= 1e-3f, "rotation is not a unit quaternion");
        return q.Normalized();
    }
}

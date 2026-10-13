using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using EnFractal.Native.Room;
using FileAccess = Godot.FileAccess;

namespace EnFractal.Tests;

/// <summary>
/// Writes synthetic islands for the loading tests (packet S6), from the contract's island example: its meshes and terrain GLBs
/// copied as they are, its tables replaced by synthetic ones written here as the contract lays them out (README "Plant copies":
/// header, tile index, rows sorted by 8 m tile then x then z), and its manifest rewritten with each file's hash and size.
/// Deterministic: a fixed seed. Written under user://tests/rooms, never into the repository.
/// </summary>
public static class IslandFixture
{
    public static string ExampleDirectory => Path.GetFullPath(ProjectSettings.GlobalizePath("res://") + "../contracts/examples/rooms/island_example");

    /// <summary>A copy's row before it is sorted into its table. Caps are (x, z, rim half x, rim half z, rim y, top y, top half x, top half z).</summary>
    public sealed record Row(float X, float Y, float Z, float Yaw, Vector3 Scale, Color Tint, float Top, uint Flags, uint Group, uint Seed,
        float[]? Trunk = null, float[][]? Caps = null);

    public sealed record SetSpec(string Name, string Prototype, string Kind, string Species, string Collides, string FuelClass, bool Seeds,
        string Mesh, int Triangles, (string Mesh, int Triangles)[] Lods, int CapSlots, IReadOnlyList<Row> Rows);

    /// <summary>A broadleaf like the example's: a trunk to 0.53 m, a pole to 0.935 m, one cap from 0.8 to 0.94 m over the trunk.</summary>
    public static Row Broadleaf(float x, float z, float y, uint group, uint seed) => new(x, y, z, 15f, Vector3.One, new Color(0.9f, 1f, 0.85f), y + 0.95f, 0, group, seed,
        new[] { x, z, 0.055f, 0.035f, y + 0.53f, y + 0.935f }, new[] { new[] { x, z, 0.15f, 0.15f, y + 0.8f, y + 0.94f, 0.06f, 0.06f } });

    /// <summary>The table bytes for rows: sorted, behind their tile index. Returns the bytes, the tile count and the rows in table order.</summary>
    public static (byte[] Bytes, int Tiles, Row[] Ordered) Table(PlantKind kind, int capSlots, IEnumerable<Row> rows)
    {
        var ordered = rows.OrderBy(r => PlantCopies.TileOf(r.X, r.Z).Tx).ThenBy(r => PlantCopies.TileOf(r.X, r.Z).Tz).ThenBy(r => r.X).ThenBy(r => r.Z).ToArray();
        var tiles = new List<(int Tx, int Tz, int First, int Count)>();
        for (var i = 0; i < ordered.Length; i++)
        {
            var tile = PlantCopies.TileOf(ordered[i].X, ordered[i].Z);
            if (tiles.Count > 0 && tiles[^1].Tx == tile.Tx && tiles[^1].Tz == tile.Tz) tiles[^1] = tiles[^1] with { Count = tiles[^1].Count + 1 };
            else tiles.Add((tile.Tx, tile.Tz, i, 1));
        }
        var tree = kind == PlantKind.Tree;
        var rowBytes = 56 + (tree ? 28 + 32 * capSlots : 0);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("EFPLANTS"u8.ToArray());
        foreach (var word in new uint[] { 1, (uint)kind, (uint)rowBytes, (uint)ordered.Length, (uint)tiles.Count, tree ? (uint)capSlots : 0, 8, 0 }) writer.Write(word);
        foreach (var (tx, tz, first, count) in tiles) { writer.Write(tx); writer.Write(tz); writer.Write((uint)first); writer.Write((uint)count); }
        foreach (var row in ordered)
        {
            foreach (var value in new[] { row.X, row.Y, row.Z, row.Yaw, row.Scale.X, row.Scale.Y, row.Scale.Z, row.Tint.R, row.Tint.G, row.Tint.B, row.Top }) writer.Write(value);
            writer.Write(row.Flags);
            writer.Write(row.Group);
            writer.Write(row.Seed);
            if (!tree) continue;
            foreach (var value in row.Trunk!) writer.Write(value);
            writer.Write((uint)row.Caps!.Length);
            for (var k = 0; k < capSlots; k++)
                foreach (var value in k < row.Caps.Length ? row.Caps[k] : new float[8]) writer.Write(value);
        }
        writer.Flush();
        return (stream.ToArray(), tiles.Count, ordered);
    }

    /// <summary>
    /// An island at user://tests/rooms/&lt;roomId&gt;: the example's files, sets as given (a set naming an example mesh reuses it),
    /// bounds as given (null keeps the example's). Returns the room's directory.
    /// </summary>
    public static string Write(string roomId, IReadOnlyList<SetSpec> sets, int groups, Vector3? boundsMin = null, Vector3? boundsMax = null)
    {
        var target = "user://tests/rooms/" + roomId;
        var absolute = ProjectSettings.GlobalizePath(target);
        if (System.IO.Directory.Exists(absolute)) System.IO.Directory.Delete(absolute, recursive: true);
        DirAccess.MakeDirRecursiveAbsolute(absolute + "/plants");
        DirAccess.MakeDirRecursiveAbsolute(absolute + "/shell");
        var manifest = JsonNode.Parse(File.ReadAllText(ExampleDirectory + "/room.json"))!.AsObject();
        var files = new Dictionary<string, byte[]>();
        foreach (var entry in manifest["files"]!.AsArray())
        {
            var path = entry!["path"]!.GetValue<string>();
            if (path.EndsWith(".glb", StringComparison.Ordinal)) files[path] = File.ReadAllBytes(ExampleDirectory + "/" + path);
        }
        var records = new JsonArray();
        foreach (var spec in sets)
        {
            var kind = spec.Kind switch { "tree" => PlantKind.Tree, "stone" => PlantKind.Stone, _ => PlantKind.Plant };
            var (bytes, tiles, _) = Table(kind, spec.CapSlots, spec.Rows);
            var table = $"plants/{spec.Name}.bin";
            files[table] = bytes;
            var record = new JsonObject
            {
                ["set"] = spec.Name, ["prototype"] = spec.Prototype, ["kind"] = spec.Kind, ["species"] = spec.Species, ["collides"] = spec.Collides,
                ["fuel_class"] = spec.FuelClass, ["seeds"] = spec.Seeds, ["mesh"] = spec.Mesh, ["triangle_count"] = spec.Triangles,
            };
            if (spec.Lods.Length > 0) record["lods"] = new JsonArray(spec.Lods.Select(l => (JsonNode)new JsonObject { ["mesh"] = l.Mesh, ["triangle_count"] = l.Triangles }).ToArray());
            record["table"] = table;
            if (kind == PlantKind.Tree) record["cap_slots"] = spec.CapSlots;
            record["tiles"] = tiles;
            record["count"] = spec.Rows.Count;
            records.Add(record);
        }
        manifest["room_id"] = roomId;
        manifest["plant_copies"]!["groups"] = groups;
        manifest["plant_copies"]!["sets"] = records;
        if (boundsMin is { } low && boundsMax is { } high)
            manifest["bounds"] = new JsonObject { ["min_m"] = new JsonArray(low.X, low.Y, low.Z), ["max_m"] = new JsonArray(high.X, high.Y, high.Z) };
        var listed = new JsonArray();
        foreach (var (path, bytes) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            Save(target + "/" + path, bytes);
            listed.Add(new JsonObject { ["path"] = path, ["sha256"] = RoomData.Sha256Hex(bytes), ["bytes"] = bytes.Length });
        }
        manifest["files"] = listed;
        Save(target + "/room.json", System.Text.Encoding.UTF8.GetBytes(manifest.ToJsonString()));
        return target;
    }

    /// <summary>The example's grass set with these rows: a 12-triangle tuft and its 2-triangle card for distance.</summary>
    public static SetSpec Grass(string name, IReadOnlyList<Row> rows) =>
        new(name, "grass_tuft", "plant", "grass", "none", "grass", true, "plants/grass.glb", 12, new[] { ("plants/grass_far.glb", 2) }, 0, rows);

    public static SetSpec Trees(IReadOnlyList<Row> rows) =>
        new("broadleaf", "broadleaf", "tree", "broadleaf", "tree_climb", "broadleaf", true, "plants/broadleaf.glb", 12, new[] { ("plants/broadleaf_far.glb", 2) }, 2, rows);

    public static SetSpec Stones(IReadOnlyList<Row> rows) =>
        new("stones", "rock", "stone", "rock", "solid", "none", false, "plants/stones.glb", 12, Array.Empty<(string, int)>(), 0, rows);

    /// <summary>Grass rows spread evenly at random over a rectangle, group 2 every tenth (the example's patch), the rest in none.</summary>
    public static List<Row> GrassRows(int count, Rect2 area, int seed, float height = 0.13f)
    {
        var random = new Random(seed);
        var rows = new List<Row>(count);
        for (var i = 0; i < count; i++)
        {
            var x = area.Position.X + (float)random.NextDouble() * area.Size.X;
            var z = area.Position.Y + (float)random.NextDouble() * area.Size.Y;
            var scale = new Vector3(0.8f + 0.4f * (float)random.NextDouble(), 0.7f + 0.6f * (float)random.NextDouble(), 0.8f + 0.4f * (float)random.NextDouble());
            var tint = new Color(0.75f + 0.25f * (float)random.NextDouble(), 0.85f + 0.15f * (float)random.NextDouble(), 0.6f + 0.3f * (float)random.NextDouble());
            rows.Add(new Row(x, 0, z, -180f + 360f * (float)random.NextDouble(), scale, tint, height * scale.Y, 0, i % 10 == 0 ? 2u : 0u, (uint)random.Next()));
        }
        return rows;
    }

    private static void Save(string path, byte[] bytes)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write) ?? throw new IOException($"cannot write {path}: {FileAccess.GetOpenError()}");
        file.StoreBuffer(bytes);
    }
}

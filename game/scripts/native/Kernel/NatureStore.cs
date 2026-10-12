using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace EnFractal.Native.Kernel;

public sealed class NatureStateException(string message) : Exception(message);
public interface INatureStore
{
    NatureState Load(string roomId, string manifest);
    void Save(NatureState state);
}

/// <summary>Atomic sidecar, including weather, ground, object damage, undo delta and durable receipts.</summary>
public sealed class LocalNatureStore(string path) : INatureStore
{
    public const int MaxBytes = 8 * 1024 * 1024;
    private readonly string _path = Path.GetFullPath(path);
    private byte[]? _seen;
    private bool _loaded;
    public NatureState Load(string roomId, string manifest)
    {
        try
        {
            var bytes = Read();
            var state = bytes == null ? NatureState.Fresh(roomId, manifest) : Parse(bytes, roomId, manifest);
            _seen = bytes; _loaded = true; return state;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { throw new NatureStateException("Nature could not be loaded. The saved file has been kept."); }
    }
    public void Save(NatureState state)
    {
        if (!_loaded) throw new NatureStateException("Nature must load before it can be saved.");
        string? temp = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            using var lease = new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var disk = Read();
            if (!(_seen == null ? disk == null : disk != null && _seen.AsSpan().SequenceEqual(disk)))
                throw new NatureStateException("Another session changed nature. Reload the room before continuing.");
            var bytes = JsonSerializer.SerializeToUtf8Bytes(state, NatureState.JsonOptions);
            _ = Parse(bytes, state.RoomId, state.ManifestSha256);
            temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes); stream.Flush(true); }
            File.Move(temp, _path, true); temp = null; _seen = bytes;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { throw new NatureStateException("Nature could not be saved. Nothing from this change was applied."); }
        finally { if (temp != null) try { File.Delete(temp); } catch (IOException) { } }
    }
    private byte[]? Read()
    {
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length <= 0 || stream.Length > MaxBytes) throw new NatureStateException("The nature save has an invalid size.");
            var bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes); return bytes;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
    public static NatureState Parse(byte[] bytes, string roomId, string manifest)
    {
        try
        {
            if (bytes.Length is <= 0 or > MaxBytes) throw new JsonException();
            new UTF8Encoding(false, true).GetCharCount(bytes);
            using var strict = CanonicalJson.Parse(bytes);
            var required = new[] { "schema", "version", "room_id", "manifest_sha256", "revision", "weather_s", "trail_enabled", "trail_principal", "trail_rules_sha256", "cells", "objects", "wallet", "fire", "undo", "receipts" };
            if (strict.RootElement.ValueKind != JsonValueKind.Object || strict.RootElement.EnumerateObject().Count() != required.Length || required.Any(k => !strict.RootElement.TryGetProperty(k, out _))) throw new JsonException();
            var s = JsonSerializer.Deserialize<NatureState>(bytes, NatureState.JsonOptions) ?? throw new JsonException();
            if (s.Schema != "enfractal.nature_state" || s.Version != 1 || s.RoomId != roomId || s.ManifestSha256 != manifest || s.Revision < 0 || !double.IsFinite(s.WeatherS) || s.WeatherS is < 0 or > 1e10 || s.Cells == null || s.Cells.Count > NatureState.MaxCells || s.Objects == null || s.Objects.Count > 4096 || s.Receipts == null || s.Receipts.Count > NatureState.MaxReceipts || s.Wallet == null || s.Wallet.Count > 32 || s.Wallet.Any(p => p.Value is < 0 or > 100000)) throw new JsonException();
            foreach (var (key, c) in s.Cells) if (c == null || c.Key != key || !ValidCell(c)) throw new JsonException();
            if (!ValidTrail(s.TrailPrincipal, s.TrailRulesSha256) || s.Undo is { } trailUndo && !ValidTrail(trailUndo.TrailPrincipal, trailUndo.TrailRulesSha256)) throw new JsonException();
            if (s.Undo is { } ownedUndo && ownedUndo.Principal is not (CommandHost.PlayerPrincipal or CommandHost.CompanionPrincipal)) throw new JsonException();
            if (s.Cells.Values.Count(c => c.BurningS > 0) > NatureState.MaxBurning) throw new JsonException();
            foreach (var (key, o) in s.Objects) if (o == null || o.Id != key || !ValidObject(o)) throw new JsonException();
            if (s.Fire is { } f && (!float.IsFinite(f.X) || !float.IsFinite(f.Y) || !float.IsFinite(f.Z) || !float.IsFinite(f.RadiusM) || f.RadiusM is <= 0 or > NatureState.MaxFireRadiusM || !float.IsFinite(f.RemainingS) || f.RemainingS is < 0 or > 60 || f.AuthorityRevision < 0 || f.AuthorizedCreations == null || f.AuthorizedCreations.Count > 64)) throw new JsonException();
            if (s.Undo is { } u && (u.NatureRevision != s.Revision || u.AuthorityRevision < 0 || u.Cells == null || u.Cells.Count > NatureState.MaxCells || u.Objects == null || u.Objects.Count > 4096 || u.Wallet == null || u.Wallet.Count > 32 || u.Wallet.Any(p => p.Value is < 0 or > 100000) || u.Cells.Any(p => p.Value != null && (p.Key != p.Value.Key || !ValidCell(p.Value))) || u.Objects.Any(p => p.Value != null && (p.Key != p.Value.Id || !ValidObject(p.Value))))) throw new JsonException();
            foreach (var (key, record) in s.Receipts) if (key.Length > 100 || record["fingerprint"]?.GetValue<string>() is not { Length: 64 } || record["result"] is not System.Text.Json.Nodes.JsonObject) throw new JsonException();
            return s;
        }
        catch (Exception e) when (e is JsonException or CanonicalJsonException or DecoderFallbackException or InvalidOperationException or FormatException or NullReferenceException)
        { throw new NatureStateException("The nature save is invalid or belongs to another room. It was kept unchanged."); }
    }
    private static bool Unit(float value) => float.IsFinite(value) && value is >= 0 and <= 1;
    private static bool ValidTrail(string principal, string hash) => principal is "player" or "companion" && hash != null && hash.Length is 0 or 64;
    private static bool ValidObject(NatureObject o) => (o.Id.StartsWith("obj:") || o.Id.StartsWith("creation:")) && o.Id.Length <= 80 && o.Creation == o.Id.StartsWith("creation:") && Unit(o.Burn) && o.RepairCost != null && o.RepairCost.Count <= 32 && !o.RepairCost.Any(p => p.Key.Length is < 1 or > 40 || p.Value is < 1 or > 100000) && (o.Creation ? o.SourceHash.Length == 64 : o.SourceHash.Length == 0);
    private static bool ValidCell(NatureCell c) => Math.Abs((long)c.X) <= 20000 && Math.Abs((long)c.Z) <= 20000 && float.IsFinite(c.Y) && Math.Abs(c.Y) <= 1000 && Unit(c.Moisture) && Unit(c.Growth) && Unit(c.Fuel) && Unit(c.Ash) && float.IsFinite(c.LifeS) && c.LifeS is >= 0 and <= 600 && float.IsFinite(c.BurningS) && c.BurningS is >= 0 and <= 60 && float.IsFinite(c.Light) && c.Light is >= 0 and <= 2 && c.Surface.Length <= 80 && c.Material.Length <= 40;
}

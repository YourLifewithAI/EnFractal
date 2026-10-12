using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace EnFractal.Native.Kernel;

/// <summary>A fixed, player-safe error: saved text and filesystem paths never become Gubble dialogue.</summary>
public sealed class GlowProgressException(string message) : Exception(message);

/// <summary>
/// Host-owned progress for the first single-player Glow trial. This is independent of room saves and
/// physical player traits. Only completed host observations may advance it; model prose is not evidence.
/// The other four discovery IDs reserve the save vocabulary for their later, implemented behaviours.
/// </summary>
public sealed class GlowProgress
{
    public const int MaxSuccessfulCasts = 1_000_000;
    public const int HintAfterCasts = 10;
    public const string Lantern = "lantern";
    public const string Torch = "torch";
    public const string Bioluminesce = "bioluminesce";
    public const string Constellation = "constellation";
    public const string Skylight = "skylight";
    public const string LanternHint = "lantern_self";

    private static readonly string[] DiscoveryOrder = { Lantern, Torch, Bioluminesce, Constellation, Skylight };
    private static readonly string[] HintOrder = { LanternHint };

    public static GlowProgress Fresh { get; } = new(0, Array.Empty<string>(), Array.Empty<string>());
    public int SuccessfulCasts { get; }
    public IReadOnlyList<string> Discoveries { get; }
    public IReadOnlyList<string> DeliveredHints { get; }
    public int ActiveLightAllowance => Discoveries.Count >= 5 ? 5 : Discoveries.Count >= 2 ? 4 : 3;

    private GlowProgress(int successfulCasts, string[] discoveries, string[] deliveredHints)
    {
        SuccessfulCasts = successfulCasts;
        Discoveries = new ReadOnlyCollection<string>(discoveries);
        DeliveredHints = new ReadOnlyCollection<string>(deliveredHints);
    }

    public bool HasDiscovery(string id) => Discoveries.Contains(id, StringComparer.Ordinal);

    /// <summary>
    /// Builds a candidate only. The host must save it successfully before publishing it. A self Glow is
    /// Lantern; point Glow is counted but does not pretend that an unimplemented variant was discovered.
    /// A nudge can mention only the implemented Lantern behaviour and is remembered once across reloads.
    /// </summary>
    public GlowProgress AfterCast(bool lantern)
    {
        var count = Math.Min(MaxSuccessfulCasts, SuccessfulCasts + 1);
        var discoveries = Discoveries.ToList();
        var hints = DeliveredHints.ToList();
        if (lantern && !HasDiscovery(Lantern)) discoveries.Add(Lantern);
        if (count >= HintAfterCasts && !discoveries.Contains(Lantern) && !hints.Contains(LanternHint))
            hints.Add(LanternHint);
        if (count == SuccessfulCasts && discoveries.Count == Discoveries.Count && hints.Count == DeliveredHints.Count)
            return this;
        return Restore(count, discoveries, hints);
    }

    /// <summary>Validated restoration for the host's local save, never a command or companion input.</summary>
    public static GlowProgress Restore(int successfulCasts, IEnumerable<string> discoveries, IEnumerable<string> deliveredHints)
    {
        if (successfulCasts < 0 || successfulCasts > MaxSuccessfulCasts)
            throw new GlowProgressException("Glow progress has an invalid use count.");
        var found = ValidateIds(discoveries, DiscoveryOrder, "Glow progress has invalid discoveries.");
        var hints = ValidateIds(deliveredHints, HintOrder, "Glow progress has invalid hint records.");
        if (successfulCasts < found.Length || (hints.Length > 0 && successfulCasts < HintAfterCasts))
            throw new GlowProgressException("Glow progress has inconsistent use records.");
        return new GlowProgress(successfulCasts, found, hints);
    }

    private static string[] ValidateIds(IEnumerable<string> values, string[] known, string error)
    {
        if (values == null) throw new GlowProgressException(error);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
            if (value == null || !known.Contains(value, StringComparer.Ordinal) || !seen.Add(value))
                throw new GlowProgressException(error);
        return known.Where(seen.Contains).ToArray();
    }
}

/// <summary>Injectable storage keeps tests isolated and lets the host stage a cast before publishing progress.</summary>
public interface IGlowProgressStore
{
    GlowProgress Load();
    void Save(GlowProgress state);
}

/// <summary>
/// A small, strict, versioned local profile. Load errors lock this instance against writes, preserving the
/// original file. A persistent sidecar lease excludes other store writers across game instances, then saves
/// replace a flushed temporary file atomically. A byte comparison detects external changes optimistically;
/// unrelated tools that do not honour the lease are not coordinated writers.
/// No raw names, hints, dialogue or other free text is stored here.
/// </summary>
public sealed class LocalGlowProgressStore : IGlowProgressStore
{
    public const string DefaultUserPath = "user://single_player/progression/glow_trial_v1.json";
    public const string SchemaId = "enfractal.glow_progress";
    public const int Version = 1;
    public const int MaxSaveBytes = 4096;
    private static readonly string[] Fields = { "schema", "version", "successful_casts", "discoveries", "delivered_hints" };
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly string _path;
    private readonly object _gate = new();
    private bool _loaded;
    private bool _readOnly;
    private byte[]? _lastBytes;

    public bool IsReadOnly { get { lock (_gate) return _readOnly; } }

    /// <param name="absolutePath">The host globalizes DefaultUserPath; tests supply their own absolute scratch path.</param>
    public LocalGlowProgressStore(string absolutePath)
    {
        try
        {
            if (string.IsNullOrEmpty(absolutePath) || !Path.IsPathFullyQualified(absolutePath))
                throw new GlowProgressException("Glow progress needs an absolute local save path.");
            _path = Path.GetFullPath(absolutePath);
            if (string.IsNullOrEmpty(Path.GetFileName(_path)) || string.IsNullOrEmpty(Path.GetDirectoryName(_path)))
                throw new GlowProgressException("Glow progress needs a local save filename.");
        }
        catch (Exception error) when (IsFileError(error) || error is ArgumentException or NotSupportedException)
        {
            throw new GlowProgressException("Glow progress has an invalid local save path.");
        }
    }

    public GlowProgress Load()
    {
        lock (_gate)
        {
            try
            {
                var bytes = ReadBytes();
                var state = bytes == null ? GlowProgress.Fresh : Parse(bytes);
                _lastBytes = bytes;
                _loaded = true;
                _readOnly = false;
                return state;
            }
            catch (GlowProgressException)
            {
                _readOnly = true;
                throw;
            }
            catch (Exception error) when (IsFileError(error))
            {
                _readOnly = true;
                throw new GlowProgressException("Glow progress could not be read. The existing save was kept.");
            }
        }
    }

    public void Save(GlowProgress state)
    {
        if (state == null) throw new GlowProgressException("Glow progress is missing.");
        lock (_gate)
        {
            if (!_loaded || _readOnly)
                throw new GlowProgressException("Glow progress is read-only until its save can be loaded.");
            string? temporary = null;
            try
            {
                var directory = Path.GetDirectoryName(_path)!;
                Directory.CreateDirectory(directory);
                // Never delete this file: removing a released lockfile can split waiting writers onto
                // different files. FileShare.None fails immediately if another game instance holds it.
                using var lease = new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                var current = ReadBytes();
                if (!SameBytes(current, _lastBytes))
                {
                    _readOnly = true;
                    throw new GlowProgressException("Glow progress changed outside this session. Reload it before continuing.");
                }
                var bytes = Serialize(state);
                temporary = Path.Combine(directory, Path.GetFileName(_path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporary, _path, overwrite: true);
                temporary = null;
                _lastBytes = bytes;
            }
            catch (Exception error) when (IsFileError(error))
            {
                throw new GlowProgressException("Glow progress could not be saved. The previous progress was kept.");
            }
            finally
            {
                if (temporary != null)
                {
                    try { File.Delete(temporary); }
                    catch (Exception error) when (IsFileError(error)) { }
                }
            }
        }
    }

    public static GlowProgress Parse(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0 || bytes.Length > MaxSaveBytes)
            throw new GlowProgressException("Glow progress has an invalid file size.");
        JsonDocument document;
        try
        {
            // Utf8JsonReader can defer validating string bytes until a value is accessed. Check the
            // complete buffer here, so malformed UTF-8 always takes the same safe load-error path.
            StrictUtf8.GetCharCount(bytes);
            document = CanonicalJson.Parse(bytes);
        }
        catch (DecoderFallbackException) { throw new GlowProgressException("Glow progress is not valid UTF-8."); }
        catch (CanonicalJsonException) { throw new GlowProgressException("Glow progress is not a valid save file."); }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != Fields.Length ||
                root.EnumerateObject().Any(property => !Fields.Contains(property.Name, StringComparer.Ordinal)))
                throw new GlowProgressException("Glow progress has unsupported fields.");
            if (root.GetProperty("schema").ValueKind != JsonValueKind.String || root.GetProperty("schema").GetString() != SchemaId)
                throw new GlowProgressException("Glow progress has an unsupported save format.");
            var version = root.GetProperty("version");
            if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var versionNumber) || versionNumber != Version)
                throw new GlowProgressException("Glow progress has an unsupported save version.");
            var casts = root.GetProperty("successful_casts");
            if (casts.ValueKind != JsonValueKind.Number || !casts.TryGetInt32(out var count))
                throw new GlowProgressException("Glow progress has an invalid use count.");
            return GlowProgress.Restore(count, Ids(root.GetProperty("discoveries"), 5), Ids(root.GetProperty("delivered_hints"), 1));
        }
    }

    public static byte[] Serialize(GlowProgress state)
    {
        // Serialize emits UTF-8 without a BOM; the one final newline is always LF, including on Windows.
        var json = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = SchemaId,
            version = Version,
            successful_casts = state.SuccessfulCasts,
            discoveries = state.Discoveries,
            delivered_hints = state.DeliveredHints,
        });
        var bytes = new byte[json.Length + 1];
        json.CopyTo(bytes, 0);
        bytes[^1] = (byte)'\n';
        return bytes;
    }

    private byte[]? ReadBytes()
    {
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length is <= 0 or > MaxSaveBytes)
                throw new GlowProgressException("Glow progress has an invalid file size.");
            var bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            return bytes;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private static string[] Ids(JsonElement value, int maximum)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > maximum ||
            value.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
            throw new GlowProgressException("Glow progress has invalid discovery or hint records.");
        return value.EnumerateArray().Select(item => item.GetString()!).ToArray();
    }

    private static bool SameBytes(byte[]? first, byte[]? second) =>
        first == null ? second == null : second != null && first.AsSpan().SequenceEqual(second);

    private static bool IsFileError(Exception error) => error is IOException or UnauthorizedAccessException or System.Security.SecurityException;
}

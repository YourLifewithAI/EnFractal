using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace EnFractal.Native.Kernel;

public enum PlayerDamageSource { Fall, Fire, Critter, Player }
public sealed class PlayerVitalsException(string message) : Exception(message);

/// <summary>Prototype physical traits for one mode. Stamina is measured in equivalent metres of climbing.</summary>
public sealed class PlayerVitals
{
    public const float MaximumHealth = 100;
    public const float InitialStamina = 0.45f;
    public const float MaximumStamina = 3.0f;
    public const float StaminaReward = 0.10f;
    public const float GroundRecoveryPerSecond = 0.10f;
    public const float HoldingCostPerSecond = 0.01f;
    public const float MinimumChallengeRise = 0.25f;
    public const float SafeFallHeight = 0.30f;
    /// <summary>Free fall through SafeFallHeight under world_physics_profile.gd DEFAULT's 3.5 m/s² gravity.</summary>
    public static readonly float SafeLandingSpeedMps = MathF.Sqrt(2 * 3.5f * SafeFallHeight);
    /// <summary>A default-gravity 1.5 m fall lands at about 3.24 m/s and costs about 96 health.</summary>
    public const float FallDamagePerMps = 53.6f;
    public const int MaximumChallenges = 256;
    public float Health { get; }
    public float MaxHealth => MaximumHealth;
    public float Stamina { get; }
    public float MaxStamina => Math.Min(MaximumStamina, InitialStamina + Challenges.Count * StaminaReward);
    public IReadOnlyList<string> Challenges { get; }
    public int CompletedClimbs => Challenges.Count;
    public int Recoveries { get; }
    public static PlayerVitals Fresh { get; } = new(MaximumHealth, InitialStamina, Array.Empty<string>(), 0);

    private PlayerVitals(float health, float stamina, string[] challenges, int recoveries)
    {
        Health = health;
        Stamina = stamina;
        Challenges = Array.AsReadOnly(challenges);
        Recoveries = recoveries;
    }

    public static PlayerVitals Restore(float health, float stamina, IEnumerable<string> challenges, int recoveries)
    {
        var entries = challenges?.ToArray() ?? throw new PlayerVitalsException("Player traits have no challenge records.");
        if (entries.Length > MaximumChallenges || entries.Any(id => id == null || id.Length != 64 || id.Any(c => !Uri.IsHexDigit(c))) ||
            entries.Distinct(StringComparer.Ordinal).Count() != entries.Length || recoveries < 0 || recoveries > 1_000_000 ||
            !float.IsFinite(health) || health < 0 || health > MaximumHealth || !float.IsFinite(stamina) || stamina < 0 ||
            stamina > Math.Min(MaximumStamina, InitialStamina + entries.Length * StaminaReward) + 0.00001f)
            throw new PlayerVitalsException("Player traits contain invalid values.");
        return new PlayerVitals(health, stamina, entries.OrderBy(id => id, StringComparer.Ordinal).ToArray(), recoveries);
    }

    public PlayerVitals SpendClimb(float seconds, float rise, out bool enough)
    {
        if (!float.IsFinite(seconds) || !float.IsFinite(rise) || seconds < 0 || rise < 0)
            throw new PlayerVitalsException("Climbing needs a finite duration and rise.");
        var cost = Math.Max(rise, HoldingCostPerSecond * seconds);
        enough = Stamina + 0.000001f >= cost && Stamina > 0;
        return new PlayerVitals(Health, Math.Max(0, Stamina - cost), Challenges.ToArray(), Recoveries);
    }

    public PlayerVitals Rest(float seconds, bool heal)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new PlayerVitalsException("Rest needs a finite duration.");
        return new PlayerVitals(Math.Min(MaximumHealth, Health + (heal ? 2f * seconds : 0)),
            Math.Min(MaxStamina, Stamina + GroundRecoveryPerSecond * seconds), Challenges.ToArray(), Recoveries);
    }

    public PlayerVitals Damage(float amount)
    {
        if (!float.IsFinite(amount) || amount < 0) throw new PlayerVitalsException("Damage needs a finite nonnegative amount.");
        return new PlayerVitals(Math.Max(0, Health - amount), Stamina, Challenges.ToArray(), Recoveries);
    }

    /// <summary>Recovery costs position, half health and half stamina; earned traits and creations are kept.</summary>
    public PlayerVitals Recovered() => new(MaximumHealth * 0.5f, MaxStamina * 0.5f, Challenges.ToArray(), Math.Min(1_000_000, Recoveries + 1));

    public PlayerVitals CompleteClimb(string challenge)
    {
        if (Challenges.Contains(challenge, StringComparer.Ordinal) || Challenges.Count >= MaximumChallenges) return this;
        // A reward raises capacity, not the remaining energy, so it cannot sustain a hanging climb indefinitely.
        return Restore(Health, Stamina, Challenges.Append(challenge), Recoveries);
    }

    /// <summary>Injury from downward landing speed in m/s; deep water catches the body without injury.</summary>
    public static float FallDamage(float landingSpeedMps, bool deepWater) =>
        !float.IsFinite(landingSpeedMps) || landingSpeedMps < 0 || deepWater ? 0 :
            Math.Clamp((landingSpeedMps - SafeLandingSpeedMps) * FallDamagePerMps, 0, MaximumHealth);
}

/// <summary>One local player, three independent mode records. Mode changes never refill a saved mode's vitals.</summary>
public sealed class PlayerModesProfile
{
    public static readonly IReadOnlyList<string> Modes = Array.AsReadOnly(new[] { "creative", "challenge", "live" });
    public string Mode { get; }
    private readonly Dictionary<string, PlayerVitals> _modes;
    public PlayerVitals Current => For(Mode);
    public static PlayerModesProfile Fresh => new("creative", Modes.ToDictionary(mode => mode, _ => PlayerVitals.Fresh));
    private PlayerModesProfile(string mode, Dictionary<string, PlayerVitals> modes) { Mode = mode; _modes = modes; }
    public PlayerVitals For(string mode) => _modes[mode];
    public static bool ValidMode(string mode) => Modes.Contains(mode, StringComparer.Ordinal);
    public static PlayerModesProfile Restore(string mode, IReadOnlyDictionary<string, PlayerVitals> modes)
    {
        if (!ValidMode(mode) || modes.Count != 3 || Modes.Any(name => !modes.TryGetValue(name, out var value) || value == null))
            throw new PlayerVitalsException("The player profile has unsupported modes.");
        return new PlayerModesProfile(mode, modes.ToDictionary(pair => pair.Key, pair => pair.Value));
    }
    public PlayerModesProfile Select(string mode) => Restore(mode, _modes);
    public PlayerModesProfile WithCurrent(PlayerVitals value)
    {
        var modes = new Dictionary<string, PlayerVitals>(_modes) { [Mode] = value };
        return new PlayerModesProfile(Mode, modes);
    }
}

public interface IPlayerVitalsStore
{
    PlayerModesProfile Load();
    void Save(PlayerModesProfile profile);
}

/// <summary>Strict bounded profile with an exclusive writer lease and atomic replacement. Bad saves are preserved.</summary>
public sealed class LocalPlayerVitalsStore : IPlayerVitalsStore
{
    public const string DefaultUserPath = "user://single_player/progression/player_modes_v1.json";
    public const int MaxBytes = 64 * 1024;
    private readonly string _path;
    private byte[]? _baseline;
    private bool _loaded;
    private bool _readOnly;
    private readonly object _gate = new();
    public LocalPlayerVitalsStore(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new PlayerVitalsException("Player traits need an absolute local path.");
        try { _path = Path.GetFullPath(path); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException) { throw new PlayerVitalsException("Player traits have an invalid save path."); }
    }

    public PlayerModesProfile Load()
    {
        lock (_gate)
        {
            try
            {
                var bytes = Read();
                var profile = bytes == null ? PlayerModesProfile.Fresh : Parse(bytes);
                _baseline = bytes;
                _loaded = true;
                _readOnly = false;
                return profile;
            }
            catch (Exception error) when (error is PlayerVitalsException || FileError(error))
            {
                _readOnly = true;
                throw new PlayerVitalsException("Player traits could not be loaded. The existing save was kept.");
            }
        }
    }

    public void Save(PlayerModesProfile profile)
    {
        lock (_gate)
        {
            if (!_loaded || _readOnly) throw new PlayerVitalsException("Player traits are waiting for their save to be loaded.");
            string? temporary = null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                using var lease = new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                var current = Read();
                if (!(current == null ? _baseline == null : _baseline != null && current.AsSpan().SequenceEqual(_baseline)))
                {
                    _readOnly = true;
                    throw new PlayerVitalsException("Player traits changed in another session. Reload before saving.");
                }
                var bytes = Serialize(profile);
                temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes); stream.Flush(flushToDisk: true); }
                File.Move(temporary, _path, overwrite: true);
                temporary = null;
                _baseline = bytes;
            }
            catch (Exception error) when (FileError(error)) { throw new PlayerVitalsException("Player traits could not be saved. The previous save was kept."); }
            finally { if (temporary != null) try { File.Delete(temporary); } catch (Exception error) when (FileError(error)) { } }
        }
    }

    private byte[]? Read()
    {
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length <= 0 || stream.Length > MaxBytes) throw new PlayerVitalsException("Player traits have an invalid file size.");
            var bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            return bytes;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public static byte[] Serialize(PlayerModesProfile profile)
    {
        var modes = PlayerModesProfile.Modes.ToDictionary(mode => mode, mode => new
        {
            health = profile.For(mode).Health, stamina = profile.For(mode).Stamina,
            challenges = profile.For(mode).Challenges, recoveries = profile.For(mode).Recoveries,
        });
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { schema = "enfractal.player_modes", version = 1, mode = profile.Mode, modes }) + "\n");
    }

    public static PlayerModesProfile Parse(byte[] bytes)
    {
        try
        {
            if (bytes.Length == 0 || bytes.Length > MaxBytes) throw new PlayerVitalsException("Player traits have an invalid file size.");
            new UTF8Encoding(false, true).GetCharCount(bytes);
            using var document = CanonicalJson.Parse(bytes);
            var root = document.RootElement;
            Fields(root, "schema", "version", "mode", "modes");
            if (root.GetProperty("schema").GetString() != "enfractal.player_modes" || root.GetProperty("version").GetInt32() != 1)
                throw new PlayerVitalsException("Player traits have an unsupported version.");
            var modes = root.GetProperty("modes");
            Fields(modes, "creative", "challenge", "live");
            var values = new Dictionary<string, PlayerVitals>();
            foreach (var mode in PlayerModesProfile.Modes)
            {
                var value = modes.GetProperty(mode);
                Fields(value, "health", "stamina", "challenges", "recoveries");
                var challenges = value.GetProperty("challenges");
                if (challenges.ValueKind != JsonValueKind.Array || challenges.GetArrayLength() > PlayerVitals.MaximumChallenges)
                    throw new PlayerVitalsException("Player traits have invalid challenges.");
                values[mode] = PlayerVitals.Restore(value.GetProperty("health").GetSingle(), value.GetProperty("stamina").GetSingle(),
                    challenges.EnumerateArray().Select(item => item.GetString()!), value.GetProperty("recoveries").GetInt32());
            }
            return PlayerModesProfile.Restore(root.GetProperty("mode").GetString()!, values);
        }
        catch (Exception error) when (error is CanonicalJsonException or JsonException or InvalidOperationException or FormatException or OverflowException or DecoderFallbackException)
        { throw new PlayerVitalsException("Player traits are not a valid save file."); }
    }

    private static void Fields(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != names.Length ||
            value.EnumerateObject().Any(field => !names.Contains(field.Name, StringComparer.Ordinal)))
            throw new PlayerVitalsException("Player traits have unsupported fields.");
    }
    private static bool FileError(Exception error) => error is IOException or UnauthorizedAccessException or System.Security.SecurityException;
}

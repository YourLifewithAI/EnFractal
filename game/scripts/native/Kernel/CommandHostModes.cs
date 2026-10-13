using Godot;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EnFractal.Native.Kernel;

/// <summary>What one hurt did: the player's health before and after it, and whether it put them down (an injury recovery at the spawn).</summary>
public readonly record struct PlayerHarm(float Before, float After, bool Recovered);

/// <summary>The trusted body reports physical observations; commands and model text cannot award traits or set health.</summary>
public partial class CommandHost
{
    public static string DefaultPlayerVitalsPath { get; set; } = LocalPlayerVitalsStore.DefaultUserPath;
    public string Mode => _playerModes.Mode;
    public PlayerVitals Vitals => _playerModes.Current;
    public string VitalsNotice { get; private set; } = "";
    public PlayerDamageSource? LastDamageSource { get; private set; }
    private PlayerModesProfile _playerModes = PlayerModesProfile.Fresh;
    private IPlayerVitalsStore? _playerVitalsStore;
    private bool _playerVitalsUnavailable;
    private bool _vitalsDirty;
    private double _vitalsSaveElapsed;
    private float _damageRestS;
    private float _recoveryGraceS;
    /// <summary>After an injury recovery, fire cannot hurt the player for NatureLaws.RecoveryFireGraceS (counted in nature steps).</summary>
    private float _fireGraceS;
    /// <summary>Where the player last recovered: its ground stays out of the fire while the grace lasts.</summary>
    private Vector3? _recoveredAt;
    private Func<float, float, bool>? _priorClimbBudget;
    private Action<float, bool>? _priorVitalsTick;
    private Action<float, bool>? _priorFallObserved;
    private Action<Vector3, Vector3>? _priorClimbCompleted;

    public void ConfigurePlayerVitals(IPlayerVitalsStore store)
    {
        _playerVitalsStore = store;
        try
        {
            _playerModes = store.Load();
            _playerVitalsUnavailable = false;
            _vitalsDirty = false;
            _damageRestS = 0;
            _recoveryGraceS = 0;
            _fireGraceS = 0;
            _vitalsSaveElapsed = 0;
            VitalsNotice = "";
            Player?.ResetVitalsObservation();
        }
        catch (PlayerVitalsException)
        {
            _playerVitalsUnavailable = true;
            VitalsNotice = "Player traits could not be loaded. The saved profile has been kept; changing mode is waiting for recovery.";
        }
    }

    /// <summary>Called once by the host after its player exists. Fixture hosts restore the previous owner's callbacks on exit.</summary>
    public void BindPlayerVitals()
    {
        if (Player == null || !IsInstanceValid(Player)) return;
        _priorClimbBudget = Player.ClimbBudget;
        _priorVitalsTick = Player.VitalsTick;
        _priorFallObserved = Player.FallObserved;
        _priorClimbCompleted = Player.ClimbCompleted;
        Player.ClimbBudget = SpendPlayerClimb;
        Player.VitalsTick = TickPlayerVitals;
        Player.FallObserved = ObservePlayerFall;
        Player.ClimbCompleted = ObservePlayerClimb;
        Player.ResetVitalsObservation();
    }

    public void UnbindPlayerVitals()
    {
        FlushPlayerVitals();
        if (Player == null || !IsInstanceValid(Player)) return;
        if (Player.ClimbBudget == SpendPlayerClimb) Player.ClimbBudget = _priorClimbBudget;
        if (Player.VitalsTick == TickPlayerVitals) Player.VitalsTick = _priorVitalsTick;
        if (Player.FallObserved == ObservePlayerFall) Player.FallObserved = _priorFallObserved;
        if (Player.ClimbCompleted == ObservePlayerClimb) Player.ClimbCompleted = _priorClimbCompleted;
    }

    public JsonObject PlayerMode(string mode) => PlayerCommand("world.set_mode", new JsonObject { ["mode"] = mode });

    private JsonObject SetMode(JsonElement args, string principal, string actionId, string fingerprint, bool preview)
    {
        if (principal != PlayerPrincipal) throw new Refusal("permission_denied", "Only the player chooses the game mode.");
        var mode = Str(args, "mode")!;
        if (!PlayerModesProfile.ValidMode(mode))
            throw new Refusal("invalid_args", "Choose creative, challenge or live.", "$.args.mode");
        if (_playerVitalsUnavailable) throw new Refusal("not_ready", VitalsNotice);
        if (preview) return Previewed("world.set_mode", principal, actionId);
        var glowMode = PrepareGlowMode(mode);
        var next = _playerModes.Select(mode);
        if (!TrySaveVitals(next)) throw new Refusal("not_ready", VitalsNotice, retryable: true);
        _playerModes = next;
        CommitGlowMode(glowMode);
        _vitalsDirty = false;
        _damageRestS = 0;
        LastDamageSource = null;
        Player?.ResetVitalsObservation();
        return Transient("world.set_mode", principal, actionId, fingerprint, new JsonArray(), null);
    }

    /// <summary>The data object for the read-only player.status query; no hidden challenge identities are exposed.</summary>
    private JsonObject PlayerStatus() => new()
    {
        ["mode"] = Mode, ["health"] = Vitals.Health, ["max_health"] = Vitals.MaxHealth,
        ["stamina"] = Vitals.Stamina, ["max_stamina"] = Vitals.MaxStamina,
        ["completed_climbs"] = Vitals.CompletedClimbs, ["recoveries"] = Vitals.Recoveries,
        ["consequences"] = Mode != "creative",
    };

    private bool SpendPlayerClimb(float seconds, float rise)
    {
        if (Mode == "creative") return true;
        if (_playerVitalsUnavailable || Vitals.Health <= 0) return false;
        if (seconds == 0 && rise == 0) return Vitals.Stamina > 0;
        var spent = Vitals.SpendClimb(seconds, rise, out var enough);
        _playerModes = _playerModes.WithCurrent(spent);
        _vitalsDirty = true;
        return enough;
    }

    private void TickPlayerVitals(float seconds, bool safelyGrounded)
    {
        if (Mode == "creative" || _playerVitalsUnavailable) return;
        _damageRestS += seconds;
        _recoveryGraceS = Math.Max(0, _recoveryGraceS - seconds);
        if (Vitals.Health <= 0) RecoverPlayerFromInjury();
        if (safelyGrounded && (Vitals.Stamina < Vitals.MaxStamina || (_damageRestS >= 10 && Vitals.Health < Vitals.MaxHealth)))
        {
            _playerModes = _playerModes.WithCurrent(Vitals.Rest(seconds, heal: _damageRestS >= 10));
            _vitalsDirty = true;
        }
        _vitalsSaveElapsed += seconds;
        if (_vitalsDirty && _vitalsSaveElapsed >= 2)
        {
            _vitalsSaveElapsed = 0;
            FlushPlayerVitals();
        }
    }

    public void FlushPlayerVitals()
    {
        if (!_vitalsDirty || _playerVitalsUnavailable) return;
        if (TrySaveVitals(_playerModes)) _vitalsDirty = false;
    }

    private bool TrySaveVitals(PlayerModesProfile profile)
    {
        try { _playerVitalsStore?.Save(profile); VitalsNotice = ""; return true; }
        catch (PlayerVitalsException)
        {
            VitalsNotice = "Player traits could not be saved. Play continues with this session's state; the previous save has been kept.";
            return false;
        }
    }

    /// <summary>
    /// Trusted hazard hook only: Fire, future critters and future PvP use the same mode rules. No AI damage-setting command
    /// exists. Creative never hurts; nothing hurts just after a recovery, and fire not for its longer grace. Returns what the
    /// hurt did (null when it did nothing), so the host can tell the Gubble what its own fire did.
    /// </summary>
    public PlayerHarm? ApplyPlayerDamage(float amount, PlayerDamageSource source)
    {
        if (!Enum.IsDefined(source) || !float.IsFinite(amount) || amount <= 0 || Mode == "creative" || _playerVitalsUnavailable || _recoveryGraceS > 0) return null;
        if (source == PlayerDamageSource.Fire && _fireGraceS > 0) return null;
        var before = Vitals.Health;
        LastDamageSource = source;
        _damageRestS = 0;
        _playerModes = _playerModes.WithCurrent(Vitals.Damage(amount));
        _vitalsDirty = true;
        var after = Vitals.Health;
        var recovered = after <= 0 && RecoverPlayerFromInjury();
        FlushPlayerVitals();
        return new PlayerHarm(before, after, recovered);
    }

    private void ObservePlayerFall(float landingSpeedMps, bool deepWater) =>
        ApplyPlayerDamage(PlayerVitals.FallDamage(landingSpeedMps, deepWater), PlayerDamageSource.Fall);

    /// <summary>
    /// Injury recovery at the spawn: half health and stamina, a moment when nothing hurts and a longer one when fire cannot,
    /// and the recovery point is never inside a burning area (the ground around it is put out and stays out for the grace).
    /// </summary>
    private bool RecoverPlayerFromInjury()
    {
        if (Player == null || !IsInstanceValid(Player) || !Player.RecoverAtSpawn()) return false;
        _playerModes = _playerModes.WithCurrent(Vitals.Recovered());
        _recoveryGraceS = 1;
        _fireGraceS = NatureLaws.RecoveryFireGraceS;
        _recoveredAt = Player.GlobalPosition;
        MakeRecoverySafe(Player.GlobalPosition);
        _vitalsDirty = true;
        return true;
    }

    private void ObservePlayerClimb(Vector3 start, Vector3 finish)
    {
        if (Mode == "creative" || _playerVitalsUnavailable || !start.IsFinite() || !finish.IsFinite() || finish.Y - start.Y < PlayerVitals.MinimumChallengeRise) return;
        // A ledge at the same 10 cm destination on this room manifest is one challenge, even from another approach.
        var spot = $"{Room.ManifestSha256}:{Math.Round(finish.X * 10)}:{Math.Round(finish.Y * 10)}:{Math.Round(finish.Z * 10)}";
        var challenge = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(spot))).ToLowerInvariant();
        var earned = Vitals.CompleteClimb(challenge);
        if (ReferenceEquals(earned, Vitals)) return;
        var next = _playerModes.WithCurrent(earned);
        if (!TrySaveVitals(next)) return;
        _playerModes = next;
        _vitalsDirty = false;
    }
}

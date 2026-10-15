using Godot;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EnFractal.Native.Seams;

namespace EnFractal.Native.Kernel;

/// <summary>What one hurt did: the player's health before and after it, and whether it put them down (an injury recovery at the spawn).</summary>
public readonly record struct PlayerHarm(float Before, float After, bool Recovered);

/// <summary>
/// The trusted body reports physical observations; commands and model text cannot award traits or set health. The island's
/// rules (VitalsRules, from the pack's traits and modes blocks) say what each observation costs: harm and heal in whole
/// quarters of a heart, falls in absolute health points, fire at the pack's quarters a second, the bar's drains from the
/// stamina ledger (Seams/StaminaLedger.cs) and its refill on standable ground, never while running. A pack without those
/// blocks plays as today.
/// </summary>
public partial class CommandHost
{
    /// <summary>
    /// The vitals store's place (M3): a team's &lt;team&gt;/modes/&lt;mode&gt;/vitals.json names the team folder, whose every mode has its
    /// document. The default team's until the boot (Saves/TeamBoot.cs) points it at the team it plays; fixtures redirect it first.
    /// </summary>
    public static string DefaultPlayerVitalsPath { get; set; } = Saves.TeamLayout.UserVitalsPath(Saves.TeamLayout.DefaultTeamId, "creative");
    public string Mode => _playerModes.Mode;
    public PlayerVitals Vitals => _playerModes.Current;
    public string VitalsNotice { get; private set; } = "";
    public PlayerDamageSource? LastDamageSource { get; private set; }
    /// <summary>Whether the bar ran empty and has not refilled to the pack's fraction yet (the body is slowed meanwhile).</summary>
    public bool StaminaSlowed => _slowed;
    private PlayerModesProfile _playerModes = PlayerModesProfile.Fresh;
    private IPlayerVitalsStore? _playerVitalsStore;
    private bool _playerVitalsUnavailable;
    private bool _vitalsDirty;
    private double _vitalsSaveElapsed;
    private float _damageRestS;
    private float _recoveryGraceS;
    private float _pendingRecoveryS;
    /// <summary>After an injury recovery, fire cannot hurt the player for NatureLaws.RecoveryFireGraceS (counted in nature steps).</summary>
    private float _fireGraceS;
    /// <summary>Where the player last recovered: its ground stays out of the fire while the grace lasts.</summary>
    private Vector3? _recoveredAt;
    /// <summary>Seconds of healing toward the next whole quarter (hearts in quarters).</summary>
    private double _healS;
    /// <summary>Fire's harm toward the next whole quarter: a quarter a second at the pack's rate, the difficulty applied (Gentle: one every 2 s).</summary>
    private double _fireQuarters;
    private bool _slowed;
    /// <summary>What the body reported since the last vitals tick (the stamina ledger), charged by that tick.</summary>
    private BodyExertion _exertion;
    private KernelStaminaLedger? _ledger;
    private Func<float, float, bool>? _priorClimbBudget;
    private Action<float, bool>? _priorVitalsTick;
    private Action<float, bool>? _priorFallObserved;
    private Action<Vector3, Vector3>? _priorClimbCompleted;
    private IStaminaLedger? _priorStaminaLedger;
    private BodyKnacks? _priorKnacks;

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
            _pendingRecoveryS = 0;
            _fireGraceS = 0;
            _vitalsSaveElapsed = 0;
            VitalsNotice = "";
            ResolveVitalsRules();
            CatchUpTimeAway();
            ResetVitalsCounters();
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
        _priorStaminaLedger = Player.StaminaLedger;
        _ledger ??= new KernelStaminaLedger(this);
        Player.ClimbBudget = SpendPlayerClimb;
        Player.VitalsTick = TickPlayerVitals;
        Player.FallObserved = ObservePlayerFall;
        Player.ClimbCompleted = ObservePlayerClimb;
        Player.StaminaLedger = _ledger;
        _priorKnacks = Player.Knacks;
        ApplyBodyKnacks();
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
        if (ReferenceEquals(Player.StaminaLedger, _ledger)) Player.StaminaLedger = _priorStaminaLedger;
        if (_priorKnacks != null) Player.Knacks = _priorKnacks;
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
        // What the mode being left changed since its last save goes first: the store writes each mode's own document.
        FlushPlayerVitals();
        if (_vitalsDirty) throw new Refusal("not_ready", VitalsNotice, retryable: true);
        var glowMode = PrepareGlowMode(mode);
        var selected = _playerModes.Select(mode);
        var rules = ResolvedRules(selected.Mode);
        var normalized = rules.Normalize(selected.Current);
        var next = selected.WithCurrent(CaughtUp(rules, normalized, selected.Written(mode)));
        if (TrySaveVitals(next) is not { } saved) throw new Refusal("not_ready", VitalsNotice, retryable: true);
        _playerModes = saved;
        _vitalsRules = rules;
        CommitGlowMode(glowMode);
        _vitalsDirty = false;
        _damageRestS = 0;
        ResetVitalsCounters();
        LastDamageSource = null;
        Player?.ResetVitalsObservation();
        return Transient("world.set_mode", principal, actionId, fingerprint, new JsonArray(), null);
    }

    /// <summary>
    /// The data object for the read-only player.status query; no hidden challenge identities are exposed. With the pack's
    /// blocks it adds the pool (energy, shadow in Creative), the hearts in quarters and the bar's bands (contract: player.status).
    /// </summary>
    private JsonObject PlayerStatus()
    {
        var vitals = Vitals;
        var data = new JsonObject
        {
            ["mode"] = Mode, ["health"] = vitals.Health, ["max_health"] = vitals.MaxHealth,
            ["stamina"] = vitals.Stamina, ["max_stamina"] = vitals.MaxStamina,
            ["completed_climbs"] = vitals.CompletedClimbs, ["recoveries"] = vitals.Recoveries,
            ["consequences"] = _vitalsRules.Consequences,
        };
        if (_vitalsRules.HasPool)
            data["energy"] = new JsonObject { ["pips"] = Math.Max(0, vitals.Pips), ["max_pips"] = _vitalsRules.MaxPips, ["shadow"] = _vitalsRules.ShadowPool };
        if (_vitalsRules.InQuarters)
            data["hearts"] = new JsonObject { ["quarters"] = _vitalsRules.Quarters(vitals), ["max_quarters"] = _vitalsRules.MaxQuarters(vitals) };
        if (_vitalsRules.InBands)
            data["bar"] = new JsonObject { ["bands"] = _vitalsRules.OpenBands(vitals), ["slowed"] = _slowed };
        // The team's wallet (the island save's): what gathering put in and repairs took out (CommandHostGather).
        data["wallet"] = WalletCounts();
        return data;
    }

    private bool SpendPlayerClimb(float seconds, float rise)
    {
        if (Mode == "creative") return true;
        if (_playerVitalsUnavailable || Vitals.Health <= 0) return false;
        if (seconds == 0 && rise == 0) return Vitals.Stamina > 0;
        var spent = _vitalsRules.SpendClimb(Vitals, seconds, rise, out var enough);
        _playerModes = _playerModes.WithCurrent(spent);
        // A bar that runs empty on a climb slows the body until it refills (in bands).
        if (_vitalsRules.InBands && spent.Stamina <= 0) _slowed = true;
        _vitalsDirty = true;
        return enough;
    }

    /// <summary>The stamina ledger's report (KernelStaminaLedger): seconds the body ran, swam in deep water or dived this tick.</summary>
    internal void ReportExertion(BodyExertion exertion) =>
        _exertion = new BodyExertion(_exertion.SprintS + Positive(exertion.SprintS), _exertion.DeepWaterS + Positive(exertion.DeepWaterS), _exertion.DiveS + Positive(exertion.DiveS));

    private static float Positive(float seconds) => float.IsFinite(seconds) && seconds > 0 ? seconds : 0f;

    /// <summary>The gate the body obeys after the tick (the stamina ledger): free in Creative, without bands, or while the vitals are unavailable.</summary>
    public StaminaGate BodyGate => Mode == "creative" || _playerVitalsUnavailable ? StaminaGate.Free : _vitalsRules.Gate(Vitals, _slowed);

    private void TickPlayerVitals(float seconds, bool safelyGrounded)
    {
        var reported = _exertion;
        _exertion = BodyExertion.None;
        if (_playerVitalsUnavailable || !float.IsFinite(seconds) || seconds < 0) return;
        // What the body reported is raw seconds of this tick: never more than the tick.
        var exertion = new BodyExertion(Math.Min(reported.SprintS, seconds), Math.Min(reported.DeepWaterS, seconds), Math.Min(reported.DiveS, seconds));
        // Creative: nothing hurts or drains; the shadow pool still refills.
        if (Mode == "creative")
        {
            RefillPips(seconds);
            SaveVitalsWhenDue(seconds);
            return;
        }
        _damageRestS += seconds;
        // A knockout fall's tumble plays where the player landed before the recovery at the spawn (DelayKnockout).
        if (_pendingRecoveryS > 0)
        {
            _pendingRecoveryS = Math.Max(0, _pendingRecoveryS - seconds);
            if (_pendingRecoveryS > 0) return;
        }
        _recoveryGraceS = Math.Max(0, _recoveryGraceS - seconds);
        if (Vitals.Health <= 0) RecoverPlayerFromInjury();
        var vitals = Vitals;
        // The bar: what the body reported (running, deep water, diving) drains it first, never below empty; reaching empty slows
        // the body. Then standable ground refills it for the seconds not spent running.
        if (_vitalsRules.InBands)
        {
            var drain = _vitalsRules.Drain(exertion);
            if (drain > 0)
            {
                vitals = vitals.With(stamina: Math.Max(0, vitals.Stamina - drain));
                if (vitals.Stamina <= 0) _slowed = true;
            }
        }
        var rest = Math.Max(0, seconds - Positive(exertion.SprintS));
        var heal = _damageRestS >= _vitalsRules.HealAfterUnharmedS && vitals.Health < vitals.MaxHealth;
        if (safelyGrounded && (vitals.Stamina < vitals.MaxStamina || heal))
        {
            vitals = _vitalsRules.Rest(vitals, rest, heal);
            // In quarters the heal lands whole: one quarter every heal_quarter_every_s.
            if (heal && _vitalsRules.InQuarters)
            {
                _healS += seconds;
                while (_healS >= _vitalsRules.HealQuarterEveryS && vitals.Health < vitals.MaxHealth)
                {
                    _healS -= _vitalsRules.HealQuarterEveryS;
                    vitals = _vitalsRules.HealedQuarter(vitals);
                }
            }
        }
        if (!heal) _healS = 0;
        if (_slowed && vitals.Stamina >= _vitalsRules.SlowUntil(vitals) && vitals.Stamina > 0) _slowed = false;
        if (!ReferenceEquals(vitals, Vitals))
        {
            _playerModes = _playerModes.WithCurrent(vitals);
            _vitalsDirty = true;
        }
        RefillPips(seconds);
        SaveVitalsWhenDue(seconds);
    }

    private void SaveVitalsWhenDue(float seconds)
    {
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
        if (TrySaveVitals(_playerModes) is { } saved)
        {
            _playerModes = saved;
            _vitalsDirty = false;
        }
    }

    /// <summary>Saves the profile with its current mode stamped now; the saved profile, or null (with the notice) when the store refused.</summary>
    private PlayerModesProfile? TrySaveVitals(PlayerModesProfile profile)
    {
        var stamped = profile.Stamped(Clock());
        try { _playerVitalsStore?.Save(stamped); VitalsNotice = ""; return stamped; }
        catch (PlayerVitalsException)
        {
            VitalsNotice = "Player traits could not be saved. Play continues with this session's state; the previous save has been kept.";
            return null;
        }
    }

    /// <summary>Whether a hurt from this source could land now: never in Creative, nor just after a recovery, nor while a knockout's tumble plays; fire not for its longer grace.</summary>
    private bool CanHarm(PlayerDamageSource source) =>
        Enum.IsDefined(source) && Mode != "creative" && _vitalsRules.Consequences && !_playerVitalsUnavailable && _recoveryGraceS <= 0 && _pendingRecoveryS <= 0 &&
        !(source == PlayerDamageSource.Fire && _fireGraceS > 0);

    /// <summary>
    /// Trusted hazard hook only: Fire, future critters and future PvP use the same mode rules. No AI damage-setting command
    /// exists. Creative never hurts; nothing hurts just after a recovery, and fire not for its longer grace. The amount is in
    /// health points: in quarters it is multiplied by the difficulty and rounded to whole quarters now (a fall capped where the
    /// rules cap it), and a hurt that rounds to none does nothing. Returns what the hurt did (null when it did nothing), so the
    /// host can tell the Gubble what its own fire did.
    /// </summary>
    public PlayerHarm? ApplyPlayerDamage(float amount, PlayerDamageSource source)
    {
        if (!float.IsFinite(amount) || amount <= 0 || !CanHarm(source)) return null;
        if (!_vitalsRules.InQuarters) return Hurt(_vitalsRules.Damage(Vitals, amount), source);
        var quarters = _vitalsRules.HarmQuarters(amount, source == PlayerDamageSource.Fall);
        return quarters <= 0 ? null : Hurt(_vitalsRules.Harmed(Vitals, quarters), source);
    }

    /// <summary>Whole quarters of harm (the fire's own count): the same guards as any hurt.</summary>
    private PlayerHarm? ApplyPlayerQuarters(int quarters, PlayerDamageSource source) =>
        quarters <= 0 || !CanHarm(source) ? null : Hurt(_vitalsRules.Harmed(Vitals, quarters), source);

    private PlayerHarm? Hurt(PlayerVitals hurt, PlayerDamageSource source)
    {
        var before = Vitals.Health;
        if (hurt.Health >= before && before > 0) return null;
        LastDamageSource = source;
        _damageRestS = 0;
        _healS = 0;
        _playerModes = _playerModes.WithCurrent(hurt);
        _vitalsDirty = true;
        var after = Vitals.Health;
        var recovered = after <= 0 && !DelayKnockout(source) && RecoverPlayerFromInjury();
        FlushPlayerVitals();
        return new PlayerHarm(before, after, recovered);
    }

    /// <summary>
    /// The player stands in burning ground for this many seconds (one nature step). With the pack's harm block fire takes
    /// whole quarters at its rate, the difficulty applied, counted across steps (at a quarter a second, two steps a quarter);
    /// without it, today's NatureLaws.PlayerBurnDamage a step.
    /// </summary>
    private PlayerHarm? BurnPlayer(float seconds)
    {
        if (!CanHarm(PlayerDamageSource.Fire)) return null;
        if (_vitalsRules.Harm is not { } harm || !_vitalsRules.InQuarters) return ApplyPlayerDamage(NatureLaws.PlayerBurnDamage, PlayerDamageSource.Fire);
        _fireQuarters += harm.FireQuartersPerS * _vitalsRules.DamageMultiplier * seconds;
        var whole = (int)Math.Floor(_fireQuarters + 1e-9);
        if (whole <= 0) return null;
        _fireQuarters -= whole;
        return ApplyPlayerQuarters(whole, PlayerDamageSource.Fire);
    }

    /// <summary>A knockout fall's recovery is waiting for its tumble to finish (nothing hurts meanwhile).</summary>
    public bool RecoveryPending => _pendingRecoveryS > 0;

    /// <summary>
    /// A fall that knocks the player out waits for the knockout tumble (docs/runs/RUN-2-CHARACTERS.md, the tumble) before the
    /// recovery at the spawn moves them: the delay is the body's KnockoutRecoveryDelayS, 0 unless a tumble set it, and control
    /// is held meanwhile. The recovery then happens on the vitals tick that ends the wait. Only a body on the floor waits: a
    /// landing there is where the tumble plays (any landing that knocks out tumbles, whatever its tier), while the out-of-bounds
    /// net's fall report (the body still falling) has no tumble to wait for and recovers at once.
    /// </summary>
    private bool DelayKnockout(PlayerDamageSource source)
    {
        if (source != PlayerDamageSource.Fall || Player == null || !IsInstanceValid(Player) || Player.KnockoutRecoveryDelayS <= 0 || !Player.IsOnFloor()) return false;
        _pendingRecoveryS = Player.KnockoutRecoveryDelayS;
        Player.HoldControl(_pendingRecoveryS);
        return true;
    }

    private void ObservePlayerFall(float landingSpeedMps, bool deepWater) =>
        ApplyPlayerDamage((Player?.Knacks ?? BodyKnacks.None).FallDamage(landingSpeedMps, deepWater), PlayerDamageSource.Fall);

    /// <summary>
    /// Injury recovery at the spawn (the camp, today): half the hearts and the bar, the pool at least half full, a moment when
    /// nothing hurts and a longer one when fire cannot, and the recovery point is never inside a burning area (the ground
    /// around it is put out and stays out for the grace).
    /// </summary>
    private bool RecoverPlayerFromInjury()
    {
        if (Player == null || !IsInstanceValid(Player) || !Player.RecoverAtSpawn()) return false;
        _playerModes = _playerModes.WithCurrent(_vitalsRules.Recovered(Vitals));
        _recoveryGraceS = 1;
        _fireGraceS = NatureLaws.RecoveryFireGraceS;
        _recoveredAt = Player.GlobalPosition;
        _fireQuarters = 0;
        _healS = 0;
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
        var earned = _vitalsRules.CompleteClimb(Vitals, challenge);
        if (ReferenceEquals(earned, Vitals)) return;
        var next = _playerModes.WithCurrent(earned);
        if (TrySaveVitals(next) is not { } saved) return;
        _playerModes = saved;
        _vitalsDirty = false;
    }

    private void ResetVitalsCounters()
    {
        _healS = 0;
        _fireQuarters = 0;
        _pipProgress = 0;
        _slowed = _vitalsRules.InBands && Vitals.Stamina <= 0;
        _exertion = BodyExertion.None;
    }

    /// <summary>
    /// While the player is away the world keeps living (MECHANICS section 4): the time since this mode was last written heals
    /// hearts at the resting pace (after the unharmed wait), refills the bar, and wakes the pool at least half full, as at
    /// camp. Nothing in Creative needs it but the shadow pool. A mode never written by this host (fresh, or only in a version
    /// 1 save) has no time away.
    /// </summary>
    private PlayerVitals CaughtUp(VitalsRules rules, PlayerVitals vitals, DateTime? written)
    {
        if (written is not { } at) return vitals;
        var away = (Clock() - at).TotalSeconds;
        if (!double.IsFinite(away) || away <= 0) return vitals;
        var pips = rules.HasPool && vitals.Pips < rules.CampPips ? rules.CampPips : vitals.Pips;
        if (!rules.Consequences) return pips == vitals.Pips ? vitals : vitals.With(pips: pips);
        var resting = Math.Max(0, away - rules.HealAfterUnharmedS);
        var health = vitals.Health;
        if (resting > 0 && health > 0)
            health = rules.InQuarters
                ? (float)(Math.Min(rules.MaxQuarters(vitals), rules.Quarters(vitals) + Math.Floor(resting / rules.HealQuarterEveryS)) * rules.HealthPerQuarter)
                : (float)Math.Min(vitals.MaxHealth, health + 2 * resting);
        var stamina = (float)Math.Min(vitals.MaxStamina, vitals.Stamina + rules.RefillMPerS * away);
        if (health == vitals.Health && stamina == vitals.Stamina && pips == vitals.Pips) return vitals;
        return vitals.With(health: health, stamina: stamina, pips: pips);
    }

    private void CatchUpTimeAway()
    {
        var caught = CaughtUp(_vitalsRules, Vitals, _playerModes.Written(Mode));
        if (ReferenceEquals(caught, Vitals)) return;
        _playerModes = _playerModes.WithCurrent(caught);
        _vitalsDirty = true;
    }
}

/// <summary>
/// The kernel's side of the stamina ledger (game/scripts/native/Seams/StaminaLedger.cs), bound to the body by
/// BindPlayerVitals: the body reports its sprint, deep-water and dive seconds once a physics tick, before VitalsTick, which
/// charges them from the pack (traits.stamina) and the team's knacks; then the body obeys the gate. No allocation per tick.
/// </summary>
public sealed class KernelStaminaLedger(CommandHost host) : IStaminaLedger
{
    public void Report(BodyExertion exertion) => host.ReportExertion(exertion);
    public StaminaGate Gate => host.BodyGate;
}

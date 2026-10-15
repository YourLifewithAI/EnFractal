using System;
using System.Collections.Generic;
using EnFractal.Native.Seams;

namespace EnFractal.Native.Kernel;

/// <summary>
/// The island's rules for the player's body and the pool, resolved for one mode, difficulty, set of knacks and count of
/// expertise (docs/game/MECHANICS.md sections 4, 5 and 8; the rules pack's traits, cost, modes and knacks blocks). Every
/// part the pack leaves out keeps today's behaviour: without traits.hearts health is 100 points and harm is in points;
/// without traits.stamina the bar is 0.45 m growing 0.10 m per completed climb to 3.0 m, refilled 0.10 m a second, and
/// nothing but climbing drains it; without traits.pips there is no pool and nothing is charged. Immutable and allocation
/// free to read: the host resolves it again when the rules, the mode, the difficulty, the knacks or the expertise change.
/// </summary>
public sealed class VitalsRules
{
    /// <summary>Today's rules, as a pack with no mechanics blocks has them.</summary>
    public static VitalsRules Legacy { get; } = new(null, "challenge", "standard", null, 0);

    public string Mode { get; }
    public string Difficulty { get; }
    public HeartsRules? Hearts { get; }
    public HarmRules? Harm { get; }
    public StaminaRules? Stamina { get; }
    public PipsRules? Pips { get; }
    public CostRules? Cost { get; }
    public FractalsRules? FractalRules { get; }
    /// <summary>The modes block's row this mode plays by (Live plays as Challenge Standard until Live exists), or null.</summary>
    public ModeRow? Row { get; }

    /// <summary>Hearts in quarters (traits.hearts present): every harm and heal a whole number of quarters.</summary>
    public bool InQuarters => Hearts != null;
    /// <summary>The bar in bands (traits.stamina present): running, deep water and diving drain it; the per-climb reward is retired.</summary>
    public bool InBands => Stamina != null;
    /// <summary>A pool of connection energy (traits.pips present).</summary>
    public bool HasPool => Pips != null;
    /// <summary>Health in one quarter: the pack's, times Big heart's rank (5, 5.5, 6). Today's 5 when the pack has no hearts.</summary>
    public double HealthPerQuarter { get; }
    /// <summary>Every hurt is multiplied by this (Gentle 0.5, Standard 1, Brave 1.5; 0 in Creative, where nothing hurts).</summary>
    public double DamageMultiplier { get; }
    /// <summary>The pool's refill pace is multiplied by this (Gentle 1.5, Standard 1, Brave 0.75).</summary>
    public double RefillMultiplier { get; }
    public bool Consequences { get; }
    /// <summary>Creative's pool: it drains and refills but never refuses.</summary>
    public bool ShadowPool { get; }
    /// <summary>The most quarters one fall may take: the mode's row over traits.harm's; null, no cap (the design names no number).</summary>
    public int? FallCapQuarters { get; }
    /// <summary>Big bar: stamina per bar. Steady climber: distance climbed for the same stamina. Strong swimmer: deep water's cost down. Long breath: stamina under water.</summary>
    public double StaminaPerBarFactor { get; }
    public double ClimbFactor { get; }
    public double SwimFactor { get; }
    public double BreathFactor { get; }
    /// <summary>The pool's maximum: the start plus one pip per expertise_per_pip expertise, never past the ceiling.</summary>
    public int MaxPips { get; }
    /// <summary>At camp, and after time away, the pool wakes at least this full.</summary>
    public int CampPips { get; }
    /// <summary>Healing lands one quarter every this many seconds (today: 2 health a second) ...</summary>
    public double HealQuarterEveryS => Hearts?.HealQuarterEveryS ?? 2.5;
    /// <summary>... once this many seconds have passed without harm, on safe ground.</summary>
    public double HealAfterUnharmedS => Hearts?.HealAfterUnharmedS ?? 10;
    public double RefillMPerS => Stamina?.RefillMPerS ?? PlayerVitals.GroundRecoveryPerSecond;

    private VitalsRules(IslandRules? rules, string mode, string difficulty, IReadOnlyDictionary<string, int>? knacks, int expertise)
    {
        Mode = mode;
        Difficulty = difficulty;
        var traits = rules?.Traits;
        Hearts = traits?.Hearts;
        Harm = traits?.Harm;
        Stamina = traits?.Stamina;
        Pips = traits?.Pips;
        Cost = rules?.Cost;
        FractalRules = rules?.Fractals;
        // Live is present but refused until it exists: meanwhile it plays as Challenge Standard ("Live (solo preview)").
        var modes = rules?.Modes;
        Row = modes == null ? null : mode == "creative" ? modes.Row("creative", null)
            : modes.Row("challenge", mode == "challenge" ? difficulty : "standard") ?? modes.Row("challenge", "standard") ?? FirstChallenge(modes);
        Consequences = Row?.Consequences ?? mode != "creative";
        ShadowPool = Row?.ShadowPool ?? mode == "creative";
        DamageMultiplier = Row?.DamageMultiplier ?? (mode == "creative" ? 0 : 1);
        RefillMultiplier = Row?.RefillMultiplier ?? 1;
        FallCapQuarters = Row?.FallCapQuarters ?? Harm?.FallCapQuarters;
        double Factor(string moves)
        {
            var factor = 1.0;
            if (knacks == null || rules?.Knacks == null) return factor;
            foreach (var (id, rank) in knacks)
                if (rules.Knacks.Knack(id) is { Available: true, Unit: "factor" } knack && knack.Moves == moves && rank >= 1)
                    factor *= knack.Ranks[Math.Min(rank, knack.Ranks.Count) - 1];
            return factor;
        }
        HealthPerQuarter = Hearts == null ? PlayerVitals.MaximumHealth / 20 : Hearts.HealthPerQuarter * Factor("health_per_quarter");
        StaminaPerBarFactor = Factor("stamina_per_bar");
        ClimbFactor = Factor("climb_per_stamina");
        SwimFactor = Factor("swim");
        BreathFactor = Factor("breath");
        MaxPips = Pips == null ? 0 : Math.Min(Pips.Ceiling, Pips.Start + Math.Max(0, expertise) / Pips.ExpertisePerPip);
        CampPips = Pips == null ? 0 : (int)Math.Floor(MaxPips * Pips.CampFraction + 1e-9);
    }

    private static ModeRow? FirstChallenge(ModesRules modes)
    {
        foreach (var row in modes.Rows) if (row.Mode == "challenge") return row;
        return null;
    }

    /// <summary>The rules for a pack (null: none loaded, today's), a mode, a difficulty (Challenge's; standard otherwise), knack ranks by id and expertise learned.</summary>
    public static VitalsRules For(IslandRules? rules, string mode, string difficulty = "standard", IReadOnlyDictionary<string, int>? knacks = null, int expertise = 0) =>
        new(rules, PlayerModesProfile.ValidMode(mode) ? mode : "creative", difficulty is "gentle" or "standard" or "brave" ? difficulty : "standard", knacks, expertise);

    // ---- the body's numbers ----

    public int MaxHearts(PlayerVitals v) => Hearts == null ? 5 : Math.Min(Hearts.CapHearts, Hearts.StartHearts + v.Fractals.Hearts);
    public int MaxQuarters(PlayerVitals v) => 4 * MaxHearts(v);
    public float MaxHealthOf(PlayerVitals v) => Hearts == null ? PlayerVitals.MaximumHealth : (float)(MaxQuarters(v) * HealthPerQuarter);
    /// <summary>The quarters left: health in whole quarters (the hearts the meters draw).</summary>
    public int Quarters(PlayerVitals v) => Math.Clamp((int)Math.Round(v.Health / HealthPerQuarter, MidpointRounding.AwayFromZero), 0, MaxQuarters(v));
    /// <summary>The bands open: the starting bar and one more per band bought, never past the pack's last.</summary>
    public int OpenBands(PlayerVitals v) => Stamina == null ? 1 : Math.Min(Stamina.Bands, 1 + v.Fractals.Bands);
    public float MaxStaminaOf(PlayerVitals v) => Stamina == null ? PlayerVitals.LegacyMaxStamina(v.Challenges.Count)
        : (float)Math.Min(PlayerVitals.ContractMaxStamina, (Stamina.StartM + Stamina.BandM * (OpenBands(v) - 1)) * StaminaPerBarFactor);

    /// <summary>A new body under these rules: every heart, the starting bar full, the pool full.</summary>
    public PlayerVitals Fresh()
    {
        var blank = Normalize(PlayerVitals.Fresh);
        return blank.With(health: blank.MaxHealth, stamina: blank.MaxStamina, pips: HasPool ? MaxPips : PlayerVitals.UnsetPool);
    }

    /// <summary>
    /// A body read under other rules (an older save, another pack, a knack changed) brought to these: the maxima recomputed,
    /// health in whole quarters, stamina and the pool within their maxima, an unset pool filled. The quarters left are kept as
    /// the body had them (its health against its own maximum), so Big heart makes each quarter tougher without healing or
    /// hurting. The same record when nothing changes.
    /// </summary>
    public PlayerVitals Normalize(PlayerVitals v)
    {
        var maxHealth = MaxHealthOf(v);
        var maxStamina = MaxStaminaOf(v);
        var quarters = 4 * MaxHearts(v);
        var held = v.MaxHealth > 0 ? v.MaxHealth / quarters : HealthPerQuarter;
        var health = Hearts == null ? Math.Clamp(v.Health, 0, maxHealth)
            : (float)(Math.Clamp((int)Math.Round(v.Health / held, MidpointRounding.AwayFromZero), 0, quarters) * HealthPerQuarter);
        var stamina = Math.Clamp(v.Stamina, 0, maxStamina);
        var pips = !HasPool ? v.Pips : v.Pips < 0 ? MaxPips : Math.Clamp(v.Pips, 0, MaxPips);
        if (maxHealth == v.MaxHealth && maxStamina == v.MaxStamina && health == v.Health && stamina == v.Stamina && pips == v.Pips) return v;
        return v.With(health: health, maxHealth: maxHealth, stamina: stamina, maxStamina: maxStamina, pips: pips);
    }

    // ---- harm and heal ----

    /// <summary>
    /// The quarters a hurt of this many health points takes: multiplied by the difficulty, rounded to the nearest quarter at
    /// the moment of harm (never on the screen), a fall capped where the rules cap it. Fall damage stays in health points
    /// (decision K), so Big heart's larger quarter takes fewer of them from the same fall.
    /// </summary>
    public int HarmQuarters(float healthPoints, bool fall)
    {
        if (!float.IsFinite(healthPoints) || healthPoints <= 0) return 0;
        var quarters = (int)Math.Min(1000, Math.Round(healthPoints * DamageMultiplier / HealthPerQuarter, MidpointRounding.AwayFromZero));
        if (fall && FallCapQuarters is { } cap) quarters = Math.Min(quarters, cap);
        return Math.Max(0, quarters);
    }

    /// <summary>Quarters taken from the body (in quarters only).</summary>
    public PlayerVitals Harmed(PlayerVitals v, int quarters) =>
        quarters <= 0 ? v : v.With(health: (float)(Math.Max(0, Quarters(v) - quarters) * HealthPerQuarter));

    /// <summary>Today's harm in health points, multiplied by the difficulty.</summary>
    public PlayerVitals Damage(PlayerVitals v, float amount)
    {
        if (!float.IsFinite(amount) || amount < 0) throw new PlayerVitalsException("Damage needs a finite nonnegative amount.");
        return v.With(health: Math.Max(0, v.Health - (float)(amount * (Row == null ? 1 : DamageMultiplier))));
    }

    /// <summary>One quarter healed (in quarters only).</summary>
    public PlayerVitals HealedQuarter(PlayerVitals v) => v.With(health: (float)(Math.Min(MaxQuarters(v), Quarters(v) + 1) * HealthPerQuarter));

    /// <summary>
    /// Today's rest on safe ground: the bar refills by the seconds given, and health heals 2 points a second when heal is
    /// set (in quarters the host heals whole quarters instead, HealedQuarter).
    /// </summary>
    public PlayerVitals Rest(PlayerVitals v, float seconds, bool heal)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new PlayerVitalsException("Rest needs a finite duration.");
        var health = heal && Hearts == null ? Math.Min(v.MaxHealth, v.Health + 2f * seconds) : v.Health;
        return v.With(health: health, stamina: Math.Min(v.MaxStamina, v.Stamina + (float)(RefillMPerS * seconds)));
    }

    /// <summary>A knockout's waking at camp: half the hearts and half the bar, the pool at least half full; earned traits and creations are kept.</summary>
    public PlayerVitals Recovered(PlayerVitals v)
    {
        var health = Hearts == null ? v.MaxHealth * 0.5f : (float)(MaxQuarters(v) / 2 * HealthPerQuarter);
        var pips = HasPool && v.Pips < CampPips ? CampPips : v.Pips;
        return v.With(health: health, stamina: v.MaxStamina * 0.5f, recoveries: Math.Min(1_000_000, v.Recoveries + 1), pips: pips);
    }

    // ---- the bar ----

    /// <summary>Climbing as today: the rise or the holding cost, whichever is more, divided by Steady climber's factor.</summary>
    public PlayerVitals SpendClimb(PlayerVitals v, float seconds, float rise, out bool enough)
    {
        if (!float.IsFinite(seconds) || !float.IsFinite(rise) || seconds < 0 || rise < 0)
            throw new PlayerVitalsException("Climbing needs a finite duration and rise.");
        var cost = (float)(Math.Max(rise, PlayerVitals.HoldingCostPerSecond * seconds) / ClimbFactor);
        enough = v.Stamina + 0.000001f >= cost && v.Stamina > 0;
        return v.With(stamina: Math.Max(0, v.Stamina - cost));
    }

    /// <summary>
    /// A completed climb is recorded once by its id. Today it also raises the bar 0.10 m (to 3.0 m); in bands that reward is
    /// retired (decision L) and the climb only counts, for the journal.
    /// </summary>
    public PlayerVitals CompleteClimb(PlayerVitals v, string challenge)
    {
        if (v.Challenges.Contains(challenge) || v.Challenges.Count >= PlayerVitals.MaximumChallenges) return v;
        var climbs = new string[v.Challenges.Count + 1];
        for (var i = 0; i < v.Challenges.Count; i++) climbs[i] = v.Challenges[i];
        climbs[^1] = challenge;
        Array.Sort(climbs, StringComparer.Ordinal);
        // A reward raises capacity, not the remaining energy, so it cannot sustain a hanging climb indefinitely.
        var recorded = PlayerVitals.Of(v.Health, v.MaxHealth, v.Stamina, v.MaxStamina, climbs, v.Recoveries, v.Fractals, v.Pips);
        return Stamina == null ? recorded.With(maxStamina: PlayerVitals.LegacyMaxStamina(climbs.Length)) : recorded;
    }

    /// <summary>
    /// The bar's drain for what the body reported beyond climbing (the stamina ledger, Seams/StaminaLedger.cs), in bands only:
    /// running at running_m_per_s, deep water at deep_water_m_per_s (less by Strong swimmer's factor), under water at
    /// dive_m_per_s (times Long breath's factor); an absent water rate drains nothing. In metres, never negative.
    /// </summary>
    public float Drain(BodyExertion exertion)
    {
        if (Stamina == null) return 0;
        var drain = Clean(exertion.SprintS) * Stamina.RunningMPerS + Clean(exertion.DeepWaterS) * (Stamina.DeepWaterMPerS ?? 0) / SwimFactor +
            Clean(exertion.DiveS) * (Stamina.DiveMPerS ?? 0) * BreathFactor;
        return (float)Math.Max(0, drain);
    }

    private static double Clean(float seconds) => float.IsFinite(seconds) && seconds > 0 ? seconds : 0;

    /// <summary>
    /// The first bar, the top row the meters draw: the bands up to bands_per_bar, with Big bar; the whole bar while it has no
    /// second row.
    /// </summary>
    public float FirstBarOf(PlayerVitals v) => Stamina == null ? v.MaxStamina
        : Math.Min(v.MaxStamina, (float)((Stamina.StartM + Stamina.BandM * (Math.Min(OpenBands(v), Stamina.BandsPerBar) - 1)) * StaminaPerBarFactor));

    /// <summary>
    /// The bar's level below which, once it ran empty, the body cannot run: the pack's fraction of the first bar (the founder,
    /// 15 October: no running until the first bar is completely refilled).
    /// </summary>
    public float SlowUntil(PlayerVitals v) => Stamina == null ? 0 : (float)(FirstBarOf(v) * Stamina.EmptyNoRunUntilFraction);

    /// <summary>The gate the body obeys: empty at zero in bands with consequences; no running from empty until the first bar refills.</summary>
    public StaminaGate Gate(PlayerVitals v, bool slowed) =>
        Stamina == null || !Consequences ? StaminaGate.Free : new StaminaGate(v.Stamina <= 0.000001f, slowed);

    // ---- the pool ----

    /// <summary>
    /// What casting this costs, in whole pips, by principal alone (contracts/README.md "Price by principal"): the player's
    /// order pays the ordered price, the Gubble's own idea the own-idea price, except while a wish runs (every Gubble command
    /// then pays as ordered, whatever its action id says). 0 without a pool, without a cost block, or for an unpriced ability.
    /// A dictionary lookup: no allocation.
    /// </summary>
    public int Price(string capability, bool companion, bool wishRunning)
    {
        if (Pips == null || Cost == null || !Cost.Prices.TryGetValue(capability, out var price)) return 0;
        return companion && !wishRunning ? price.OwnIdea : price.Ordered;
    }

    /// <summary>Seconds per pip refilled at this distance between the Gubble and the player, the difficulty's pace applied.</summary>
    public double RefillEveryS(float distanceM) => Pips == null ? double.PositiveInfinity
        : (distanceM <= Pips.RefillNearWithinM ? Pips.RefillNearEveryS : Pips.RefillFarEveryS) / RefillMultiplier;
}

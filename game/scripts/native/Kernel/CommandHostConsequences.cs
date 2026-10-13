using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace EnFractal.Native.Kernel;

/// <summary>
/// Consequences, and what the Gubble's AI learns from them (docs/magic/README.md "Fire"). The founder, 13 October: "Let
/// things like fire or other actions that might hurt a player, hurt the player. This becomes feedback for the AI and there
/// should be a learning mechanism of sorts that helps the AI make better choices later on." So the Gubble's fire is never
/// held for the player's sake; it is kept in bounds by guardrails, and what it does comes back to the AI:
/// - guardrails: the player's own setting "Let the Gubble use fire" (the profile's gubble_fire, on unless the player turns
///   it off; only the player's control changes it, no command reaches it), one fire of its own at a time, and
///   NatureLaws.CompanionFireCooldownS after its last one went out. The player is never slowed by them: the player's own
///   Fire puts the Gubble's out. The player's Stop (X) puts the Gubble's fire out at once, whatever any budget says.
/// - feedback: when the Gubble's fire hurts the player, a fixed-format notice (what happened, which fire, health before and
///   after, whether the player had to recover; no prose from anyone) rides on the Gubble's next result, of any kind, once.
///   Until then any query returns it (player.status is the cheapest).
/// - learning across sessions: the host writes one consequence fact per fire into the team's journal, in the game's own
///   words ("The Gubble's fire burned you near ..."), naming only places the team knows. journal.read with kind
///   "consequence" reads the lessons back; the journal is the AI's memory.
/// </summary>
public partial class CommandHost
{
    /// <summary>The player's profile key (RoomHud.ProfilePath, [profile] section) for "Let the Gubble use fire".</summary>
    public const string FireProfileKey = "gubble_fire";
    /// <summary>The setting's value when the player never chose: on (the founder's decision is consequences, not prohibition).</summary>
    public const bool DefaultGubbleFire = true;
    public const string FireOffMessage = "The player has turned off \"Let the Gubble use fire\".";
    /// <summary>Unread notices kept for the Gubble, the oldest dropped first.</summary>
    public const int MaxNotices = 8;
    /// <summary>A consequence fact names the nearest thing the team knows within this distance of where it happened.</summary>
    public const float ConsequencePlaceM = 1.5f;

    private bool? _gubbleFire;
    /// <summary>The weather clock when the Gubble's own fire was last seen burning (its cooldown counts from there).</summary>
    private double _companionFireSeenS = double.NegativeInfinity;
    private readonly List<JsonObject> _notices = new();
    /// <summary>The unread notice still gathering one fire's harm (updated in place until the Gubble reads it).</summary>
    private JsonObject? _openNotice;
    /// <summary>The fire (by the room revision it was lit at) whose consequence fact is written, and that fact.</summary>
    private int _factFire = -1;
    private JsonObject? _factEntry;
    private bool _factRecovered;

    /// <summary>
    /// The player's setting "Let the Gubble use fire": read once from the player's profile (never room state), then whatever
    /// the player's own control sets this session. A profile that exists but does not load counts as off.
    /// </summary>
    public bool GubbleFireAllowed => _gubbleFire ??= LoadGubbleFire(RoomHud.ProfilePath);

    /// <summary>The player's control (the HUD's setting) and tests. No command reaches this.</summary>
    public void SetGubbleFire(bool allowed) => _gubbleFire = allowed;

    public static bool LoadGubbleFire(string profilePath)
    {
        var config = new ConfigFile();
        var loaded = config.Load(profilePath);
        if (loaded == Error.FileNotFound) return DefaultGubbleFire;
        if (loaded != Error.Ok) return false;
        var value = config.GetValue("profile", FireProfileKey, DefaultGubbleFire);
        return value.VariantType == Variant.Type.Bool && value.AsBool();
    }

    /// <summary>The notices the Gubble has not read yet (tests and the HUD).</summary>
    public IReadOnlyList<JsonObject> UnreadNotices => _notices;

    // ---- the guardrails ----

    /// <summary>The Gubble's Fire, preview or cast: the player's setting, one fire of its own at a time, and the wait after its last.</summary>
    private void CheckCompanionFire()
    {
        if (!GubbleFireAllowed) throw new Refusal("permission_denied", FireOffMessage, "$.args.capability");
        if (Nature.Fire?.Principal == CompanionPrincipal)
            throw new Refusal("target_busy", "Your fire is still burning: one fire at a time. Wait for it to go out.", "$.args.capability", retryable: true);
        var wait = NatureLaws.CompanionFireCooldownS - (Nature.WeatherS - _companionFireSeenS);
        if (wait > 0)
            throw new Refusal("rate_limited", $"Wait about {Math.Ceiling(wait):0} seconds after your last fire before lighting another.", "$.args.capability",
                retryable: true, actual: JsonValue.Create(Math.Round(wait, 1)));
    }

    /// <summary>Notes that the Gubble's fire burns now (its cooldown counts from the last time it was seen burning).</summary>
    private void SeeCompanionFire()
    {
        if (Nature.Fire?.Principal == CompanionPrincipal) _companionFireSeenS = Nature.WeatherS;
    }

    /// <summary>
    /// The player's Stop of the Gubble (and the Gubble's own stop): its fire goes out at once, every cell of it. Like a fire
    /// burning out, this is the simulation's change, not a room revision, so no ledger, budget or rate limit can refuse it;
    /// it is saved at once when the save is there. Returns whether a fire went out.
    /// </summary>
    internal bool PutOutCompanionFire()
    {
        if (!NatureConfigured || Nature.Fire is not { Principal: CompanionPrincipal }) return false;
        _companionFireSeenS = Nature.WeatherS;
        Nature.PutOutFire();
        _natureDirty = true;
        NatureChanged?.Invoke();
        SaveNature();
        return true;
    }

    // ---- the feedback ----

    /// <summary>A fire hurt the player: when it is the Gubble's, the Gubble's notice and the journal's consequence fact.</summary>
    private void NoteFireHarm(NatureFire fire, PlayerHarm harm, Vector3 at)
    {
        if (fire.Principal != CompanionPrincipal) return;
        if (_openNotice != null && _notices.Contains(_openNotice) && _openNotice["fire_revision"]!.GetValue<int>() == fire.Revision)
        {
            _openNotice["health_after"] = Health(harm.After);
            _openNotice["recovered"] = harm.Recovered || _openNotice["recovered"]!.GetValue<bool>();
            _openNotice["at_utc"] = Now();
        }
        else
        {
            _openNotice = new JsonObject
            {
                ["kind"] = "fire_hurt_player", ["fire_revision"] = fire.Revision, ["health_before"] = Health(harm.Before), ["health_after"] = Health(harm.After),
                ["recovered"] = harm.Recovered, ["at_utc"] = Now(),
            };
            _notices.Add(_openNotice);
            while (_notices.Count > MaxNotices) _notices.RemoveAt(0);
        }
        WriteConsequence(fire, harm, at);
    }

    private static double Health(float value) => Math.Round(Math.Clamp(value, 0, PlayerVitals.MaximumHealth), 1);

    /// <summary>
    /// The Gubble's next result, of any kind, carries every notice it has not read, once; a result that would grow past the
    /// result bound keeps them for the next one. Further harm by the same fire after that starts a new notice.
    /// </summary>
    private void DeliverNotices(JsonObject result)
    {
        if (_notices.Count == 0) return;
        result["notices"] = new JsonArray(_notices.Select(n => (JsonNode?)n.DeepClone()).ToArray());
        if (CanonicalJson.Bytes(result).Length > MaxResultBytes)
        {
            result.Remove("notices");
            return;
        }
        _notices.Clear();
        _openNotice = null;
    }

    /// <summary>
    /// One consequence fact per fire that hurt the player, in the game's own words and saved with the journal at once:
    /// "The Gubble's fire burned you near "the jetty"." The place is the nearest thing the team knows (never one it has not
    /// seen), quoted as world text; the fact is updated in place if the same fire later puts the player down.
    /// </summary>
    private void WriteConsequence(NatureFire fire, PlayerHarm harm, Vector3 at)
    {
        if (_factFire == fire.Revision && _factEntry != null)
        {
            if (!harm.Recovered || _factRecovered) return;
            _factRecovered = true;
            _factEntry["line"] = KernelJson.DisplayText(ConsequenceLine(NearestKnownPlace(at), recovered: true), 200);
            SaveTeam();
            return;
        }
        _factFire = fire.Revision;
        _factRecovered = harm.Recovered;
        _factEntry = Fact("consequence", ConsequenceLine(NearestKnownPlace(at), harm.Recovered), CompanionPrincipal, fire.DirectedBy, null, null, at);
        AddHistory(_factEntry);
        SaveTeam();
    }

    private static string ConsequenceLine(string? place, bool recovered) =>
        "The Gubble's fire burned you" + (place == null ? "" : " near " + Quoted(place)) + (recovered ? " until you had to recover." : ".");

    /// <summary>The name of the thing the team knows nearest a place, within ConsequencePlaceM; null when it knows none there.</summary>
    private string? NearestKnownPlace(Vector3 at)
    {
        string? name = null;
        var best = ConsequencePlaceM;
        foreach (var (id, seen) in TeamMemory().Entries)
        {
            if (!KnownIdPattern.IsMatch(id)) continue;
            var distance = Distance(seen.Summary, at);
            if (distance > best) continue;
            best = distance;
            name = seen.Summary["display_name"]?.GetValue<string>();
        }
        return name;
    }
}

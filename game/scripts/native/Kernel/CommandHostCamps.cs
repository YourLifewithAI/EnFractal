using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using EnFractal.Native.Room;

namespace EnFractal.Native.Kernel;

/// <summary>A campfire's spot (bottom centre, on the ground): the island's starting campfire, or a firepit the kernel chose.</summary>
public sealed record CampSite(string Id, Vector3 PositionM, bool Starting);

/// <summary>What sitting down by a fire answered: whether the player sits, the words, the campfire, and what it taught or changed.</summary>
public sealed record RestOutcome(bool Ok, string Words, string? Campfire = null, bool LearnedFire = false, bool NewCamp = false);

/// <summary>
/// Fire's camp in numbers (docs/game/MECHANICS.md decisions 49, 61, 62, 70 and 71). Every number here is provisional: the design
/// names none of them.
/// </summary>
public static class CampLaws
{
    public const string StartingId = "camp:start";
    public const string FirepitPrefix = "firepit:";
    /// <summary>The starting campfire burns this far in front of the player's spawn, so the first load wakes the player facing it.</summary>
    public const float StartingFireAheadM = 0.2f;
    /// <summary>The sit action works within this of a lit campfire (three body heights), and at most RestRiseM above or below it.</summary>
    public const float RestRadiusM = 0.3f;
    public const float RestRiseM = 0.15f;
    /// <summary>How many firepits an island gets from its benches, and how close two may stand (the rule: farthest-point sampling).</summary>
    public const int FirepitCount = 6;
    public const float FirepitMinSpacingM = 4f;
    /// <summary>An island without the generator's bench Fractals: standable dry ground probed on a grid this coarse.</summary>
    public const float FirepitGridStepM = 3f;
    /// <summary>A Fire cast lights a firepit when its circle reaches within this of the firepit (and within FirepitCatchRiseM of its height).</summary>
    public const float FirepitCatchM = 0.12f;
    public const float FirepitCatchRiseM = 0.3f;
    /// <summary>Hearts refill this many times faster within CommandHost.CampRadiusM of the starting camp or any lit campfire.</summary>
    public const float CampHeartsFactor = 2f;
    /// <summary>The start spot is saved again once the player's last safe footing moved this far.</summary>
    public const float SpotMovedM = 0.25f;
    /// <summary>The sit ends once the body moved this far from where it sat down.</summary>
    public const float SitHoldM = 0.03f;
    public const int MaxFirepits = 16;

    /// <summary>
    /// The firepits from the candidates, spread evenly: farthest-point sampling from the starting campfire. Each pick is the candidate
    /// farthest (on the ground plane) from the starting campfire and every pick so far, the first listed on a tie; it stops at
    /// <paramref name="count"/> or when the farthest left is nearer than <paramref name="spacing"/>. The same candidates always give the
    /// same firepits in the same order, so their ids (firepit:1, firepit:2, ...) are stable for an island.
    /// </summary>
    public static List<Vector3> ChooseFirepits(Vector3 start, IReadOnlyList<Vector3> candidates, int count, float spacing)
    {
        var chosen = new List<Vector3>();
        var left = candidates.ToList();
        while (chosen.Count < Math.Min(count, MaxFirepits) && left.Count > 0)
        {
            var best = -1f;
            var at = -1;
            for (var i = 0; i < left.Count; i++)
            {
                var nearest = Flat(left[i], start);
                foreach (var pick in chosen) nearest = Math.Min(nearest, Flat(left[i], pick));
                if (nearest > best) { best = nearest; at = i; }
            }
            if (best < spacing) break;
            chosen.Add(left[at]);
            left.RemoveAt(at);
        }
        return chosen;
    }

    public static float Flat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();
}

/// <summary>
/// Fire's camp (the Fire's camp packet, 16 October; MECHANICS.md decisions 49, 61, 62, 63, 70 and 71):
/// <list type="bullet">
/// <item><b>The starting campfire</b> (61): every island has one, CampLaws.StartingFireAheadM in front of the player's spawn, lit from
/// the start for now (the opening scene will put it out and have the Gubble relight it). The camp is where it burns.</item>
/// <item><b>Firepits</b> (62): chosen by the kernel from the island's benches, without a generator change: the manifest carries no
/// benches (the generator keeps them in its package), so the candidates are the generator's bench-placed Fractals (those with a
/// reach_band), and on an island without them, standable dry ground on a grid; FirepitCount of them spread evenly by farthest-point
/// sampling. Unlit at first; a Fire cast on one lights it (LightFirepit, from CastFire) and it is a campfire from then on, saved with
/// the island; the first one lit teaches Campfire. Rain and the X stop do not put a campfire out (a contained fire, not a spreading one).</item>
/// <item><b>A lit campfire is a full camp</b> (71): PlayerAtCamp is true within CampRadiusM of the starting camp or of any lit
/// campfire, so Fractals are spent and knacks chosen there; hearts refill CampHeartsFactor times faster there; and the campfire the
/// player last sat at is the camp they wake at (a knockout's recovery lands there, with the pool at least half full).</item>
/// <item><b>Resting is sitting down by a fire</b> (70): PlayerRest, the HUD's rest key, within RestRadiusM of a lit campfire, on the
/// ground. The first rest at the starting campfire teaches Fire (61), once per team. The host verifies it from the body itself.</item>
/// <item><b>The start spot</b> (49): the island save's team block keeps where the player and the Gubble last stood, one spot per mode
/// (the block "camp"); a spot that is not safe ground (water, mid-air, inside something) falls back to the starting campfire, and the
/// first load of an island starts there. The island's clock record (x_clock) is kept in the same block's neighbour, "clock".</item>
/// </list>
/// </summary>
public partial class CommandHost
{
    public const string RestNearFire = "Sit down by a lit campfire to rest: come closer to the fire.";
    public const string RestOnGround = "Stand on the ground by the fire to sit down.";
    public const string RestUnlit = "This firepit is not lit yet: ask the Gubble to cast Fire on it.";

    private static readonly StringName CampHitPosition = "position";
    private static readonly StringName CampHitNormal = "normal";
    private static readonly StringName CampHitCollider = "collider";
    private static readonly System.Text.RegularExpressions.Regex CampIdPattern = new(@"\A(camp:start|firepit:[1-9][0-9]?)\z", System.Text.RegularExpressions.RegexOptions.Compiled);

    private List<CampSite>? _camps;
    private readonly HashSet<string> _litCampfires = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonObject> _startSpots = new(StringComparer.Ordinal);
    private string? _homeCamp;
    private Vector3? _homeSpot;
    private bool _spotsLive;
    private Vector3 _spotNoted;
    private bool _resting;
    private Vector3 _restAt;
    private Vector3? _campSpawn;
    private IReadOnlyList<Vector3>? _firepitsForTests;

    /// <summary>The look and the HUD hear when a campfire is lit or the saved camp state is loaded.</summary>
    public event Action? CampsChanged;

    /// <summary>Test seam: the firepits' spots in place of the bench rule's (the test room is no island and has none).</summary>
    internal IReadOnlyList<Vector3>? FirepitsForTests
    {
        get => _firepitsForTests;
        set { _firepitsForTests = value; _camps = null; }
    }

    /// <summary>Every campfire spot: the starting campfire first, then the firepits (computed once the room has physics).</summary>
    public IReadOnlyList<CampSite> CampSites => EnsureCamps();

    public CampSite? StartingCampfire => EnsureCamps().Count > 0 ? EnsureCamps()[0] : null;

    /// <summary>The starting campfire is lit; a firepit once a Fire cast lit it.</summary>
    public bool IsCampfireLit(string id) => id == CampLaws.StartingId || _litCampfires.Contains(id);

    /// <summary>The campfire the player last sat at, the camp they wake at (null: the island's spawn).</summary>
    public string? HomeCamp => _homeCamp;

    /// <summary>Whether the player sits by a fire now: from their rest until the body moves (or PlayerStand).</summary>
    public bool PlayerResting => _resting && Player != null && IsInstanceValid(Player) && Player.GlobalPosition.DistanceTo(_restAt) <= CampLaws.SitHoldM;

    private List<CampSite> EnsureCamps()
    {
        if (_camps != null) return _camps;
        if (Room == null) return new List<CampSite>();
        var start = StartingCampfireSpot();
        var camps = new List<CampSite> { new(CampLaws.StartingId, start, true) };
        // Before the room has physics only the starting campfire is known (not kept: the firepits are probed once it can be).
        if (!IsInsideTree()) return camps;
        var pits = _firepitsForTests ?? CampLaws.ChooseFirepits(start, FirepitCandidates(), CampLaws.FirepitCount, CampLaws.FirepitMinSpacingM);
        for (var i = 0; i < pits.Count && i < CampLaws.MaxFirepits; i++) camps.Add(new CampSite(CampLaws.FirepitPrefix + (i + 1), pits[i], false));
        _camps = camps;
        return camps;
    }

    /// <summary>In front of the player's spawn by CampLaws.StartingFireAheadM, on the ground there when the room has it.</summary>
    private Vector3 StartingCampfireSpot()
    {
        var spawn = Room.SpawnFor("player");
        var at = spawn.PositionM + new Basis(Vector3.Up, Mathf.DegToRad(spawn.YawDeg)) * Vector3.Forward * CampLaws.StartingFireAheadM;
        if (GroundBelow(at + Vector3.Up * 0.2f, 0.5f) is { } ground) at.Y = ground;
        return at;
    }

    /// <summary>
    /// The benches a firepit may stand on: the generator's bench-placed Fractals (with a reach_band, on dry ground), then, on an island
    /// (terrain or a sea) with fewer than FirepitCount of them, standable dry ground on a FirepitGridStepM grid. An indoor room has none.
    /// </summary>
    private List<Vector3> FirepitCandidates()
    {
        var found = new List<Vector3>();
        foreach (var fractal in AllFractals)
            if (fractal.ReachBand != null && !WaterColumn(fractal.PositionM, 2f, 0.05f).Wet) found.Add(fractal.PositionM);
        if (found.Count >= CampLaws.FirepitCount || (Room.Terrain == null && Room.Sea == null)) return found;
        var bounds = Room.Bounds;
        var step = CampLaws.FirepitGridStepM;
        for (var x = bounds.Position.X + step * 0.5f; x < bounds.End.X; x += step)
            for (var z = bounds.Position.Z + step * 0.5f; z < bounds.End.Z; z += step)
                if (StandableGround(new Vector3(x, bounds.End.Y + 1, z), bounds.Size.Y + 2) is { } spot) found.Add(spot);
        return found;
    }

    /// <summary>The first thing under a point if it is walkable ground (not a room object or a creation, not a steep face) with no water over it.</summary>
    private Vector3? StandableGround(Vector3 top, float depth)
    {
        using var query = PhysicsRayQueryParameters3D.Create(top, top + Vector3.Down * depth, RoomBuilder.BodyMask, BubbleExclusions());
        using var hit = GetViewport().World3D.DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0 || hit[CampHitNormal].AsVector3().Y < Mathf.Cos(Mathf.DegToRad(35))) return null;
        var at = hit[CampHitPosition].AsVector3();
        var (_, id) = NatureSurface(hit[CampHitCollider].AsGodotObject() as Node);
        if (id.StartsWith("obj:", StringComparison.Ordinal) || id.StartsWith("creation:", StringComparison.Ordinal)) return null;
        return WaterColumn(at, 2f, 0.05f).Wet ? null : at;
    }

    /// <summary>Whether a spot is at the camp: within CampRadiusM of the island's spawn or of a lit campfire. Allocates nothing once the camps are known.</summary>
    internal bool AtCamp(Vector3 at)
    {
        if (Room == null) return false;
        _campSpawn ??= Room.SpawnFor("player").PositionM;
        if (at.DistanceTo(_campSpawn.Value) <= CampRadiusM) return true;
        var camps = EnsureCamps();
        for (var i = 0; i < camps.Count; i++)
            if (IsCampfireLit(camps[i].Id) && at.DistanceTo(camps[i].PositionM) <= CampRadiusM) return true;
        return false;
    }

    /// <summary>How much faster hearts refill now (decision 62): CampHeartsFactor at the camp, else 1.</summary>
    internal float CampHeartsFactorNow() => PlayerAtCamp() ? CampLaws.CampHeartsFactor : 1f;

    private CampSite? NearestCampfire(Vector3 at, float within, bool litOnly)
    {
        CampSite? best = null;
        var bestDistance = float.MaxValue;
        foreach (var camp in EnsureCamps())
        {
            if (litOnly && !IsCampfireLit(camp.Id)) continue;
            var flat = CampLaws.Flat(at, camp.PositionM);
            if (flat > within || Math.Abs(at.Y - camp.PositionM.Y) > CampLaws.RestRiseM || flat >= bestDistance) continue;
            best = camp;
            bestDistance = flat;
        }
        return best;
    }

    // ---- resting: sitting down by a fire (decision 70) ----

    /// <summary>
    /// The player's rest key: sit down by a lit campfire. The host checks the body itself (on the ground, not swimming, within
    /// RestRadiusM of a lit campfire). The first rest at the starting campfire teaches Fire, once per team (RecordExpertise); the
    /// campfire becomes the camp the player wakes at. Not a command in the contract: a trusted HUD call, never on the companion's surface.
    /// </summary>
    public RestOutcome PlayerRest()
    {
        if (Player == null || !IsInstanceValid(Player) || Room == null) return new(false, "There is no one here to sit down.");
        if (!Player.IsOnFloor() || Player.IsSwimming) return new(false, RestOnGround);
        var at = Player.GlobalPosition;
        if (NearestCampfire(at, CampLaws.RestRadiusM, litOnly: true) is not { } campfire)
            return new(false, NearestCampfire(at, CampLaws.RestRadiusM, litOnly: false) != null ? RestUnlit : RestNearFire);
        _resting = true;
        _restAt = at;
        var learned = campfire.Starting && RecordExpertise(ExpertiseLists.Fire, ExpertiseLists.FirstFire);
        var newCamp = _homeCamp != campfire.Id;
        _homeCamp = campfire.Id;
        _homeSpot = at;
        Player.SetSpawnPoint(at);
        if (newCamp) NoteCampChanged();
        var words = campfire.Starting ? "You sit down by the campfire and rest." : "You sit down by the campfire and rest. This is your camp now: you will wake here.";
        return new(true, words, campfire.Id, learned, newCamp);
    }

    /// <summary>The player stands up (any key that moves them does it too, by moving the body).</summary>
    public void PlayerStand() => _resting = false;

    // ---- lighting a firepit (decision 62): the Gubble's Fire cast on one ----

    /// <summary>The unlit firepit a Fire cast's circle reaches, the nearest to its centre; null when none (the cast is an ordinary fire).</summary>
    private CampSite? UnlitFirepitUnder(Vector3 center, float radius)
    {
        CampSite? best = null;
        var bestDistance = float.MaxValue;
        foreach (var camp in EnsureCamps())
        {
            if (camp.Starting || IsCampfireLit(camp.Id)) continue;
            var flat = CampLaws.Flat(center, camp.PositionM);
            if (flat > radius + CampLaws.FirepitCatchM || Math.Abs(center.Y - camp.PositionM.Y) > CampLaws.FirepitCatchRiseM || flat >= bestDistance) continue;
            best = camp;
            bestDistance = flat;
        }
        return best;
    }

    /// <summary>
    /// A Fire cast on an unlit firepit lights the firepit and nothing else (the ring holds it: no ground catches, nothing burns), on
    /// the same path as every Fire: a preview first (one cell, the firepit, so the HUD's confirm works as for any fire), then the
    /// commit as exactly that preview, a keyed_yes pack's hold for the Gubble's, the price by principal charged at commit, a durable
    /// nature receipt. The firepit is a campfire from then on, saved with the island; the first one lit teaches Campfire.
    /// </summary>
    private JsonObject LightFirepit(JsonElement root, IslandAbility ability, Vector3 center, float radius, float duration, CampSite firepit, string principal,
        string action, string fingerprint, bool preview, string? approvedBy)
    {
        var keyed = principal != PlayerPrincipal && ability.Tier == "keyed_yes";
        var signature = CanonicalJson.Sha256Hex(CanonicalJson.Bytes(new JsonObject
        {
            ["capability"] = "fire", ["firepit"] = firepit.Id, ["center_m"] = KernelJson.Vector(center), ["radius_m"] = KernelJson.Num(radius), ["mode"] = Mode,
        }));
        var data = new JsonObject
        {
            ["center_m"] = KernelJson.Vector(center), ["radius_m"] = KernelJson.Num(radius), ["duration_s"] = KernelJson.Num(duration),
            ["cell_count"] = 1, ["requires_approval"] = keyed,
        };
        if (preview)
        {
            data["cells"] = new JsonArray(KernelJson.Vector(firepit.PositionM));
            data["cells_truncated"] = false;
            data["max_burning_cells"] = NatureState.MaxBurning;
            data["avatars_in_area"] = new JsonArray();
            foreach (var stale in _naturePreviews.Where(p => p.Value.Expires <= Clock()).Select(p => p.Key).ToList()) _naturePreviews.Remove(stale);
            while (_naturePreviews.Count >= 32) _naturePreviews.Remove(_naturePreviews.Keys.First());
            var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            _naturePreviews[token] = new NaturePreview(principal, signature, Revision, Clock() + ApprovalLifetime);
            data["preview_id"] = token;
            return NaturePreviewed("nature.cast", principal, action, data, Array.Empty<string>());
        }
        var previewId = root.GetProperty("args").TryGetProperty("preview_id", out var given) ? given.GetString() : null;
        if (previewId == null || !_naturePreviews.TryGetValue(previewId, out var planned) || planned.Principal != principal || planned.Signature != signature ||
            planned.Expires <= Clock() || planned.Revision != Revision)
            throw new Refusal("revision_conflict", "Preview this fire again before casting: the place, what it would reach or its permission has changed.", "$.args.preview_id", retryable: true);
        if (keyed && approvedBy == null)
            return Hold(root, principal, action, fingerprint, "nature.cast", "The Gubble asks to light this firepit. Approve this exact firepit or deny it.", new(), bindRevision: true);
        var price = GuardEnergy(ability.Capability, principal);
        _naturePreviews.Remove(previewId);
        data["started_cells"] = 1;
        var committed = CommitNature(Nature.Copy(), "nature.cast", principal, action, fingerprint, approvedBy, Array.Empty<string>(), data, undo: false);
        _litCampfires.Add(firepit.Id);
        NoteCampChanged();
        ChargeEnergy(price);
        RecordExpertise(ExpertiseLists.Fire, ExpertiseLists.Campfire);
        return committed;
    }

    private void NoteCampChanged()
    {
        _teamDirty = true;
        SaveTeam();
        CampsChanged?.Invoke();
    }

    // ---- the start spot (decision 49) ----

    /// <summary>
    /// The boot's placing of the two bodies, once the island save is read and the mode set (RoomWorld): the spot this mode saved, if it
    /// is safe ground (dry, supported and with room for the whole body); else the starting campfire (the spawn, the fire in front), as
    /// on an island's first load. The Gubble goes to its own saved spot, else beside the camp. The camp the player last sat at becomes
    /// where a recovery lands. From then on the island save keeps the spots. Returns "saved", "fallback" or "first".
    /// </summary>
    public string PlaceAtStartSpot()
    {
        EnsureCamps();
        _spotsLive = true;
        if (GetNodeOrNull<CampKeeper>(CampKeeperPath) == null) AddChild(new CampKeeper { Name = CampKeeperName });
        if (Player == null || !IsInstanceValid(Player) || Room == null) return "first";
        var spawn = Room.SpawnFor("player");
        var saved = _startSpots.TryGetValue(Mode, out var spot) ? spot : null;
        string placed;
        // The first load leaves the bodies where the room put them (the spawns, the fire in front of the player): nothing moves.
        if (saved == null) placed = "first";
        else if (VectorOf(saved["player_m"]) is { } there && SafeGround(Player, there)) placed = "saved";
        else
        {
            Player.TryTeleportTo(spawn.PositionM);
            placed = "fallback";
        }
        if (placed != "first" && Companion != null && IsInstanceValid(Companion))
        {
            var gubble = placed == "saved" && VectorOf(saved!["companion_m"]) is { } at && SafeGround(Companion, at);
            if (!gubble) Companion.TryTeleportTo(Room.SpawnFor("companion", spawn).PositionM);
        }
        if (_homeCamp != null && IsCampfireLit(_homeCamp) && _homeSpot is { } home) Player.SetSpawnPoint(home);
        _spotNoted = Player.LastSafePosition;
        GD.Print($"CAMP start spot {placed} ({Mode}); campfires {EnsureCamps().Count} ({EnsureCamps().Count(c => IsCampfireLit(c.Id))} lit): " +
            string.Join(", ", EnsureCamps().Select(c => $"{c.Id} ({c.PositionM.X:0.0}, {c.PositionM.Y:0.00}, {c.PositionM.Z:0.0})")));
        return placed;
    }

    /// <summary>Safe ground for a body: no water there, and the body fits standing on a support (TryTeleportTo puts it there only then).</summary>
    private bool SafeGround(SmallPlayerController body, Vector3 feet) => !WaterColumn(feet, 0.3f, 0.05f).Wet && body.TryTeleportTo(feet);

    private static Vector3? VectorOf(JsonNode? node) =>
        node is JsonArray a && a.Count == 3 && a.All(v => v?.GetValueKind() == JsonValueKind.Number) ? new Vector3(Coordinate(a[0]!), Coordinate(a[1]!), Coordinate(a[2]!)) : null;

    /// <summary>A number however it was made (parsed, or written by this host as a double): read through its JSON text.</summary>
    private static float Coordinate(JsonNode value) => float.Parse(value.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A second's tick (CampKeeper): the island save is due again once the player's footing moved CampLaws.SpotMovedM.</summary>
    internal void NoteCampSpots()
    {
        if (!_spotsLive || Player == null || !IsInstanceValid(Player)) return;
        if (Player.LastSafePosition.DistanceTo(_spotNoted) < CampLaws.SpotMovedM) return;
        _spotNoted = Player.LastSafePosition;
        _teamDirty = true;
    }

    private const string CampKeeperName = "CampKeeper";
    private static readonly NodePath CampKeeperPath = CampKeeperName;

    // ---- the expertise announcement: every expertise newly learned, once ----

    private readonly Queue<(string Ability, string Id)> _learned = new();

    /// <summary>
    /// The next expertise newly learned and saved (Glow's, Bubbles', Fire's: every one PublishProgress adds), for the HUD's chime and
    /// card; each is given once, in the order learned. A store loaded or reconfigured announces nothing.
    /// </summary>
    public bool TakeLearned(out string ability, out string id)
    {
        if (_learned.TryDequeue(out var next)) { (ability, id) = next; return true; }
        ability = id = "";
        return false;
    }

    private void NoteLearned(GlowProgress before, GlowProgress after)
    {
        foreach (var list in ExpertiseLists.All)
            foreach (var id in list.Order)
                if (after.Has(list.Ability, id) && !before.Has(list.Ability, id) && _learned.Count < 16) _learned.Enqueue((list.Ability, id));
    }

    // ---- the island save's camp block (the team block's "camp") and the clock ("clock") ----

    /// <summary>
    /// The camp block: the spots per mode ({"player_m", "companion_m"}), the lit firepits, and the camp the player wakes at with its
    /// spot. Null while there is nothing to keep (a room no boot placed and nothing lit), so a save without camps is unchanged.
    /// </summary>
    internal JsonObject? CampBlock()
    {
        if (!_spotsLive && _startSpots.Count == 0 && _litCampfires.Count == 0 && _homeCamp == null) return null;
        var spots = new JsonObject();
        foreach (var (mode, spot) in _startSpots.OrderBy(p => p.Key, StringComparer.Ordinal))
            if (!(_spotsLive && mode == Mode)) spots[mode] = spot.DeepClone();
        if (_spotsLive && Player != null && IsInstanceValid(Player) && Player.LastSafePosition.IsFinite())
        {
            var now = new JsonObject { ["player_m"] = KernelJson.Vector(Player.LastSafePosition) };
            if (Companion != null && IsInstanceValid(Companion) && Companion.GlobalPosition.IsFinite()) now["companion_m"] = KernelJson.Vector(Companion.GlobalPosition);
            spots[Mode] = now;
        }
        var block = new JsonObject
        {
            ["spots"] = new JsonObject(spots.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, p.Value?.DeepClone()))),
            ["lit"] = new JsonArray(_litCampfires.OrderBy(id => id, StringComparer.Ordinal).Select(id => (JsonNode?)id).ToArray()),
        };
        if (_homeCamp != null && _homeSpot is { } home)
        {
            block["home"] = _homeCamp;
            block["home_m"] = KernelJson.Vector(home);
        }
        return block;
    }

    /// <summary>What is wrong with a saved camp block (or null): the spots per mode, the lit firepits, the home camp and its spot.</summary>
    internal static string? CampBlockProblem(JsonNode? node)
    {
        if (node is not JsonObject block || block.Any(p => p.Key is not ("spots" or "lit" or "home" or "home_m"))) return "the camp";
        if (block["spots"] is not JsonObject spots || spots.Any(p => p.Key is not ("creative" or "challenge" or "live") || p.Value is not JsonObject spot ||
            spot.Any(q => q.Key is not ("player_m" or "companion_m")) || !Vec3(spot["player_m"]) || (spot["companion_m"] != null && !Vec3(spot["companion_m"]))))
            return "the camp's spots";
        if (block["lit"] is not JsonArray lit || lit.Count > CampLaws.MaxFirepits || lit.Any(id => id?.GetValueKind() != JsonValueKind.String ||
            !CampIdPattern.IsMatch(id.GetValue<string>()) || id.GetValue<string>() == CampLaws.StartingId) ||
            lit.Select(id => id!.GetValue<string>()).Distinct().Count() != lit.Count) return "the camp's campfires";
        if ((block["home"] == null) != (block["home_m"] == null) || (block["home"] != null && (block["home"]!.GetValueKind() != JsonValueKind.String ||
            !CampIdPattern.IsMatch(block["home"]!.GetValue<string>()) || !Vec3(block["home_m"])))) return "the camp's home";
        return null;
    }

    /// <summary>The saved camp block, already checked (CampBlockProblem), or null: nothing saved.</summary>
    private void LoadCampBlock(JsonObject? block)
    {
        _litCampfires.Clear();
        _startSpots.Clear();
        _homeCamp = null;
        _homeSpot = null;
        _resting = false;
        if (block != null)
        {
            foreach (var (mode, spot) in block["spots"]!.AsObject()) _startSpots[mode] = (JsonObject)spot!.DeepClone();
            foreach (var id in block["lit"]!.AsArray()) _litCampfires.Add(id!.GetValue<string>());
            if (block["home"] is { } home)
            {
                _homeCamp = home.GetValue<string>();
                _homeSpot = VectorOf(block["home_m"]);
            }
        }
        CampsChanged?.Invoke();
    }

    /// <summary>The island's clock record kept in the island save (M6's x_clock): believed only when the look's reader accepts it.</summary>
    private void LoadClockBlock(JsonNode? node)
    {
        if (node is JsonObject clock && global::EnFractal.Native.Look.WorldClock.FromNode(clock, out var error) != null && error.Length == 0) ClockRecord = (JsonObject)clock.DeepClone();
    }

    /// <summary>The team block with the camp and the clock, when there is any (CommandHostJournal.ExportTeam).</summary>
    private JsonObject WithCamp(JsonObject block)
    {
        if (CampBlock() is { } camp) block["camp"] = camp;
        if (ClockRecord != null) block["clock"] = ClockRecord.DeepClone();
        return block;
    }

    /// <summary>A team block's keys that are the camp's and the clock's (beside the journal's, the map's and whose save it is).</summary>
    private static bool IsCampKey(string key) => key is "camp" or "clock";

    /// <summary>What is wrong with a team block's camp and clock (the clock only in shape: the look's reader decides whether it is believed).</summary>
    private static string? CampKeysProblem(JsonObject team)
    {
        if (team["camp"] is { } camp && CampBlockProblem(camp) is { } problem) return problem;
        if (team["clock"] is { } clock && (clock is not JsonObject record || record.Count > 16)) return "the island's clock";
        return null;
    }
}

/// <summary>The host's second-long tick for the start spot (added by PlaceAtStartSpot, so only a booted island has it).</summary>
public partial class CampKeeper : Node
{
    private double _age;

    public override void _PhysicsProcess(double delta)
    {
        _age += delta;
        if (_age < 1) return;
        _age = 0;
        if (GetParent() is CommandHost host) host.NoteCampSpots();
    }
}

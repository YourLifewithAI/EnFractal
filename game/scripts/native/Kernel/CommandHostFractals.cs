using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using EnFractal.Native.Sandbox;

namespace EnFractal.Native.Kernel;

/// <summary>One Fractal of the island (the manifest's extensions.x_fractals): its id, where it rests (bottom centre), and its kind when typed.</summary>
public sealed record IslandFractal(string Id, Vector3 PositionM, string? Kind, int? ReachBand);

/// <summary>
/// Fractals (docs/game/MECHANICS.md section 6; contracts/README.md "The mechanics round"): the found things that grow the
/// body, placed by the generator in the manifest's extensions.x_fractals (packet M4) and read here from the room's pinned
/// manifest, fail-closed (a manifest whose block does not check has none). The player alone picks one up (item.pickup, a
/// player-only op): within reach and with a clear way to it, it leaves the island save into the team's count for this mode
/// (the vitals' fractals), as one durable room revision with its receipt. The island save keeps the ids picked up in the
/// host's nature block (x_play_nature's "fractals"), never an edit of the manifest; when the torch round's item kind lands
/// in room state, a picked Fractal can become an entity record of that kind. The Gubble may see, go to and look at one
/// (kind fractal in perception, in sight now; its map does not keep them yet), never gather it (NamesAFractal). Picking up
/// costs no pips and is the same in every mode; Creative's count is its own.
/// </summary>
public partial class CommandHost
{
    /// <summary>A Fractal's size for sight and reach: a small box from its bottom centre (the look draws it within this).</summary>
    public const float FractalHalfWidthM = 0.02f;
    public const float FractalHeightM = 0.04f;
    public const int MaxFractals = 1000;
    /// <summary>Why the island has no Fractals although its manifest names some (its block did not check), for the player; empty otherwise.</summary>
    public string FractalsNotice { get; private set; } = "";
    private IReadOnlyList<IslandFractal>? _fractals;
    private bool _fractalsTyped;

    /// <summary>Every Fractal the manifest places, picked up or not, in id order.</summary>
    public IReadOnlyList<IslandFractal> AllFractals => _fractals ??= LoadFractals();

    /// <summary>The Fractals still on the island (not picked up in this save).</summary>
    public IEnumerable<IslandFractal> Fractals => AllFractals.Where(f => !Nature.FractalsPicked.Contains(f.Id));

    /// <summary>The Fractals within the player's reach now, for the HUD's pickup hint.</summary>
    public IReadOnlyList<string> FractalsInReach()
    {
        if (Player == null || !IsInstanceValid(Player)) return Array.Empty<string>();
        return Fractals.Where(f => SandboxRules.WithinReach(Player, FractalBox(f).Grow(0.01f))).Select(f => f.Id).ToList();
    }

    public static Aabb FractalBox(IslandFractal fractal) =>
        new(fractal.PositionM - new Vector3(FractalHalfWidthM, 0, FractalHalfWidthM), new Vector3(FractalHalfWidthM * 2, FractalHeightM, FractalHalfWidthM * 2));

    public JsonObject PlayerPickUp(string fractal, bool preview = false) => PlayerCommand("item.pickup", new JsonObject { ["target"] = fractal }, preview);

    /// <summary>The manifest's x_fractals, read from the room's pinned manifest and checked as contracts/room-manifest.schema.json and validate.py do.</summary>
    private IReadOnlyList<IslandFractal> LoadFractals()
    {
        FractalsNotice = "";
        if (Room == null || string.IsNullOrEmpty(Room.Directory)) return Array.Empty<IslandFractal>();
        var path = Room.Directory + "/room.json";
        if (!Godot.FileAccess.FileExists(path)) return Array.Empty<IslandFractal>();
        var bytes = Godot.FileAccess.GetFileAsBytes(path);
        // The room as loaded: the manifest's own bytes, never a file that changed since.
        if (bytes.Length == 0 || CanonicalJson.Sha256Hex(bytes) != Room.ManifestSha256) return Refuse("its manifest changed since the room was loaded");
        try
        {
            using var document = CanonicalJson.Parse(bytes);
            if (!document.RootElement.TryGetProperty("extensions", out var extensions) || extensions.ValueKind != JsonValueKind.Object ||
                !extensions.TryGetProperty("x_fractals", out var block)) return Array.Empty<IslandFractal>();
            return ParseFractals(block, Room.Bounds, out _fractalsTyped) ?? Refuse("its Fractals do not check");
        }
        catch (CanonicalJsonException) { return Refuse("its manifest is not strict JSON"); }
    }

    private IReadOnlyList<IslandFractal> Refuse(string why)
    {
        FractalsNotice = $"This island's Fractals were not loaded: {why}.";
        GD.Print("COMMAND_HOST " + FractalsNotice);
        return Array.Empty<IslandFractal>();
    }

    /// <summary>x_fractals checked: typed and items only; each item an id of one spelling, listed once, inside the bounds, a kind only when typed; null for any problem.</summary>
    internal static IReadOnlyList<IslandFractal>? ParseFractals(JsonElement block, Aabb bounds, out bool typed)
    {
        typed = false;
        if (block.ValueKind != JsonValueKind.Object || block.EnumerateObject().Any(p => p.Name is not ("typed" or "items")) ||
            !block.TryGetProperty("typed", out var flag) || flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            !block.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > MaxFractals) return null;
        typed = flag.ValueKind == JsonValueKind.True;
        var list = new List<IslandFractal>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || item.EnumerateObject().Any(p => p.Name is not ("id" or "position_m" or "kind" or "reach_band"))) return null;
            if (Str(item, "id") is not { } id || !FractalId.IsMatch(id) || !item.TryGetProperty("position_m", out var at) || at.ValueKind != JsonValueKind.Array ||
                at.GetArrayLength() != 3 || at.EnumerateArray().Any(v => v.ValueKind != JsonValueKind.Number)) return null;
            var position = new Vector3((float)at[0].GetDouble(), (float)at[1].GetDouble(), (float)at[2].GetDouble());
            if (!position.IsFinite() || !bounds.Grow(0.001f).HasPoint(position)) return null;
            string? kind = null;
            if (item.TryGetProperty("kind", out var k))
            {
                kind = k.ValueKind == JsonValueKind.String ? k.GetString() : null;
                if (!typed || kind is not ("heart" or "band")) return null;
            }
            else if (typed) return null;
            int? band = null;
            if (item.TryGetProperty("reach_band", out var reach))
            {
                if (!IsIntegerLiteral(reach) || reach.GetInt64() is < 1 or > 20) return null;
                band = reach.GetInt32();
            }
            list.Add(new IslandFractal(id, position, kind, band));
        }
        if (list.Select(f => f.Id).Distinct(StringComparer.Ordinal).Count() != list.Count) return null;
        list.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        return list;
    }

    /// <summary>The Fractals still on the island as entity summaries (kind fractal): small, not movable, placed by the generator.</summary>
    private void AddFractals(List<(string Id, JsonObject Summary)> list)
    {
        var name = Rules?.Names?.Words.TryGetValue("fractals", out var word) == true ? Singular(word) : "Fractal";
        foreach (var fractal in Fractals)
        {
            var box = FractalBox(fractal);
            list.Add((fractal.Id, Summary(fractal.Id, "fractal", name, null, null, fractal.PositionM, box, Array.Empty<string>(), false, false, "procedural", 0)));
        }
    }

    /// <summary>The pack's word for Fractals, as one (untrusted display text: "Fractals" reads "Fractal").</summary>
    private static string Singular(string word) => word.Length > 1 && word.EndsWith('s') ? word[..^1] : word;

    /// <summary>
    /// item.pickup: the player picks a Fractal up. It must be on the island now (target_not_found otherwise, as any id), within
    /// the player's reach with a clear way to it (out_of_bounds, retryable: come closer). Committed as one durable room
    /// revision (the island save's nature block keeps it as picked up), then counted into this mode's vitals; a pace line.
    /// </summary>
    private JsonObject PickUpFractal(JsonElement args, string principal, string actionId, string fingerprint, bool preview)
    {
        if (principal != PlayerPrincipal) throw new Refusal("permission_denied", FractalRefusal, "$.args.target");
        RequireChange(principal);
        var target = Str(args, "target")!;
        var fractal = Fractals.FirstOrDefault(f => f.Id == target) ?? throw new Refusal("target_not_found", "That is not in this room.", "$.args.target");
        if (Player == null || !IsInstanceValid(Player)) throw new Refusal("not_ready", "The player is not in the room.", retryable: true);
        var box = FractalBox(fractal);
        if (!SandboxRules.WithinReach(Player, box.Grow(0.01f))) throw OutOfReach("$.args.target");
        if (!Physics().ReachClear(Player, box, new Rid()))
            throw new Refusal("out_of_bounds", "That is out of reach: something is in the way.", "$.args.target", retryable: true);
        if (_playerVitalsUnavailable) throw new Refusal("not_ready", VitalsNotice, retryable: true);
        if (preview) return Previewed("item.pickup", principal, actionId, target);
        var candidate = Nature.Copy();
        candidate.FractalsPicked.Add(fractal.Id);
        // The receipt names the Fractal in its data (the authority knows no fractal: ids); the result carries it in affected.
        var result = CommitNature(candidate, "item.pickup", principal, actionId, fingerprint, null, Array.Empty<string>(), new JsonObject { ["fractal"] = fractal.Id }, undo: false);
        var found = Vitals.With(fractals: Vitals.Fractals.Picked(_fractalsTyped ? fractal.Kind : null));
        _playerModes = _playerModes.WithCurrent(found);
        _vitalsDirty = true;
        FlushPlayerVitals();
        NotePace("fractal", found.Fractals.Found);
        return result;
    }
}

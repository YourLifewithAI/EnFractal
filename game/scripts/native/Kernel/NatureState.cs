using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace EnFractal.Native.Kernel;

/// <summary>Saved simulation data; the meshes are a disposable view of these cells.</summary>
public sealed class NatureCell
{
    public int X { get; set; }
    public int Z { get; set; }
    public float Y { get; set; }
    public string Surface { get; set; } = "";
    public string Material { get; set; } = "soil";
    public bool Fertile { get; set; }
    public float Moisture { get; set; } = .35f;
    public float Growth { get; set; }
    public float LifeS { get; set; }
    public float Fuel { get; set; }
    public float BurningS { get; set; }
    public float Ash { get; set; }
    public float Light { get; set; } = .5f;
    public bool Exposed { get; set; } = true;
    [JsonIgnore] public string Key => NatureState.Key(X, Z);
    [JsonIgnore] public Vector3 Position => new((X + .5f) * NatureState.CellM, Y, (Z + .5f) * NatureState.CellM);
    public NatureCell Copy() => (NatureCell)MemberwiseClone();
}

public sealed class NatureObject
{
    public string Id { get; set; } = "";
    public bool Creation { get; set; }
    public float Burn { get; set; }
    public string SourceHash { get; set; } = "";
    public Dictionary<string, int> RepairCost { get; set; } = new();
    public NatureObject Copy() => new() { Id = Id, Creation = Creation, Burn = Burn, SourceHash = SourceHash, RepairCost = new(RepairCost) };
}

public sealed class NatureFire
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float RadiusM { get; set; }
    public float RemainingS { get; set; }
    public int AuthorityRevision { get; set; }
    // Only these creations can be harmed. A newly built or revised creation never inherits an old approval.
    public Dictionary<string, int> AuthorizedCreations { get; set; } = new();
    [JsonIgnore] public Vector3 Center => new(X, Y, Z);
}

public sealed class NatureUndoStep
{
    // Captured by the host. Older saves without ownership remain player-owned.
    public string Principal { get; init; } = CommandHost.PlayerPrincipal;
    public int AuthorityRevision { get; set; }
    public int NatureRevision { get; set; }
    public string Label { get; set; } = "";
    public Dictionary<string, NatureCell?> Cells { get; set; } = new();
    public Dictionary<string, NatureObject?> Objects { get; set; } = new();
    public Dictionary<string, int> Wallet { get; set; } = new();
    public bool TrailEnabled { get; set; }
    public string TrailPrincipal { get; set; } = "player";
    public string TrailRulesSha256 { get; set; } = "";
}

public readonly record struct NatureWeather(float Rain01, Vector3 WindMps, string Pattern);

public sealed class NatureState
{
    public const float CellM = .05f;
    public const int MaxCells = 16000;
    public const int MaxBurning = 200;
    public const float MaxFireRadiusM = 1.5f;
    public const int MaxReceipts = 1024;
    public string Schema { get; set; } = "enfractal.nature_state";
    public int Version { get; set; } = 1;
    public string RoomId { get; set; } = "";
    public string ManifestSha256 { get; set; } = "";
    public int Revision { get; set; }
    public double WeatherS { get; set; }
    public bool TrailEnabled { get; set; } = true;
    public string TrailPrincipal { get; set; } = "player";
    public string TrailRulesSha256 { get; set; } = "";
    public Dictionary<string, NatureCell> Cells { get; set; } = new();
    public Dictionary<string, NatureObject> Objects { get; set; } = new();
    // Prototype material accounting: one unit per original authored part; no prior economy exists.
    public Dictionary<string, int> Wallet { get; set; } = new() { ["wood"] = 20, ["stone"] = 10, ["copper"] = 10, ["leaf"] = 20 };
    public NatureFire? Fire { get; set; }
    public NatureUndoStep? Undo { get; set; }
    public Dictionary<string, JsonObject> Receipts { get; set; } = new();
    public static string Key(int x, int z) => $"{x}:{z}";
    public static (int X, int Z) Grid(Vector3 at) => ((int)Math.Floor(at.X / CellM), (int)Math.Floor(at.Z / CellM));
    public static NatureState Fresh(string room, string hash) => new() { RoomId = room, ManifestSha256 = hash };
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    public NatureState Copy() => JsonSerializer.Deserialize<NatureState>(JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions), JsonOptions)!;
    public JsonObject ToJson() => JsonSerializer.SerializeToNode(this, JsonOptions)!.AsObject();
    [JsonIgnore] public NatureWeather Weather
    {
        get
        {
            // A six-minute local sequence. Saving its clock resumes the same weather, with no offline catch-up.
            var t = (float)(WeatherS % 360);
            var rain = t < 120 ? 0 : t < 165 ? (t - 120) / 45 : t < 225 ? 1 : t < 270 ? (270 - t) / 45 : 0;
            var direction = (float)(WeatherS / 150);
            return new NatureWeather(rain, new Vector3(MathF.Cos(direction), 0, MathF.Sin(direction)) * (.12f + .22f * rain), rain > .65f ? "rain" : rain > .02f ? "showers" : "fair");
        }
    }

    public static IEnumerable<(int X, int Z)> Disk(Vector3 center, float radius)
    {
        var (cx, cz) = Grid(center);
        var span = (int)Math.Ceiling(radius / CellM) + 1;
        for (var x = cx - span; x <= cx + span; x++)
        for (var z = cz - span; z <= cz + span; z++)
            if (new Vector2((x + .5f) * CellM - center.X, (z + .5f) * CellM - center.Z).LengthSquared() <= radius * radius)
                yield return (x, z);
    }

    public void BeginUndo(string label, int authorityRevision, string principal = CommandHost.PlayerPrincipal)
    {
        Revision++;
        Undo = new NatureUndoStep { Principal = principal, AuthorityRevision = authorityRevision, NatureRevision = Revision, Label = label, Wallet = new(Wallet), TrailEnabled = TrailEnabled, TrailPrincipal = TrailPrincipal, TrailRulesSha256 = TrailRulesSha256 };
    }
    public void RememberCell(string key)
    {
        if (Undo != null && !Undo.Cells.ContainsKey(key)) Undo.Cells[key] = Cells.TryGetValue(key, out var value) ? value.Copy() : null;
    }
    public void RememberObject(string key)
    {
        if (Undo != null && !Undo.Objects.ContainsKey(key)) Undo.Objects[key] = Objects.TryGetValue(key, out var value) ? value.Copy() : null;
    }
    public void UndoLast()
    {
        if (Undo == null) throw new InvalidOperationException("No nature change can be undone.");
        foreach (var (key, cell) in Undo.Cells) { if (cell == null) Cells.Remove(key); else Cells[key] = cell.Copy(); }
        foreach (var (key, item) in Undo.Objects) { if (item == null) Objects.Remove(key); else Objects[key] = item.Copy(); }
        Wallet = new(Undo.Wallet); TrailEnabled = Undo.TrailEnabled; TrailPrincipal = Undo.TrailPrincipal; TrailRulesSha256 = Undo.TrailRulesSha256; Fire = null; Undo = null; Revision++;
        foreach (var cell in Cells.Values) cell.BurningS = 0;
    }

    /// <summary>Plant only on a verified physical fertile surface, never on an assumed rectangle.</summary>
    public int Bloom(Vector3 center, float radius, float duration, Func<int, int, NatureCell?> sample, bool remember = true)
    {
        var planted = 0;
        foreach (var (x, z) in Disk(center, radius))
        {
            var key = Key(x, z);
            var ground = sample(x, z);
            if (ground is not { Fertile: true } || ground.BurningS > 0) continue;
            if (!Cells.ContainsKey(key) && Cells.Count >= MaxCells) break;
            if (remember) RememberCell(key);
            var cell = Cells.GetValueOrDefault(key) ?? ground;
            cell.Y = ground.Y; cell.Surface = ground.Surface; cell.Exposed = ground.Exposed;
            cell.Growth = Math.Max(.2f, cell.Growth); cell.LifeS = Math.Max(cell.LifeS, duration);
            cell.Fuel = Math.Max(cell.Fuel, .35f); Cells[key] = cell; planted++;
        }
        return planted;
    }

    public int Ignite(Vector3 center, float radius, float duration, int authorityRevision, Dictionary<string, int> authorized, Func<int, int, NatureCell?> sample)
    {
        Fire = new NatureFire { X = center.X, Y = center.Y, Z = center.Z, RadiusM = radius, RemainingS = duration, AuthorityRevision = authorityRevision, AuthorizedCreations = new(authorized) };
        var lit = 0;
        foreach (var (x, z) in Disk(center, Math.Min(radius, .13f)))
        {
            var key = Key(x, z);
            var ground = sample(x, z);
            if (ground == null || ground.Fuel <= 0 || ground.Moisture >= .8f) continue;
            if (!Cells.ContainsKey(key) && Cells.Count >= MaxCells) break;
            RememberCell(key);
            var cell = Cells.GetValueOrDefault(key) ?? ground;
            if (cell.Moisture >= .8f || cell.Fuel <= 0) continue;
            cell.BurningS = Math.Min(10, duration); Cells[key] = cell; lit++;
        }
        return lit;
    }

    /// <summary>A bounded deterministic step: wind biases spread; rain/wet soil and nonfuel gaps stop it.</summary>
    public void Step(float seconds, Func<int, int, NatureCell?> sample, Func<NatureCell, bool> fireproof, Func<Vector3, float> light)
    {
        if (!float.IsFinite(seconds) || seconds <= 0 || seconds > 1) throw new ArgumentOutOfRangeException(nameof(seconds));
        WeatherS += seconds;
        var weather = Weather;
        var burning = Cells.Values.Where(c => c.BurningS > 0).OrderBy(c => c.Key, StringComparer.Ordinal).Take(MaxBurning).ToArray();
        var next = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cell in Cells.Values)
        {
            cell.Light = Math.Clamp(light(cell.Position + Vector3.Up * .02f), 0, 2);
            cell.Moisture = Math.Clamp(cell.Moisture + seconds * ((cell.Exposed ? weather.Rain01 * .18f : 0) - .009f * cell.Light), 0, 1);
            cell.LifeS = Math.Max(0, cell.LifeS - seconds);
            if (cell.BurningS > 0) continue;
            if (cell.LifeS > 0 && cell.Moisture > .12f && cell.Light > .12f)
                cell.Growth = Math.Min(1, cell.Growth + seconds * .035f * cell.Moisture * Math.Min(1, cell.Light));
            else cell.Growth = Math.Max(0, cell.Growth - seconds * (cell.LifeS > 0 ? .006f : .025f));
            if (cell.Ash > 0 && cell.Fertile && cell.Moisture > .2f)
            {
                cell.Ash = Math.Max(0, cell.Ash - seconds * .006f * cell.Moisture);
                cell.Fuel = Math.Min(.35f, cell.Fuel + seconds * .002f * cell.Moisture);
            }
        }
        foreach (var cell in burning)
        {
            var surface = sample(cell.X, cell.Z);
            if (Fire == null || Fire.RemainingS <= 0 || fireproof(cell) || surface is not { Fuel: > 0 } || Math.Abs(surface.Y - cell.Y) > .04f || cell.Moisture >= .8f)
            { cell.BurningS = 0; continue; }
            RememberCell(cell.Key);
            cell.Fuel = Math.Max(0, cell.Fuel - seconds * .08f);
            cell.Growth = 0; cell.LifeS = 0; cell.Ash = Math.Min(1, cell.Ash + seconds * .2f);
            cell.BurningS = Math.Max(0, cell.BurningS - seconds * (1 + weather.Rain01 * 5));
            if (cell.Fuel <= .005f) cell.BurningS = 0;
            if (cell.BurningS <= 0 || weather.Rain01 >= .8f) continue;
            foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                if (burning.Length + next.Count >= MaxBurning || Cells.Count + next.Count >= MaxCells) break;
                var x = cell.X + dx; var z = cell.Z + dz; var key = Key(x, z);
                if (next.Contains(key) || Cells.TryGetValue(key, out var existing) && existing.BurningS > 0) continue;
                var ground = sample(x, z);
                if (ground == null || ground.Fuel <= 0 || ground.Moisture >= .7f || fireproof(ground)) continue;
                if (new Vector2(ground.Position.X - Fire.Center.X, ground.Position.Z - Fire.Center.Z).Length() > Fire.RadiusM || Math.Abs(ground.Y - cell.Y) > .07f) continue;
                var fuel = existing ?? ground;
                if (fuel.Fuel < .03f || fuel.Moisture >= .7f) continue;
                // A reproducible rotating gate keeps one half-second update from burning the entire circle.
                var downwind = new Vector3(dx, 0, dz).Dot(weather.WindMps) > 0;
                var gate = Math.Abs((long)x * 31 + (long)z * 17 + (long)(WeatherS * 2)) % (downwind ? 3 : 7);
                if (gate != 0) continue;
                RememberCell(key); next.Add(key);
            }
        }
        foreach (var key in next)
        {
            var pieces = key.Split(':'); var x = int.Parse(pieces[0]); var z = int.Parse(pieces[1]);
            var cell = Cells.GetValueOrDefault(key) ?? sample(x, z)!;
            cell.BurningS = Math.Min(8, Fire!.RemainingS); Cells[key] = cell;
        }
        if (Fire != null)
        {
            Fire.RemainingS = Math.Max(0, Fire.RemainingS - seconds);
            if (Fire.RemainingS <= 0 || !Cells.Values.Any(c => c.BurningS > 0)) { foreach (var cell in Cells.Values) cell.BurningS = 0; Fire = null; }
        }
        // Expired flower-only cells can be sampled again. Ash stays until regrowth consumes it.
        foreach (var key in Cells.Where(p => p.Value.Growth <= 0 && p.Value.LifeS <= 0 && p.Value.BurningS <= 0 && p.Value.Ash <= 0).Select(p => p.Key).ToArray()) Cells.Remove(key);
    }
}

using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using EnFractal.Native.Look;
using EnFractal.Native.Room;

namespace EnFractal.Native.Kernel;

public partial class CommandHost
{
    public const int FireLimit = 6;
    public int ActiveFireCount => _fires.Count;
    private sealed class FireState
    {
        public required string Id, Principal;
        public required Vector3 Origin, Position, Velocity;
        public required float Lifetime;
        public required FireVisual Visual;
        public float Age;
        public bool Ignites;
    }
    private readonly Dictionary<string, FireState> _fires = new();

    /// <summary>Manual casting supplies only typed data. The trusted host selects the player's principal.</summary>
    public JsonObject PlayerFire(bool bolt)
    {
        if (Player == null) return Fail("effect.start", PlayerPrincipal, null, null, new Refusal("not_ready", "The player is not ready."));
        var eye = Player.EyeCamera.GlobalPosition;
        var direction = -Player.GlobalBasis.Z;
        var position = eye + direction * 0.07f;
        if (!bolt)
        {
            var end = eye + direction * 0.42f;
            var hit = FireRay(eye, end);
            if (hit.Count > 0) end = hit["position"].AsVector3() + hit["normal"].AsVector3() * 0.035f;
            var floor = FireRay(end, end + Vector3.Down * 0.6f);
            if (floor.Count == 0) return Fail("effect.start", PlayerPrincipal, null, null, new Refusal("out_of_bounds", "Face a nearby surface to place a flame."));
            position = floor["position"].AsVector3() + Vector3.Up * 0.003f;
        }
        return PlayerCommand("effect.start", new JsonObject
        {
            ["capability"] = Room.Ecology.Count > 0 ? (bolt ? "ignition_bolt" : "wildfire") : (bolt ? "firebolt" : "flame"), ["duration_s"] = bolt ? 2 : 12,
            ["area"] = new JsonObject { ["center_m"] = KernelJson.Vector(position), ["radius_m"] = 0.04 },
            ["params"] = bolt ? new JsonObject { ["direction_x"] = direction.X, ["direction_y"] = direction.Y, ["direction_z"] = direction.Z } : new JsonObject(),
        });
    }
    public JsonObject PlayerStopFire() => PlayerCommand("effect.stop", new JsonObject { ["effect"] = "all" });

    private JsonObject StartFire(JsonElement args, string principal, string actionId, string fingerprint, bool preview)
    {
        var capability = Str(args, "capability");
        if (capability is not ("flame" or "firebolt" or "wildfire" or "ignition_bolt")) throw new Refusal("unsupported_capability", "Choose flame or firebolt.");
        var bolt = capability is "firebolt" or "ignition_bolt";
        var ignites = capability is "wildfire" or "ignition_bolt";
        if (ignites && Room.Ecology.Count == 0) throw new Refusal("unsupported_capability", "This island has no authored combustible parts.");
        if (args.TryGetProperty("targets", out var targets) && (targets.ValueKind != JsonValueKind.Array || targets.GetArrayLength() != 0))
            throw new Refusal("invalid_args", "Fire is visual magic: it does not target or damage entities.");
        if (!args.TryGetProperty("area", out var area) || area.ValueKind != JsonValueKind.Object ||
            area.EnumerateObject().Any(p => p.Name is not ("center_m" or "radius_m")) || !area.TryGetProperty("center_m", out var center) ||
            !area.TryGetProperty("radius_m", out var radius) || radius.ValueKind != JsonValueKind.Number || radius.GetDouble() is < 0.01 or > 0.05)
            throw new Refusal("invalid_args", "Fire needs a center and a radius from 1 to 5 cm.");
        CheckVector(center, "$.args.area.center_m");
        var at = KernelJson.ReadVector(center);
        if (!args.TryGetProperty("duration_s", out var duration) || duration.ValueKind != JsonValueKind.Number ||
            duration.GetDouble() < 0.1 || duration.GetDouble() > (bolt ? 2 : 15))
            throw new Refusal("invalid_args", bolt ? "A firebolt lasts at most 2 seconds." : "A flame lasts at most 15 seconds.");
        var body = principal == PlayerPrincipal ? Player : Companion;
        if (body == null || !IsInstanceValid(body)) throw new Refusal("not_ready", "The casting avatar is not ready.");
        var eye = body.EyeCamera.GlobalPosition;
        if (!Room.Bounds.HasPoint(at) || eye.DistanceTo(at) > (bolt ? 0.15f : 0.7f))
            throw new Refusal("out_of_bounds", "Cast nearby, inside this room.");
        var obstruction = FireRay(eye, at);
        if (obstruction.Count > 0 && obstruction["position"].AsVector3().DistanceTo(at) > 0.01f)
            throw new Refusal("occupied", "A surface blocks that cast.");
        var parameters = args.GetProperty("params");
        var direction = Vector3.Zero;
        if (!bolt && parameters.EnumerateObject().Any()) throw new Refusal("field_unknown", "Flame takes no extra parameters.");
        if (bolt)
        {
            var names = new[] { "direction_x", "direction_y", "direction_z" };
            if (parameters.EnumerateObject().Count() != 3 || names.Any(n => !parameters.TryGetProperty(n, out var v) || v.ValueKind != JsonValueKind.Number || Math.Abs(v.GetDouble()) > 1))
                throw new Refusal("invalid_args", "Firebolt needs direction_x, direction_y and direction_z, a unit direction.");
            direction = new Vector3(parameters.GetProperty(names[0]).GetSingle(), parameters.GetProperty(names[1]).GetSingle(), parameters.GetProperty(names[2]).GetSingle());
            if (Mathf.Abs(direction.Length() - 1) > 0.01f) throw new Refusal("invalid_args", "The firebolt direction must have length one.");
        }
        if (_fires.Count >= FireLimit) throw new Refusal("budget_exceeded", "Six flames are already active. Stop them or wait for them to fade.");
        if (preview) return Previewed("effect.start", principal, actionId);
        var id = "effect:" + Guid.NewGuid().ToString("N");
        var visual = new FireVisual { Name = "Fire_" + id[7..], Scale = Vector3.One * ((float)radius.GetDouble() / 0.04f) };
        AddChild(visual); visual.GlobalPosition = at;
        _fires.Add(id, new FireState { Id = id, Principal = principal, Origin = at, Position = at,
            Velocity = direction * 1.1f, Lifetime = (float)duration.GetDouble(), Visual = visual, Ignites = ignites });
        return Transient("effect.start", principal, actionId, fingerprint, new JsonArray(id), new JsonObject { ["effect_id"] = id });
    }

    private Godot.Collections.Dictionary FireRay(Vector3 from, Vector3 to)
    {
        using var ray = PhysicsRayQueryParameters3D.Create(from, to, RoomBuilder.WorldLayer);
        return GetWorld3DForFire()?.DirectSpaceState.IntersectRay(ray) ?? new Godot.Collections.Dictionary();
    }
    private World3D? GetWorld3DForFire() => Player?.GetWorld3D() ?? Companion?.GetWorld3D();

    private int StopFire(string principal, string id = "all")
    {
        var selected = _fires.Values.Where(f => (principal.Length == 0 || f.Principal == principal) && (id == "all" || f.Id == id)).ToArray();
        if (id == "all") DouseEcology(principal);
        foreach (var fire in selected) { _fires.Remove(fire.Id); fire.Visual.QueueFree(); }
        return selected.Length;
    }

    private void TickFire(double delta)
    {
        foreach (var fire in _fires.Values.ToArray())
        {
            fire.Age += (float)delta;
            var space = GetWorld3DForFire()?.DirectSpaceState;
            if (space != null) { var water = RoomWater.At(space,fire.Position); if (water.Wet && water.SurfaceY >= fire.Position.Y) { StopFire("",fire.Id); continue; } }
            var next = fire.Position + fire.Velocity * (float)delta;
            var hit = fire.Velocity != Vector3.Zero ? FireRay(fire.Position,next) : new Godot.Collections.Dictionary();
            if (hit.Count > 0 && fire.Ignites) _boltHeat[fire.Id] = (hit["position"].AsVector3(),fire.Principal);
            if (fire.Age >= fire.Lifetime || !Room.Bounds.HasPoint(next) || fire.Origin.DistanceTo(next) > 2.2f ||
                hit.Count > 0)
            { StopFire("", fire.Id); continue; }
            fire.Position = next;
            fire.Visual.GlobalPosition = fire.Position;
            fire.Visual.ShowAge(fire.Age, fire.Velocity != Vector3.Zero);
        }
    }

    private JsonObject FireCapabilities(JsonElement args)
    {
        if (args.TryGetProperty("cursor", out var cursor) && (!int.TryParse(cursor.GetString(), out var offset) || offset < 0 || offset >= (Room.Ecology.Count > 0 ? 4 : 2)))
            throw new Refusal("invalid_args", "That cursor is not from this list.");
        var items = new JsonArray();
        if (Str(args, "category") is null or "fire")
            foreach (var name in Room.Ecology.Count > 0 ? new[] { "flame", "firebolt", "wildfire", "ignition_bolt" } : new[] { "flame", "firebolt" })
            {
                var parameters = new JsonObject();
                if (name is "firebolt" or "ignition_bolt") foreach (var axis in new[] { "x", "y", "z" })
                    parameters["direction_" + axis] = new JsonObject { ["min"] = -1, ["max"] = 1 };
                items.Add(new JsonObject { ["capability"] = name, ["category"] = "fire", ["params"] = parameters,
                    ["area_radius_max_m"] = 0.05, ["duration_max_s"] = name is "flame" or "wildfire" ? 15 : 2 });
            }
        var start = int.TryParse(Str(args,"cursor"),out var parsed) ? parsed : 0;
        var limit = args.TryGetProperty("limit", out var value) ? value.GetInt32() : 20;
        var result = new JsonObject { ["items"] = new JsonArray(items.Skip(start).Take(limit).Select(i => i!.DeepClone()).ToArray()) };
        if (start + limit < items.Count) result["next_cursor"] = (start + limit).ToString();
        return result;
    }
}

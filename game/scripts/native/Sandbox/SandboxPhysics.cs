using Godot;
using System;
using System.Collections.Generic;
using EnFractal.Native.Room;

namespace EnFractal.Native.Sandbox;

/// <summary>A thing a verb moves: its body (excluded from its own queries), size and pose, the pivot at its bottom centre.</summary>
public readonly record struct Thing(string Id, Rid Body, Vector3 Size, Vector3 Position, Quaternion Rotation);

/// <summary>Where a verb leaves a thing: its pose, the bounds it then occupies and the entity it rests on ("" when none is known).</summary>
public readonly record struct Rest(Vector3 Position, Quaternion Rotation, Aabb Bounds, string Support);

/// <summary>Why the room's physics will not take a verb: a contract error code and a plain message for the player or the companion.</summary>
public sealed record Blocked(string Code, string Message, bool Retryable = false);

/// <summary>
/// Where the sandbox verbs leave things, worked out with Jolt's shape queries against the room's real colliders at the
/// moment the command arrives, so a result is decided before its receipt is written and replays exactly.
/// - The query shape is the thing's box (its asset dimensions at its scale and rotation, pivot at the bottom centre),
///   half a millimetre narrower on each side so things set flush against each other are not taken for overlapping.
/// - Dropping and placing start the thing just above where it goes and sweep it straight down onto the first support;
///   the rest height is then snapped to that support's surface by rays, so stacks sit exactly on their supports.
/// - A push slides the thing a centimetre at a time a millimetre above its support, against the world and both avatars:
///   it stops early at an obstacle, climbs a ledge up to a quarter of its own height (at most 1 cm, so a book rides over
///   a rug's edge), settles after every step and falls when it leaves its support. A push that leaves it hanging with
///   its centre over nothing carries on until it tips off, at most half its diagonal further.
/// Things stay upright: there is no tipping over, rolling or sliding once at rest.
/// </summary>
public sealed class SandboxPhysics
{
    public const uint WorldMask = RoomBuilder.WorldLayer;
    /// <summary>The avatars' collision layer (SmallPlayerController).</summary>
    public const uint AvatarMask = 2;
    public const float SideClearanceM = 0.0005f;
    public const float LiftM = 0.001f;
    public const float SlideStepM = 0.01f;
    public const float MaxStepUpM = 0.01f;
    /// <summary>After a slide step, a thing with nothing this close under it falls.</summary>
    public const float FallAfterM = 0.02f;
    /// <summary>A placement's height is a hint: the thing starts this far above it and settles.</summary>
    public const float ProbeM = 0.05f;
    public const float SnapWindowM = 0.003f;
    /// <summary>A push that moves a thing less than this along its support has moved nothing.</summary>
    public const float MinPushM = 0.001f;
    public const float PoseGridM = 0.0001f;

    private readonly PhysicsDirectSpaceState3D _space;
    private readonly Aabb _room;

    public SandboxPhysics(PhysicsDirectSpaceState3D space, Aabb room)
    {
        _space = space;
        _room = room;
    }

    /// <summary>The axis-aligned bounds of a thing at a pose.</summary>
    public static Aabb Bounds(Vector3 pivot, Quaternion rotation, Vector3 size)
    {
        var basis = new Basis(rotation);
        Aabb? box = null;
        foreach (var x in new[] { -0.5f, 0.5f })
        foreach (var y in new[] { 0f, 1f })
        foreach (var z in new[] { -0.5f, 0.5f })
        {
            var corner = pivot + basis * new Vector3(size.X * x, size.Y * y, size.Z * z);
            box = box == null ? new Aabb(corner, Vector3.Zero) : box.Value.Expand(corner);
        }
        return box!.Value;
    }

    /// <summary>Half the thing's horizontal extent along a horizontal direction.</summary>
    public static float HalfAlong(Quaternion rotation, Vector3 size, Vector3 direction)
    {
        var basis = new Basis(rotation);
        return Mathf.Abs(direction.Dot(basis.X)) * size.X * 0.5f + Mathf.Abs(direction.Dot(basis.Z)) * size.Z * 0.5f;
    }

    /// <summary>Release with no placement: set down in front of the actor, then settle.</summary>
    public (Rest? At, Blocked? Blocked) Drop(Thing thing, SmallPlayerController actor)
    {
        var facing = -actor.GlobalBasis.Z;
        facing.Y = 0;
        facing = facing.LengthSquared() > 1e-6f ? facing.Normalized() : Vector3.Forward;
        var feet = actor.GlobalPosition;
        var at = feet + facing * (actor.BodyRadiusM + HalfAlong(thing.Rotation, thing.Size, facing) + 0.01f);
        // From where it is carried (over the head); with something low overhead, from just above the feet.
        foreach (var height in new[] { actor.BodyHeightM + SandboxRules.CarryGapM, actor.StepHeightM })
        {
            var start = new Vector3(at.X, feet.Y + height, at.Z);
            if (start.Y + thing.Size.Y > _room.End.Y || !Free(thing, start, thing.Rotation, WorldMask | AvatarMask)) continue;
            return Finish(thing, start, thing.Rotation, null);
        }
        return (null, new Blocked("occupied", "There is no room to put it down here. Turn round or step back.", true));
    }

    /// <summary>A placement without a support: the thing starts a little above position_m and settles on whatever is below.</summary>
    public (Rest? At, Blocked? Blocked) PlaceAt(Thing thing, Vector3 position, Quaternion rotation)
    {
        var start = new Vector3(position.X, Mathf.Min(position.Y + ProbeM, _room.End.Y - thing.Size.Y - 0.001f), position.Z);
        if (!Free(thing, start, rotation, WorldMask | AvatarMask))
            return (null, new Blocked("occupied", "There is no room for it there.", true));
        return Finish(thing, start, rotation, null);
    }

    /// <summary>
    /// A placement on a support: snapped onto its walkable top at position_m's horizontal place, kept inside the top where
    /// it fits (centred on an axis where it does not), and refused when anything else is in the way. position_m's height
    /// picks a lower surface of the support when it has one there (a shelf); otherwise the thing goes on top.
    /// </summary>
    public (Rest? At, Blocked? Blocked) PlaceOn(Thing thing, Vector3 position, Quaternion rotation, string support, Aabb supportBounds)
    {
        var footprint = Bounds(Vector3.Zero, rotation, thing.Size);
        var x = Inside(position.X, supportBounds.Position.X - footprint.Position.X + 0.001f, supportBounds.End.X - footprint.End.X - 0.001f, supportBounds.GetCenter().X);
        var z = Inside(position.Z, supportBounds.Position.Z - footprint.Position.Z + 0.001f, supportBounds.End.Z - footprint.End.Z - 0.001f, supportBounds.GetCenter().Z);
        var heights = new List<float> { Mathf.Clamp(position.Y + ProbeM, supportBounds.Position.Y, supportBounds.End.Y) + 0.002f };
        if (supportBounds.End.Y + 0.002f - heights[0] > 0.0005f) heights.Add(supportBounds.End.Y + 0.002f);
        Blocked? blocked = null;
        foreach (var height in heights)
        {
            var start = new Vector3(x, height, z);
            if (start.Y + thing.Size.Y > _room.End.Y)
            {
                blocked ??= new Blocked("out_of_bounds", "It would not fit under the ceiling there.");
                continue;
            }
            if (!Free(thing, start, rotation, WorldMask | AvatarMask)) continue;
            var (rest, refused) = Finish(thing, start, rotation, support);
            if (rest != null) return (rest, null);
            blocked ??= refused;
        }
        return (null, blocked ?? new Blocked("occupied", "There is no room for it there.", true));
    }

    /// <summary>A push: slid along its support up to distance, with collisions; see the class summary.</summary>
    public (Rest? At, Blocked? Blocked) Push(Thing thing, Vector3 direction, float distance)
    {
        var dir = new Vector3(direction.X, 0, direction.Z);
        if (dir.LengthSquared() < 1e-8f) return (null, new Blocked("invalid_args", "That push has no direction."));
        dir = dir.Normalized();
        var pivot = thing.Position;
        var rotation = thing.Rotation;
        var stepUp = Mathf.Min(MaxStepUpM, thing.Size.Y * 0.25f);
        var tipLimit = new Vector2(thing.Size.X, thing.Size.Z).Length() * 0.5f;
        var travelled = 0f;
        var tipped = 0f;
        for (var guard = 0; guard < 400; guard++)
        {
            float want;
            var tipping = false;
            if (travelled < distance - 1e-5f) want = Mathf.Min(SlideStepM, distance - travelled);
            else if (tipped < tipLimit - 1e-5f && !CentreSupported(thing, pivot, rotation))
            {
                want = Mathf.Min(SlideStepM, tipLimit - tipped);
                tipping = true;
            }
            else break;
            var moved = Slide(thing, ref pivot, rotation, dir, want, stepUp);
            if (tipping) tipped += moved; else travelled += moved;
            var fell = !SettleAfterSlide(thing, ref pivot, rotation, stepUp);
            if (fell || moved < want - 1e-5f) break;
        }
        // Less than a millimetre along the floor is no push (a thing against a wall only settles a hair).
        if (new Vector2(pivot.X - thing.Position.X, pivot.Z - thing.Position.Z).Length() < MinPushM && pivot.Y >= thing.Position.Y - FallAfterM)
            return (null, new Blocked("occupied", "Something is in the way.", true));
        return Finish(thing, pivot, rotation, null, settled: true);
    }

    // ---- queries ----

    private PhysicsShapeQueryParameters3D Query(Thing thing, Vector3 pivot, Quaternion rotation, uint mask, Vector3 motion = default)
    {
        var size = new Vector3(Mathf.Max(0.001f, thing.Size.X - 2 * SideClearanceM), Mathf.Max(0.001f, thing.Size.Y), Mathf.Max(0.001f, thing.Size.Z - 2 * SideClearanceM));
        var basis = new Basis(rotation);
        return new PhysicsShapeQueryParameters3D
        {
            Shape = new BoxShape3D { Size = size, Margin = 0.001f },
            Transform = new Transform3D(basis, pivot + basis * new Vector3(0, thing.Size.Y * 0.5f, 0)),
            Motion = motion, CollisionMask = mask, CollideWithBodies = true, CollideWithAreas = false,
            Exclude = new Godot.Collections.Array<Rid> { thing.Body },
        };
    }

    public bool Free(Thing thing, Vector3 pivot, Quaternion rotation, uint mask) => _space.IntersectShape(Query(thing, pivot, rotation, mask), 1).Count == 0;

    private float Cast(Thing thing, Vector3 pivot, Quaternion rotation, Vector3 motion, uint mask)
    {
        if (motion.LengthSquared() < 1e-12f) return 1f;
        var fractions = _space.CastMotion(Query(thing, pivot, rotation, mask, motion));
        return fractions.Length > 0 ? Mathf.Clamp(fractions[0], 0f, 1f) : 0f;
    }

    /// <summary>One slide step a millimetre above the support, climbing a ledge when blocked. Returns the distance covered.</summary>
    private float Slide(Thing thing, ref Vector3 pivot, Quaternion rotation, Vector3 direction, float want, float stepUp)
    {
        var start = pivot + Vector3.Up * LiftM;
        var mask = WorldMask | AvatarMask;
        // A cast that starts against an obstacle may report it clear; a step that would end inside something is no step.
        var moved = want * Cast(thing, start, rotation, direction * want, mask);
        if (moved > 0 && !Free(thing, start + direction * moved, rotation, mask)) moved = 0;
        if (moved < want - 1e-5f && stepUp > 0)
        {
            var raised = start + Vector3.Up * stepUp * Cast(thing, start, rotation, Vector3.Up * stepUp, mask);
            var over = want * Cast(thing, raised, rotation, direction * want, mask);
            if (over > moved + 0.0005f && Free(thing, raised + direction * over, rotation, mask))
            {
                pivot = raised + direction * over;
                return over;
            }
        }
        pivot = start + direction * moved;
        return moved;
    }

    /// <summary>Lower the thing onto what is under it after a slide step. False when it went over an edge and fell.</summary>
    private bool SettleAfterSlide(Thing thing, ref Vector3 pivot, Quaternion rotation, float stepUp)
    {
        var near = LiftM + stepUp + FallAfterM;
        var fraction = Cast(thing, pivot, rotation, Vector3.Down * near, WorldMask);
        if (fraction < 1f)
        {
            pivot.Y -= near * fraction;
            return true;
        }
        var far = pivot.Y - _room.Position.Y + 0.5f;
        pivot.Y -= far * Cast(thing, pivot, rotation, Vector3.Down * far, WorldMask);
        return false;
    }

    private bool CentreSupported(Thing thing, Vector3 pivot, Quaternion rotation)
    {
        var ray = PhysicsRayQueryParameters3D.Create(pivot + Vector3.Up * 0.002f, pivot + Vector3.Down * 0.01f, WorldMask, new Godot.Collections.Array<Rid> { thing.Body });
        return _space.IntersectRay(ray).Count > 0;
    }

    /// <summary>
    /// Settle from start (unless already settled), snap the height to the support, then check the room and the avatars.
    /// With mustRestOn, a thing that comes to rest on anything else is refused.
    /// </summary>
    private (Rest? At, Blocked? Blocked) Finish(Thing thing, Vector3 start, Quaternion rotation, string? mustRestOn, bool settled = false)
    {
        var pivot = start;
        if (!settled)
        {
            var drop = pivot.Y - _room.Position.Y + 0.5f;
            var fraction = Cast(thing, pivot, rotation, Vector3.Down * drop, WorldMask);
            if (fraction >= 1f) return (null, new Blocked("out_of_bounds", "There is nothing there to put it on."));
            pivot.Y -= drop * fraction;
        }
        var support = "";
        if (Snap(thing, pivot, rotation) is { } snapped)
        {
            pivot.Y = snapped.Height;
            support = snapped.Support;
        }
        else support = Contact(thing, pivot, rotation);
        if (mustRestOn != null && support != mustRestOn) return (null, new Blocked("occupied", "There is no room for it there.", true));
        pivot = new Vector3(Mathf.Snapped(pivot.X, PoseGridM), Mathf.Snapped(pivot.Y, PoseGridM), Mathf.Snapped(pivot.Z, PoseGridM));
        var rounded = new Quaternion(Mathf.Snapped(rotation.X, 1e-6f), Mathf.Snapped(rotation.Y, 1e-6f), Mathf.Snapped(rotation.Z, 1e-6f), Mathf.Snapped(rotation.W, 1e-6f)).Normalized();
        var bounds = Bounds(pivot, rounded, thing.Size);
        if (!_room.Grow(0.001f).Encloses(bounds)) return (null, new Blocked("out_of_bounds", "It would not fit inside the room there."));
        if (!Free(thing, pivot + Vector3.Up * 0.0005f, rounded, AvatarMask)) return (null, new Blocked("occupied", "An avatar is in the way.", true));
        return (new Rest(pivot, rounded, bounds, support), null);
    }

    /// <summary>The highest upward-facing surface under the footprint within a few millimetres of its bottom, and whose it is.</summary>
    private (float Height, string Support)? Snap(Thing thing, Vector3 pivot, Quaternion rotation)
    {
        var basis = new Basis(rotation);
        var hx = Mathf.Max(0, thing.Size.X * 0.5f - 0.001f);
        var hz = Mathf.Max(0, thing.Size.Z * 0.5f - 0.001f);
        (float Height, string Support)? best = null;
        foreach (var (sx, sz) in new[] { (0f, 0f), (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
        {
            var offset = basis * new Vector3(sx * hx, 0, sz * hz);
            var at = new Vector3(pivot.X + offset.X, pivot.Y, pivot.Z + offset.Z);
            var ray = PhysicsRayQueryParameters3D.Create(at + Vector3.Up * SnapWindowM, at + Vector3.Down * SnapWindowM, WorldMask, new Godot.Collections.Array<Rid> { thing.Body });
            var hit = _space.IntersectRay(ray);
            if (hit.Count == 0 || hit["normal"].AsVector3().Y < 0.7f) continue;
            var height = hit["position"].AsVector3().Y;
            if (best == null || height > best.Value.Height) best = (height, EntityOf(hit["collider"].AsGodotObject()));
        }
        return best;
    }

    /// <summary>What the thing touches just below its bottom, when no ray found its support (resting on an edge).</summary>
    private string Contact(Thing thing, Vector3 pivot, Quaternion rotation)
    {
        var info = _space.GetRestInfo(Query(thing, pivot + Vector3.Down * 0.0015f, rotation, WorldMask));
        return info.Count > 0 ? EntityOf(GodotObject.InstanceFromId(info["collider_id"].AsUInt64())) : "";
    }

    /// <summary>The entity id a collider belongs to: the nearest node at or above it carrying entity_id metadata.</summary>
    public static string EntityOf(GodotObject? collider)
    {
        for (var node = collider as Node; node != null; node = node.GetParent())
            if (node.HasMeta("entity_id")) return node.GetMeta("entity_id").AsString();
        return "";
    }

    private static float Inside(float value, float low, float high, float centre) => low <= high ? Mathf.Clamp(value, low, high) : centre;
}

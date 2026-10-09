using Godot;
using System;
using System.Collections.Generic;
using EnFractal.Native.Room;

namespace EnFractal.Native.Look.Fauna;

/// <summary>
/// Fish in the sea's deeper water near the swimmer (Run 2, the sea's look). A few small schools, a fixed number of fish (so the
/// cost is the same anywhere at sea), kept within a few metres of the swimmer: a school that falls too far behind is moved, out of
/// sight in the haze, to open water ahead. Schools live only where the open sea is deep (outside the playable water, at least
/// MinDepthM over RoomSea.OpenSeaBedAt), mid-water between the bed and the surface. Fish dart from the player who comes close and
/// never from the Gubble (the founder, 8 October: a ghost only the player can see). One MultiMesh, one draw; nothing here is an
/// entity, collides, or is saved.
/// </summary>
public partial class SeaLife : Node3D
{
    public const int SchoolCount = 3;
    public const int PerSchool = 7;
    /// <summary>How far from the swimmer a school is placed, metres (beyond what the water lets a diver see clearly).</summary>
    public const float PlaceNearM = 1.6f, PlaceFarM = 3.0f;
    /// <summary>A school further than this from the swimmer is moved ahead.</summary>
    public const float KeepWithinM = 5.0f;
    /// <summary>The open sea must be at least this deep where a school lives.</summary>
    public const float MinDepthM = 0.4f;
    public const float CruiseMps = 0.05f, DartMps = 0.32f, FleeM = 0.3f;

    public sealed class SeaFish
    {
        public Vector3 Position, Velocity, Offset;
        public float Phase, LengthM, Seed;
        public int School, Variety;
    }

    private RoomSea _sea = null!;
    private Func<Node3D?> _swimmer = () => null;
    private readonly RandomNumberGenerator _random = new();
    private readonly Vector3?[] _anchors = new Vector3?[SchoolCount];
    private readonly List<SeaFish> _fish = new();
    private float[] _buffer = Array.Empty<float>();

    public IReadOnlyList<SeaFish> Fish => _fish;
    public IReadOnlyList<Vector3?> Anchors => _anchors;
    public MultiMeshInstance3D School { get; private set; } = null!;

    /// <summary>Sea fish for a room's sea, following the swimmer the function names (the player; never the Gubble).</summary>
    public static SeaLife Create(RoomSea sea, Func<Node3D?> swimmer, ulong seed = 20261009)
    {
        var life = new SeaLife { Name = "SeaLife", _sea = sea, _swimmer = swimmer };
        life._random.Seed = seed;
        life.Build();
        return life;
    }

    private void Build()
    {
        for (var school = 0; school < SchoolCount; school++)
            for (var k = 0; k < PerSchool; k++)
                _fish.Add(new SeaFish
                {
                    School = school, Variety = school == 2 && k < 3 ? 1 : 0, LengthM = _random.RandfRange(0.024f, 0.034f),
                    Phase = _random.Randf() * Mathf.Tau, Seed = _random.Randf(),
                    Offset = new Vector3(_random.RandfRange(-0.18f, 0.18f), _random.RandfRange(-0.06f, 0.06f), _random.RandfRange(-0.18f, 0.18f)),
                    Position = new Vector3(0f, -1000f, 0f),
                });
        var material = new ShaderMaterial { Shader = GD.Load<Shader>(PondLife.ShaderPath), ResourceName = "painterly sea fish" };
        // Sea fish: silver-blue backs, and a few yellow ones.
        material.SetShaderParameter("minnow_color", new Color(0.46f, 0.58f, 0.66f));
        material.SetShaderParameter("minnow_fin", new Color(0.62f, 0.70f, 0.74f));
        material.SetShaderParameter("golden_color", new Color(0.95f, 0.78f, 0.28f));
        var multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = FishMesh.Get() };
        multimesh.InstanceCount = _fish.Count;
        // The schools go wherever the swimmer goes: never culled by where the node stands.
        var everywhere = new Aabb(new Vector3(-4000f, -50f, -4000f), new Vector3(8000f, 100f, 8000f));
        multimesh.CustomAabb = everywhere;
        _buffer = new float[_fish.Count * 16];
        School = new MultiMeshInstance3D { Name = "SeaFish", Multimesh = multimesh, MaterialOverride = material, CustomAabb = everywhere, GIMode = GeometryInstance3D.GIModeEnum.Disabled };
        School.SetMeta(LookDirector.DressedMeta, true);
        AddChild(School);
        WriteBuffer();
        multimesh.Buffer = _buffer;
    }

    /// <summary>Whether a school can live at a point: the open sea there, outside the playable water, deep enough.</summary>
    public bool Habitable(Vector2 point) => !_sea.InPlayArea(point) && !RoomSea.Inside(_sea.Coast, point) && _sea.LevelM - _sea.OpenSeaBedAt(point) >= MinDepthM;

    /// <summary>A school's place near a swimmer: open water PlaceNearM to PlaceFarM off, ahead if it can, mid-water; null if none.</summary>
    public Vector3? PlaceNear(Vector3 swimmer, Vector3 ahead)
    {
        var heading = new Vector2(ahead.X, ahead.Z);
        var start = heading.LengthSquared() > 1e-6f ? heading.Angle() : _random.Randf() * Mathf.Tau;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var angle = start + (attempt % 2 == 0 ? 1f : -1f) * (attempt / 2) * 0.5f + _random.RandfRange(-0.4f, 0.4f);
            var flat = new Vector2(swimmer.X, swimmer.Z) + Vector2.FromAngle(angle) * _random.RandfRange(PlaceNearM, PlaceFarM);
            if (!Habitable(flat)) continue;
            var bed = _sea.OpenSeaBedAt(flat);
            return new Vector3(flat.X, Mathf.Lerp(bed + 0.12f, _sea.LevelM - 0.12f, _random.RandfRange(0.35f, 0.7f)), flat.Y);
        }
        return null;
    }

    public override void _Process(double delta) => Advance((float)delta);

    /// <summary>Move every fish by dt seconds: schools kept near the swimmer, each fish wandering round its school and darting from the swimmer.</summary>
    public void Advance(float dt)
    {
        var swimmer = _swimmer();
        if (swimmer == null || !IsInstanceValid(swimmer) || !swimmer.IsInsideTree() || dt <= 0f) return;
        var at = swimmer.GlobalPosition;
        var ahead = -swimmer.GlobalBasis.Z;
        for (var school = 0; school < SchoolCount; school++)
            if (_anchors[school] is not { } anchor || new Vector2(anchor.X - at.X, anchor.Z - at.Z).Length() > KeepWithinM)
            {
                _anchors[school] = PlaceNear(at, ahead);
                if (_anchors[school] is { } placed)
                    foreach (var fish in _fish)
                        if (fish.School == school) { fish.Position = placed + fish.Offset; fish.Velocity = Vector3.Zero; }
            }
        foreach (var fish in _fish)
        {
            if (_anchors[fish.School] is not { } home) { fish.Position = new Vector3(at.X, -1000f, at.Z); continue; }
            // Wander slowly round the school's place, each fish on its own orbit.
            var orbit = new Vector3(Mathf.Cos(fish.Phase * 0.21f), 0f, Mathf.Sin(fish.Phase * 0.21f)) * 0.12f;
            var want = (home + fish.Offset + orbit - fish.Position).LimitLength(1f) * CruiseMps * 4f;
            var away = fish.Position - at;
            if (away.Length() < FleeM) want = away.Normalized() * DartMps;
            fish.Velocity = fish.Velocity.Lerp(want.LimitLength(DartMps), Mathf.Clamp(dt * 3f, 0f, 1f));
            fish.Position += fish.Velocity * dt;
            var flat = new Vector2(fish.Position.X, fish.Position.Z);
            fish.Position.Y = Mathf.Clamp(fish.Position.Y, _sea.OpenSeaBedAt(flat) + 0.03f, _sea.LevelM - 0.03f);
            fish.Phase += dt * (4f + 10f * fish.Velocity.Length() / DartMps);
        }
        WriteBuffer();
        RenderingServer.MultimeshSetBuffer(School.Multimesh.GetRid(), _buffer);
    }

    private void WriteBuffer()
    {
        for (var i = 0; i < _fish.Count; i++)
        {
            var fish = _fish[i];
            var heading = new Vector3(fish.Velocity.X, fish.Velocity.Y * 0.3f, fish.Velocity.Z);
            var forward = heading.LengthSquared() > 1e-8f ? heading.Normalized() : Vector3.Forward;
            var basis = Basis.LookingAt(forward, Vector3.Up).Scaled(Vector3.One * fish.LengthM);
            var o = i * 16;
            _buffer[o + 0] = basis.Column0.X; _buffer[o + 1] = basis.Column1.X; _buffer[o + 2] = basis.Column2.X; _buffer[o + 3] = fish.Position.X;
            _buffer[o + 4] = basis.Column0.Y; _buffer[o + 5] = basis.Column1.Y; _buffer[o + 6] = basis.Column2.Y; _buffer[o + 7] = fish.Position.Y;
            _buffer[o + 8] = basis.Column0.Z; _buffer[o + 9] = basis.Column1.Z; _buffer[o + 10] = basis.Column2.Z; _buffer[o + 11] = fish.Position.Z;
            _buffer[o + 12] = fish.Phase;
            _buffer[o + 13] = 0.35f + 0.65f * Mathf.Clamp(fish.Velocity.Length() / DartMps, 0f, 1f);
            _buffer[o + 14] = fish.Variety;
            _buffer[o + 15] = fish.Seed;
        }
    }
}

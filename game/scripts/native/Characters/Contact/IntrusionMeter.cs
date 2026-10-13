using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Godot;

namespace EnFractal.Native.Characters.Contact;

/// <summary>
/// The intrusion metric (docs/runs/RUN-2-CHARACTERS.md, "Later: wide poses meet the world", item 1): a measurement only. Each
/// sample asks the physics space how deep each part's mesh bounds (a box riding the part's final bone) reach into world
/// geometry, and adds depth × time, in millimetre-seconds, to the part's total. Only the shared capsule collides, so wide parts
/// pass into walls, gaps and posts; this baseline is what the later round's tucks, braces and turns must lower. It changes
/// nothing about where a body can go. Allocates per sample (a test and tuning tool, not a frame-budget path).
/// </summary>
public sealed class IntrusionMeter
{
    /// <summary>The thinnest a part's box is made (flat painted parts have almost no thickness).</summary>
    public const float MinBoxM = 0.0005f;

    private readonly CharacterBody _rig;
    private readonly int[] _parts;
    private readonly double[] _mmS;
    private readonly BoxShape3D _box = new();
    private readonly PhysicsShapeQueryParameters3D _query;

    /// <param name="exclude">Colliders not to count (the body's own capsule, the floor it walks on).</param>
    public IntrusionMeter(CharacterBody rig, uint mask, IEnumerable<Rid> exclude)
    {
        _rig = rig ?? throw new ArgumentNullException(nameof(rig));
        _parts = rig.Bones.Where(b => b.HasMesh).Select(b => b.Index).ToArray();
        _mmS = new double[_parts.Length];
        _query = new PhysicsShapeQueryParameters3D
        {
            Shape = _box, CollisionMask = mask, CollideWithAreas = false, CollideWithBodies = true, Margin = 0,
            Exclude = new Godot.Collections.Array<Rid>(exclude),
        };
    }

    /// <summary>Millimetre-seconds inside the world so far, over every part.</summary>
    public double TotalMmS => _mmS.Sum();
    public double SampledS { get; private set; }
    public int Samples { get; private set; }
    /// <summary>The deepest any part reached on any sample, metres.</summary>
    public float WorstDepthM { get; private set; }

    /// <summary>Each part with its millimetre-seconds, the most first.</summary>
    public IEnumerable<(string Part, double MmS)> Parts =>
        _parts.Select((bone, i) => (_rig.Bones[bone].Name, _mmS[i])).OrderByDescending(p => p.Item2).ThenBy(p => p.Name, StringComparer.Ordinal);

    public void Reset()
    {
        Array.Clear(_mmS);
        SampledS = 0;
        Samples = 0;
        WorstDepthM = 0;
    }

    /// <summary>One sample over <paramref name="dt"/> seconds, from the bones' final transforms (as of the last skeleton update).</summary>
    public void Sample(PhysicsDirectSpaceState3D space, float dt)
    {
        var body = _rig.GlobalTransform;
        for (var i = 0; i < _parts.Length; i++)
        {
            var bone = _rig.Bones[_parts[i]];
            var depth = Depth(space, body * _rig.FinalBoneTransform(bone.Index), bone.MeshBounds);
            if (depth <= 0) continue;
            _mmS[i] += depth * 1000.0 * dt;
            WorstDepthM = Mathf.Max(WorstDepthM, depth);
        }
        SampledS += dt;
        Samples++;
    }

    /// <summary>How deep a box (the bounds under <paramref name="transform"/>) reaches into the world: the deepest contact pair's separation.</summary>
    public float Depth(PhysicsDirectSpaceState3D space, Transform3D transform, Aabb bounds)
    {
        var scale = transform.Basis.Scale;
        var size = new Vector3(Mathf.Max(bounds.Size.X * Mathf.Abs(scale.X), MinBoxM), Mathf.Max(bounds.Size.Y * Mathf.Abs(scale.Y), MinBoxM),
            Mathf.Max(bounds.Size.Z * Mathf.Abs(scale.Z), MinBoxM));
        if (!float.IsFinite(size.X + size.Y + size.Z)) return 0;
        _box.Size = size;
        _query.Transform = new Transform3D(transform.Basis.Orthonormalized(), transform * bounds.GetCenter());
        var pairs = space.CollideShape(_query, 16);
        var deepest = 0f;
        for (var p = 0; p + 1 < pairs.Count; p += 2) deepest = Mathf.Max(deepest, pairs[p].DistanceTo(pairs[p + 1]));
        return deepest;
    }

    /// <summary>One line for the run's evidence: the total, the time measured and the parts that intrude most.</summary>
    public string Describe(int top = 4)
    {
        var c = CultureInfo.InvariantCulture;
        var text = new StringBuilder();
        text.Append(c, $"total {TotalMmS:0.0} mm·s over {SampledS:0.00} s (worst depth {WorstDepthM * 1000:0.0} mm)");
        var worst = Parts.Where(p => p.MmS > 0.05).Take(top).ToList();
        text.Append(worst.Count == 0 ? "; no part inside" : "; most: " + string.Join(", ", worst.Select(p => string.Create(c, $"{p.Part} {p.MmS:0.0}"))));
        return text.ToString();
    }
}

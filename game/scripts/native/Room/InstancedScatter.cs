using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using EnFractal.Native.Look;

namespace EnFractal.Native.Room;

/// <summary>
/// SCALE EXPERIMENT (run2/scale-experiment; not for merging as is). A room magnified x W around today's 10 cm body keeps today's
/// planting density, so it holds about W^2 times as many grass tufts, flowers, ferns and shrubs: merged into one mesh as today
/// they would be hundreds of megabytes. Here each plant prototype is one mesh drawn many times (MultiMesh), in square tiles so
/// the camera's frustum culls whole tiles, and each tile stops drawing past DrawRangeM (a 3 cm tuft there is under a pixel).
/// The prototype is first dressed by the look like any shell mesh (a hidden template under this node); its painterly
/// materials then go onto the instanced copies, with each placement's tint as the instance colour.
/// Visual only: these plants never collide (the export only instances plants without collision).
/// </summary>
public partial class InstancedScatter : Node3D
{
    public const float TileM = 8f;
    public const float DrawRangeM = 60f;
    private InstanceSet _set = null!;
    private MeshInstance3D? _template;
    private int _waited;
    public int Tiles { get; private set; }
    public bool Built { get; private set; }

    public static InstancedScatter Create(InstanceSet set, Node3D prototypeScene)
    {
        var node = new InstancedScatter { Name = "Instanced_" + set.Prototype, _set = set };
        node.SetMeta("entity_id", "shell:instanced_" + set.Prototype);
        node.SetMeta("surface_role", "backdrop");
        node.SetMeta("material_role", "foliage");
        node.SetMeta("drawn", true);
        prototypeScene.Name = "Template";
        prototypeScene.Visible = false;
        node.AddChild(prototypeScene);
        node._template = FindMesh(prototypeScene);
        return node;
    }

    private static MeshInstance3D? FindMesh(Node node)
    {
        if (node is MeshInstance3D { Mesh: not null } mesh) return mesh;
        foreach (var child in node.GetChildren())
            if (FindMesh(child) is { } found) return found;
        return null;
    }

    public override void _Process(double delta)
    {
        if (Built || _template == null) return;
        // Wait for the look to dress the template (a few frames); a room without a look keeps the file's own materials.
        if (!_template.HasMeta(LookDirector.DressedMeta) && ++_waited < 30) return;
        Build();
    }

    private void Build()
    {
        Built = true;
        var mesh = (Mesh)_template!.Mesh.Duplicate();
        if (mesh is ArrayMesh arrays)
            for (var surface = 0; surface < arrays.GetSurfaceCount(); surface++)
                arrays.SurfaceSetMaterial(surface, _template.GetActiveMaterial(surface));
        var tiles = new Dictionary<(int, int), List<int>>();
        var rows = _set.Rows;
        for (var i = 0; i < _set.Count; i++)
        {
            var key = ((int)Mathf.Floor(rows[i * InstanceSet.Floats] / TileM), (int)Mathf.Floor(rows[i * InstanceSet.Floats + 2] / TileM));
            if (!tiles.TryGetValue(key, out var list)) tiles[key] = list = new List<int>();
            list.Add(i);
        }
        foreach (var ((tx, tz), members) in tiles.OrderBy(t => t.Key))
        {
            var multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh };
            multimesh.InstanceCount = members.Count;
            for (var k = 0; k < members.Count; k++)
            {
                var o = members[k] * InstanceSet.Floats;
                var basis = new Basis(Vector3.Up, Mathf.DegToRad(rows[o + 3])) * Basis.FromScale(new Vector3(rows[o + 4], rows[o + 5], rows[o + 6]));
                multimesh.SetInstanceTransform(k, new Transform3D(basis, new Vector3(rows[o], rows[o + 1], rows[o + 2])));
                multimesh.SetInstanceColor(k, new Color(rows[o + 7], rows[o + 8], rows[o + 9]));
            }
            AddChild(new MultiMeshInstance3D
            {
                Name = $"Tile_{tx}_{tz}", Multimesh = multimesh, CastShadow = _template.CastShadow,
                VisibilityRangeEnd = DrawRangeM, VisibilityRangeEndMargin = TileM,
            });
        }
        Tiles = tiles.Count;
        GD.Print($"SCALE_INSTANCES {_set.Prototype}: {_set.Count} instances in {Tiles} tiles of {TileM:0} m, drawn to {DrawRangeM:0} m");
    }
}

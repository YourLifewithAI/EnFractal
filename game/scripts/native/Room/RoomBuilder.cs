using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using EnFractal.Native.Look;
using FileAccess = Godot.FileAccess;

namespace EnFractal.Native.Room;

/// <summary>
/// Builds scene nodes from RoomData. The scene is derived: nothing here is authoritative state.
/// Shell polygons become extruded slabs; objects become StaticBody3D nodes for now (the Play
/// track decides which become rigid bodies). Every node carries its entity id, roles and
/// affordances as metadata so look, play and AI systems can find what they need.
/// </summary>
public static class RoomBuilder
{
    public const uint WorldLayer = 1;

    public static Node3D Build(RoomData room)
    {
        var root = new Node3D { Name = "Room" };
        root.SetMeta("room_id", room.RoomId);
        root.SetMeta("room_manifest_sha256", room.ManifestSha256);
        var shell = new Node3D { Name = "Shell" };
        var objects = new Node3D { Name = "Objects" };
        root.AddChild(shell);
        root.AddChild(objects);
        foreach (var part in room.Shell) shell.AddChild(BuildShellPart(room, part));
        foreach (var instance in room.Objects) objects.AddChild(BuildObject(instance));
        return root;
    }

    public static string NodeName(string entityId) =>
        string.Concat(entityId[(entityId.IndexOf(':') + 1)..].Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '_'));

    private static Node3D BuildShellPart(RoomData room, ShellPart part)
    {
        Node3D node = part.Collides ? new StaticBody3D { CollisionLayer = WorldLayer, CollisionMask = 0 } : new Node3D();
        node.Name = NodeName(part.Id);
        node.SetMeta("entity_id", part.Id);
        node.SetMeta("surface_role", part.Role);
        node.SetMeta("material_role", part.MaterialRole);
        if (part.MeshPath != null)
        {
            var scene = LoadGlb(room.Directory + "/" + part.MeshPath, part.Id);
            node.AddChild(scene);
            if (part.Collides)
                foreach (var mesh in Meshes(scene))
                    node.AddChild(new CollisionShape3D { Shape = mesh.Mesh.CreateTrimeshShape(), Transform = RelativeTransform(scene, mesh) });
            return node;
        }
        var (arrayMesh, shape) = ExtrudePolygon(part.Points, part.ThicknessM);
        node.AddChild(new MeshInstance3D { Name = "Visual", Mesh = arrayMesh, MaterialOverride = MaterialLibrary.For(part.MaterialRole, part.BaseColor) });
        if (part.Collides) node.AddChild(new CollisionShape3D { Name = "Collision", Shape = shape });
        return node;
    }

    private static Node3D BuildObject(ObjectInstance instance)
    {
        var asset = instance.Asset;
        Node3D body = asset.CollisionKind == "none" ? new Node3D() : new StaticBody3D { CollisionLayer = WorldLayer, CollisionMask = 0 };
        body.Name = NodeName(instance.Id);
        body.Transform = new Transform3D(new Basis(instance.Rotation).Scaled(Vector3.One * instance.Scale), instance.PositionM);
        body.SetMeta("entity_id", instance.Id);
        body.SetMeta("asset_id", asset.AssetId);
        body.SetMeta("display_name", instance.DisplayName ?? asset.DisplayName);
        body.SetMeta("category", asset.Category);
        body.SetMeta("category_group", asset.CategoryGroup);
        body.SetMeta("tier", asset.Tier);
        body.SetMeta("provenance_kind", asset.ProvenanceKind);
        body.SetMeta("movable", asset.Movable);
        body.SetMeta("mass_kg", asset.MassKg);
        body.SetMeta("affordances", asset.Affordances);
        body.SetMeta("review_status", asset.ReviewStatus);
        body.SetMeta("support_kind", instance.SupportKind);
        if (instance.SupportTarget != null) body.SetMeta("support_target", instance.SupportTarget);
        var roles = new Godot.Collections.Dictionary();
        foreach (var slot in asset.Materials) roles[slot.Slot] = slot.Role;
        body.SetMeta("material_roles", roles);

        var size = asset.DimensionsM;
        if (asset.GeometryKind == "primitive")
        {
            var slot = asset.Materials[0];
            var (mesh, shape, centre) = Primitive(asset.Primitive!, size);
            body.AddChild(new MeshInstance3D { Name = "Visual", Mesh = mesh, Position = centre, MaterialOverride = MaterialLibrary.For(slot.Role, slot.BaseColor ?? new Color("b3aea4")) });
            if (asset.CollisionKind == "primitive") body.AddChild(new CollisionShape3D { Name = "Collision", Shape = shape, Position = centre });
            return body;
        }
        var visual = LoadGlb(asset.Directory + "/" + asset.MeshPath, instance.Id);
        visual.Name = "Visual";
        body.AddChild(visual);
        switch (asset.CollisionKind)
        {
            case "box":
                body.AddChild(new CollisionShape3D { Name = "Collision", Shape = new BoxShape3D { Size = size }, Position = Vector3.Up * size.Y * 0.5f });
                break;
            case "convex_hull":
            case "convex_decomposition":
                // A single hull until the capture pipeline ships decompositions; recorded so nobody mistakes it for the final shape.
                var points = Meshes(visual).SelectMany(m => m.Mesh.GetFaces().Select(v => RelativeTransform(visual, m) * v)).ToArray();
                body.AddChild(new CollisionShape3D { Name = "Collision", Shape = new ConvexPolygonShape3D { Points = points } });
                if (asset.CollisionKind == "convex_decomposition") body.SetMeta("collision_approximation", "single_convex_hull");
                break;
            case "trimesh_static":
                foreach (var mesh in Meshes(visual))
                    body.AddChild(new CollisionShape3D { Shape = mesh.Mesh.CreateTrimeshShape(), Transform = RelativeTransform(visual, mesh) });
                break;
        }
        return body;
    }

    private static (Mesh Mesh, Shape3D Shape, Vector3 Centre) Primitive(string kind, Vector3 size)
    {
        var radius = Mathf.Min(size.X, size.Z) * 0.5f;
        return kind switch
        {
            "box" => (new BoxMesh { Size = size }, new BoxShape3D { Size = size }, Vector3.Up * size.Y * 0.5f),
            "cylinder" => (new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = size.Y }, new CylinderShape3D { Radius = radius, Height = size.Y }, Vector3.Up * size.Y * 0.5f),
            "sphere" => (new SphereMesh { Radius = radius, Height = radius * 2 }, new SphereShape3D { Radius = radius }, Vector3.Up * radius),
            "capsule" => (new CapsuleMesh { Radius = radius, Height = Mathf.Max(size.Y, radius * 2) }, new CapsuleShape3D { Radius = radius, Height = Mathf.Max(size.Y, radius * 2) }, Vector3.Up * Mathf.Max(size.Y, radius * 2) * 0.5f),
            _ => throw new RoomLoadException($"unsupported primitive '{kind}'"),
        };
    }

    /// <summary>Extrude a planar polygon away from the room. Inner face follows the polygon winding (normal into the room).</summary>
    public static (ArrayMesh Mesh, Shape3D Shape) ExtrudePolygon(Vector3[] points, float thickness)
    {
        var normal = NewellNormal(points);
        var u = (points[1] - points[0]).Normalized();
        var v = normal.Cross(u).Normalized();
        var flat = points.Select(p => new Vector2((p - points[0]).Dot(u), (p - points[0]).Dot(v))).ToArray();
        var triangles = Geometry2D.TriangulatePolygon(flat);
        if (triangles.Length == 0) throw new RoomLoadException("shell polygon cannot be triangulated");
        var back = -normal * thickness;
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        for (var i = 0; i < triangles.Length; i += 3)
        {
            var a = points[triangles[i]]; var b = points[triangles[i + 1]]; var c = points[triangles[i + 2]];
            Triangle(tool, a, b, c, normal);
            Triangle(tool, a + back, b + back, c + back, -normal);
        }
        for (var i = 0; i < points.Length; i++)
        {
            var a = points[i]; var b = points[(i + 1) % points.Length];
            var outward = (b - a).Cross(normal).Normalized();
            Triangle(tool, a, b, b + back, outward);
            Triangle(tool, a, b + back, a + back, outward);
        }
        var mesh = tool.Commit();
        Shape3D shape = IsConvex(flat)
            ? new ConvexPolygonShape3D { Points = points.Concat(points.Select(p => p + back)).ToArray() }
            : mesh.CreateTrimeshShape();
        return (mesh, shape);
    }

    /// <summary>Godot treats clockwise triangles (seen from the front) as front faces.</summary>
    private static void Triangle(SurfaceTool tool, Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
    {
        if ((b - a).Cross(c - a).Dot(normal) > 0) (b, c) = (c, b);
        foreach (var vertex in new[] { a, b, c })
        {
            tool.SetNormal(normal);
            tool.AddVertex(vertex);
        }
    }

    public static Vector3 NewellNormal(IReadOnlyList<Vector3> points)
    {
        var n = Vector3.Zero;
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i]; var q = points[(i + 1) % points.Count];
            n += new Vector3((p.Y - q.Y) * (p.Z + q.Z), (p.Z - q.Z) * (p.X + q.X), (p.X - q.X) * (p.Y + q.Y));
        }
        if (n.LengthSquared() < 1e-12f) throw new RoomLoadException("shell polygon is degenerate");
        return n.Normalized();
    }

    private static bool IsConvex(Vector2[] polygon)
    {
        var sign = 0;
        for (var i = 0; i < polygon.Length; i++)
        {
            var a = polygon[i]; var b = polygon[(i + 1) % polygon.Length]; var c = polygon[(i + 2) % polygon.Length];
            var cross = (b - a).Cross(c - b);
            if (Mathf.Abs(cross) < 1e-9f) continue;
            var current = cross > 0 ? 1 : -1;
            if (sign != 0 && current != sign) return false;
            sign = current;
        }
        return true;
    }

    private static Node3D LoadGlb(string path, string label)
    {
        if (!FileAccess.FileExists(path)) throw new RoomLoadException($"{label}: mesh {path} not found");
        var document = new GltfDocument();
        var state = new GltfState();
        var error = document.AppendFromBuffer(FileAccess.GetFileAsBytes(path), path.GetBaseDir(), state);
        if (error != Error.Ok) throw new RoomLoadException($"{label}: mesh {path} could not be read ({error})");
        return document.GenerateScene(state) as Node3D ?? throw new RoomLoadException($"{label}: mesh {path} has no 3D scene");
    }

    private static IEnumerable<MeshInstance3D> Meshes(Node node)
    {
        if (node is MeshInstance3D { Mesh: not null } mesh) yield return mesh;
        foreach (var child in node.GetChildren())
            foreach (var nested in Meshes(child)) yield return nested;
    }

    /// <summary>Transform of a descendant relative to an ancestor, valid before the nodes enter the tree.</summary>
    private static Transform3D RelativeTransform(Node3D ancestor, Node3D descendant)
    {
        var result = Transform3D.Identity;
        for (Node? node = descendant; node != null && node != ancestor.GetParent(); node = node.GetParent())
            if (node is Node3D spatial) result = spatial.Transform * result;
        return result;
    }
}

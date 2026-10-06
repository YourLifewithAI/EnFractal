using Godot;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EnFractal.Native;
using EnFractal.Native.Look;
using EnFractal.Native.Room;
using FileAccess = Godot.FileAccess;

namespace EnFractal.Tests;

/// <summary>Room contract consumption: loading, integrity, derived scene, collision, look and a full room boot.</summary>
public partial class RoomDataTest : Node3D
{
    private int _checks;
    private int _failures;

    public override async void _Ready()
    {
        try
        {
            var room = RoomData.Load(RoomWorld.DefaultRoom);
            Check(room.RoomId == "test_room" && room.SourceKind == "hand_built", "test room loads with its identity");
            Check(room.Shell.Count == 6 && room.Objects.Count == 5 && room.Spawns.Count == 2, "test room has 6 shell parts, 5 objects and 2 spawns");
            Check(room.ManifestSha256 == FileAccess.GetSha256(RoomWorld.DefaultRoom + "/room.json"), "manifest pin is the SHA-256 of the file bytes");
            Check(room.SpawnFor("player").Id == "player_start" && room.SpawnFor("companion", room.SpawnFor("player")).Id == "companion_start", "spawns resolve by role");
            Check(room.Bounds.Size.IsEqualApprox(new Vector3(4, 2.4f, 3)), "bounds match the 4 x 2.4 x 3 m interior");

            var built = RoomBuilder.Build(room);
            AddChild(built);
            var shell = built.GetNode("Shell").GetChildren().OfType<Node3D>().ToArray();
            var objects = built.GetNode("Objects").GetChildren().OfType<Node3D>().ToArray();
            Check(shell.Length == 6 && shell.All(n => n.HasMeta("entity_id") && n.GetChildren().OfType<CollisionShape3D>().Any()), "every shell part is a collidable body with its entity id");
            Check(objects.Length == 5 && objects.All(n => n.GetMeta("entity_id").AsString().StartsWith("obj:") && n.HasMeta("material_roles") && n.HasMeta("affordances")), "every object carries entity id, material roles and affordances");
            Check(objects.All(n => n.GetMeta("movable").AsBool() && n.GetMeta("mass_kg").AsSingle() > 0), "object physics metadata survives the build");
            var floorMesh = (ArrayMesh)shell.First(n => n.GetMeta("surface_role").AsString() == "floor").GetNode<MeshInstance3D>("Visual").Mesh;
            Check(UpFacingTrianglesAreClockwise(floorMesh), "floor's upward faces wind clockwise, which Godot renders as front faces");
            await Frames(3);
            var floorHit = Ray(new Vector3(-1.5f, 1, 1.2f), new Vector3(-1.5f, -1, 1.2f));
            Check(Role(floorHit) == "floor" && Mathf.Abs(floorHit["position"].AsVector3().Y) < 0.002f, "a downward ray lands on the floor's top face at y = 0");
            var tableHit = Ray(new Vector3(-0.9f, 2, -0.9f), new Vector3(-0.9f, 0.5f, -0.9f));
            Check(Entity(tableHit) == "obj:table" && Mathf.Abs(tableHit["position"].AsVector3().Y - 0.75f) < 0.002f, "the table proxy collides at its 0.75 m top");
            var wallHit = Ray(new Vector3(0, 1, 0), new Vector3(0, 1, -3));
            Check(Entity(wallHit) == "shell:wall_north" && Mathf.Abs(wallHit["position"].AsVector3().Z + 1.5f) < 0.002f, "the north wall collides on its inner face at z = -1.5");
            built.QueueFree();

            CheckRejected(() => LoadCopy("box_proxy", b => Replace(b, "\"mass_kg\": 1.5", "\"mass_kg\": 9.5")), "hash does not match", "a same-size tampered asset fails the hash check");
            CheckRejected(() => LoadCopy("box_proxy", b => Replace(b, "\"mass_kg\": 1.5", "\"mass_kg\": 150.0")), "bytes, files list says", "a resized asset fails the size check");
            CheckRejected(() => LoadCopy(null, b => Replace(b, "\n", "\r\n")), "LF line endings", "CRLF bytes are rejected");
            CheckRejected(() => LoadCopy(null, b => Replace(b, "\"asset\": \"objects/book_proxy/asset.json\"", "\"asset\": \"../escape/asset.json\"")), "unsafe", "a path leaving the room is rejected");

            var garageDirectory = Path.GetFullPath(ProjectSettings.GlobalizePath("res://") + "../contracts/examples/rooms/garage_example");
            var garage = RoomData.Load(garageDirectory);
            Check(garage.SourceKind == "capture" && garage.Objects.Count == 4, "the capture-shaped garage example loads");
            Check(garage.Objects.Count(o => o.Asset.AssetId == "pine_shelving") == 2 && ReferenceEquals(garage.Objects[0].Asset, garage.Objects[1].Asset), "two instances share one asset");
            var garageBuilt = RoomBuilder.Build(garage);
            AddChild(garageBuilt);
            var garageObjects = garageBuilt.GetNode("Objects").GetChildren().OfType<Node3D>().ToArray();
            Check(garageObjects.All(n => n.GetNode("Visual").FindChildren("*", "MeshInstance3D", true, false).Count > 0), "GLB meshes load as object visuals");
            Check(garageObjects.All(n => n.GetChildren().OfType<CollisionShape3D>().Any(c => c.Shape is ConvexPolygonShape3D)), "mesh objects get convex collision");
            Check(garageObjects.Where(n => n.GetMeta("asset_id").AsString() == "pine_shelving").All(n => n.GetMeta("collision_approximation").AsString() == "single_convex_hull"), "decomposition fallback is labelled");
            var clutter = garageObjects.Single(n => n.GetMeta("entity_id").AsString() == "obj:paint_clutter");
            Check(clutter.GetMeta("support_target").AsString() == "obj:shelving_right" && clutter.GetMeta("review_status").AsString() == "needs_capture", "support and review status reach the scene");
            await Frames(3);
            var shelfHit = Ray(new Vector3(-0.1f, 2.5f, -2.5f), new Vector3(-0.1f, 0.5f, -2.5f));
            Check(Entity(shelfHit) == "obj:shelving_left" && Mathf.Abs(shelfHit["position"].AsVector3().Y - 1.79f) < 0.01f, "the shelving collides at its 1.79 m top");
            garageBuilt.QueueFree();

            var preset = StylePreset.Load(RoomWorld.DefaultStyle);
            Check(preset.PresetId == "storybook_painterly" && preset.PresetVersion >= 1 && preset.Sha256 == FileAccess.GetSha256(RoomWorld.DefaultStyle), "the seed preset loads and reports its pin");
            Check(preset.RoleRoughness.ContainsKey("wood") && preset.MaxShadowedLights >= 1, "per-role treatments and budgets are read");
            var look = new LookDirector();
            AddChild(look);
            look.Apply(preset, room);
            Check(look.RoomLightCount == 1 && look.GetChildren().OfType<WorldEnvironment>().Any(), "the look applies environment and the room's lamp");
            look.QueueFree();

            var world = GD.Load<PackedScene>("res://scenes/room.tscn").Instantiate<RoomWorld>();
            AddChild(world);
            for (var i = 0; i < 120 && !world.WorldReady && world.LoadError.Length == 0; i++) await Frames(1);
            Check(world.WorldReady, "the room scene boots: " + world.LoadError);
            if (world.WorldReady)
            {
                await Frames(30);
                Check(world.Player.IsOnFloor() && world.Player.GlobalPosition.Y < 0.02f, "the player rests on the rug at the spawn");
                Check(world.Companion.CompanionId == "local_companion" && world.Companion.CurrentIntent == "follow", "the companion spawns with its identity and follows");
            }
            GD.Print($"NATIVE_ROOM_DATA: {_checks - _failures}/{_checks} checks passed; room contract, integrity, derived scene, look seam");
            GetTree().Quit(_failures == 0 ? 0 : 1);
        }
        catch (Exception exception)
        {
            GD.PushError("Room data test exception: " + exception);
            GetTree().Quit(1);
        }
    }

    private static RoomData LoadCopy(string? assetToEdit, Func<byte[], byte[]> edit)
    {
        var target = "user://tests/rooms/test_room";
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(target + "/objects"));
        foreach (var asset in new[] { "table_proxy", "box_proxy", "book_proxy", "rug_proxy", "doorstop_proxy" })
        {
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath($"{target}/objects/{asset}"));
            var bytes = FileAccess.GetFileAsBytes($"{RoomWorld.DefaultRoom}/objects/{asset}/asset.json");
            Write($"{target}/objects/{asset}/asset.json", asset == assetToEdit ? edit(bytes) : bytes);
        }
        var manifest = FileAccess.GetFileAsBytes(RoomWorld.DefaultRoom + "/room.json");
        Write(target + "/room.json", assetToEdit == null ? edit(manifest) : manifest);
        return RoomData.Load(target);
    }

    private static void Write(string path, byte[] bytes)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        file.StoreBuffer(bytes);
    }

    private static byte[] Replace(byte[] bytes, string from, string to)
    {
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        if (!text.Contains(from)) throw new InvalidOperationException($"fixture text '{from}' not found");
        return System.Text.Encoding.UTF8.GetBytes(text.Replace(from, to));
    }

    private void CheckRejected(Action action, string fragment, string label)
    {
        try { action(); Check(false, label + " (it loaded)"); }
        catch (RoomLoadException error) { Check(error.Message.Contains(fragment), $"{label} ({error.Message})"); }
    }

    private static bool UpFacingTrianglesAreClockwise(ArrayMesh mesh)
    {
        var arrays = mesh.SurfaceGetArrays(0);
        var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var found = 0;
        for (var i = 0; i < vertices.Length; i += 3)
        {
            // Stored normals are compressed, so compare direction rather than exact components.
            if (normals[i].Dot(Vector3.Up) < 0.99f) continue;
            found++;
            if ((vertices[i + 1] - vertices[i]).Cross(vertices[i + 2] - vertices[i]).Dot(Vector3.Up) >= 0) return false;
        }
        return found > 0;
    }

    private Godot.Collections.Dictionary Ray(Vector3 from, Vector3 to) =>
        GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, to, RoomBuilder.WorldLayer));

    private static string Role(Godot.Collections.Dictionary hit) =>
        hit.Count > 0 && hit["collider"].AsGodotObject() is Node node && node.HasMeta("surface_role") ? node.GetMeta("surface_role").AsString() : "";

    private static string Entity(Godot.Collections.Dictionary hit) =>
        hit.Count > 0 && hit["collider"].AsGodotObject() is Node node && node.HasMeta("entity_id") ? node.GetMeta("entity_id").AsString() : "";

    private async Task Frames(int count)
    {
        for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private void Check(bool condition, string label)
    {
        _checks++;
        if (condition) return;
        _failures++;
        GD.PushError("Room data: " + label);
    }
}

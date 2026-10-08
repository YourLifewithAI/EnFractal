using Godot;
using System.Threading.Tasks;
using EnFractal.Native;
using EnFractal.Native.Navigation;

namespace EnFractal.Tests;

/// <summary>
/// The founder's report in the real test room: "If I walk behind the big box and summon my comp, it gets stuck on
/// the other side of the box." Boots the room as the game does, puts the box between the two avatars, and sends
/// come through the command host as key 3 does. No room or save files are written.
/// </summary>
public partial class RoomNavigationTest : Node3D
{
    private int _checks;
    private int _failures;

    public override async void _Ready()
    {
        try
        {
            var world = GD.Load<PackedScene>("res://scenes/room.tscn").Instantiate<RoomWorld>();
            AddChild(world);
            for (var i = 0; i < 120 && !world.WorldReady && world.LoadError.Length == 0; i++) await Frames(1);
            Check(world.WorldReady, "the room boots: " + world.LoadError);
            if (!world.WorldReady) { Finish(); return; }
            await Frames(5);
            var navigation = world.GetNodeOrNull<RoomNavigation>("RoomNavigation");
            Check(navigation != null && navigation.IsReady && world.Companion.Navigation == navigation,
                "the room bakes a navigation mesh from its static collision and gives it to the companion");
            if (navigation == null) { Finish(); return; }
            GD.Print($"ROOM_NAVIGATION_MEASURED {navigation.PolygonCount} polygons, bake {navigation.LastBakeMs:0.0} ms, agent radius {navigation.AgentRadiusM:0.000} m");

            // The big box (obj:box): 35 x 30 x 35 cm with its foot at (1.1, 0, 0.2). The player stands behind it,
            // the companion on the far side, so the straight line between them runs through the box.
            var box = world.Built.GetNode<Node3D>("Objects/box").GlobalPosition;
            var player = world.Player;
            var companion = world.Companion;
            var playerSpot = box + new Vector3(0, 0.003f, 0.43f);
            var farSide = box + new Vector3(0, 0.01f, -0.45f);
            var host = EnFractal.Native.Kernel.CommandHost.Of(world)!;
            host.PlayerGoal("stop");
            Check(player.TryTeleportTo(playerSpot) && companion.TryTeleportTo(farSide), "player behind the big box, companion on the far side");
            var route = navigation.FindRoute(companion.GlobalPosition, player.GlobalPosition, 0.05f);
            Check(route.Reaches && route.LengthM > PlanarDistance(companion.GlobalPosition, player.GlobalPosition) + 0.1f,
                $"the planned route goes round the box (route {route.LengthM:0.00} m against {PlanarDistance(companion.GlobalPosition, player.GlobalPosition):0.00} m straight)");

            // Without navigation, as in the founder's playtest: stuck on the far side.
            companion.BindNavigation(null);
            var sent = host.PlayerGoal("come");
            Check(sent["ok"]!.GetValue<bool>(), "come is accepted from the player through the command host");
            await Frames(240);
            var gap = PlanarDistance(companion.GlobalPosition, player.GlobalPosition);
            GD.Print($"ROOM_NAVIGATION_MEASURED without navigation: intent {companion.CurrentIntent}, gap {gap:0.00} m");
            Check(companion.CurrentIntent == "come" && gap > CompanionAvatar.ComeArrivalM + 0.2f, "local steering alone stays stuck behind the box (the founder's report reproduced)");

            // With the room's navigation: round the box and arrived.
            host.PlayerGoal("stop");
            Check(companion.TryTeleportTo(farSide), "companion back on the far side");
            companion.BindNavigation(navigation);
            host.PlayerGoal("come");
            var frames = 0;
            var blocked = 0;
            var entered = false;
            for (; frames < 360 && companion.CurrentIntent == "come"; frames++)
            {
                await Frames(1);
                if (companion.GoalBlocked) blocked++;
                entered |= ClearanceFromSquare(companion.GlobalPosition, box, 0.175f) < companion.BodyRadiusM - 0.005f;
            }
            gap = PlanarDistance(companion.GlobalPosition, player.GlobalPosition);
            GD.Print($"ROOM_NAVIGATION_MEASURED with navigation: arrived in {frames} frames, gap {gap:0.00} m, blocked frames {blocked}");
            Check(companion.CurrentIntent == "stay" && gap <= CompanionAvatar.ComeArrivalM + 0.02f && blocked == 0 && !entered,
                "with navigation the summoned companion walks round the big box and arrives beside the player");

            // Follow from the far side: it comes round the box to rest near the player instead of waiting behind it.
            Check(companion.TryTeleportTo(farSide), "companion on the far side again");
            host.PlayerGoal("follow");
            await Frames(300);
            gap = PlanarDistance(companion.GlobalPosition, player.GlobalPosition);
            var way = navigation.FindRoute(companion.GlobalPosition, player.GlobalPosition, 0.05f);
            GD.Print($"ROOM_NAVIGATION_MEASURED follow from the far side: gap {gap:0.00} m, walk {way.LengthM:0.00} m");
            Check(!companion.FollowMoving && gap <= CompanionAvatar.FollowFarM + 0.01f && way.LengthM <= gap + 0.05f,
                "follow from the far side of the box comes round and rests with nothing between them");
            host.PlayerGoal("stop");
            Finish();
        }
        catch (System.Exception exception)
        {
            GD.PushError("Room navigation test exception: " + exception);
            GetTree().Quit(1);
        }
    }

    private void Finish()
    {
        GD.Print($"NATIVE_ROOM_NAVIGATION: {_checks - _failures}/{_checks} checks passed; companion routes round the big box in the test room through the command host");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private static float PlanarDistance(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    /// <summary>Planar distance from a point to a square footprint (zero inside it).</summary>
    private static float ClearanceFromSquare(Vector3 point, Vector3 centre, float half) =>
        new Vector2(Mathf.Max(Mathf.Abs(point.X - centre.X) - half, 0), Mathf.Max(Mathf.Abs(point.Z - centre.Z) - half, 0)).Length();

    private async Task Frames(int count)
    {
        for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private void Check(bool condition, string label)
    {
        _checks++;
        if (condition) return;
        _failures++;
        GD.PushError("Room navigation: " + label);
    }
}

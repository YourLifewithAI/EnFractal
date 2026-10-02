using Godot;
using EnFractal.Native;

public partial class PflugerSceneTest : Node
{
    private int _checks;
    private int _failures;

    public override async void _Ready()
    {
        try
        {
            var captureMode = OS.GetCmdlineUserArgs().Contains("--capture-pfluger");
            var world = new PflugerWorld();
            AddChild(world);
            for (var i = 0; !world.WorldReady && i < 600; i++) await Frames(1);
            Check(world.WorldReady, "Pfluger world becomes ready");
            if (!world.WorldReady) { GetTree().Quit(1); return; }
            var player = world.Player;
            player.ReadKeyboard = false;
            await Frames(90);
            Check(Mathf.IsEqualApprox(player.BodyHeightM, 0.30f), "actual player body is 30 cm");
            Check(player.IsOnFloor(), "player settles on landing collider");
            Check(player.GlobalPosition.DistanceTo(world.Spawn) < 0.20f, "spawn has no large collision displacement");
            Check(world.Companion.CompanionId != "local_player", "companion has separate identity");
            Check(world.Route.Length >= 2, "source-derived landing route loaded");
            Check(world.BridgeSegmentCount > 0 && world.BuildingCount > 0, "independent bridge and urban context generated");
            var first = player.GlobalPosition;
            player.Rotation = new Vector3(0, Mathf.Atan2(-(world.Route[1].X - first.X), -(world.Route[1].Z - first.Z)), 0);
            player.SetControlInput(new Vector2(0, 1));
            await Frames(120);
            player.SetControlInput(Vector2.Zero);
            await Frames(10);
            Check(new Vector2(player.GlobalPosition.X - first.X, player.GlobalPosition.Z - first.Z).Length() > 1.0f,
                "player walks more than 1 m through actual landing scene");
            Check(player.IsOnFloor(), "landing route remains supported after walking");
            if (!captureMode)
            {
                Check(player.TryTeleportTo(world.Spawn), "full route starts on the checked landing");
                Check(world.Companion.TryTeleportTo(world.Spawn + player.Basis.X * 0.45f), "companion starts beside the route landing");
                world.Companion.Follow();
                await Frames(10);
                var routeResets = 0;
                var companionResets = 0;
                var maximumGap = 0.0f;
                var walkedMetres = 0.0f;
                var fullRoute = true;
                for (var index = 1; index < world.Route.Length; index++)
                {
                    var target = world.Route[index];
                    var remaining = new Vector2(target.X - player.Position.X, target.Z - player.Position.Z).Length();
                    var frameBudget = Mathf.CeilToInt(remaining / player.WalkSpeedMps * 60 * 2) + 120;
                    var reached = false;
                    for (var frame = 0; frame < frameBudget; frame++)
                    {
                        var direction = target - player.GlobalPosition; direction.Y = 0;
                        if (direction.Length() < 0.06f) { reached = true; break; }
                        player.Rotation = new Vector3(0, Mathf.Atan2(-direction.X, -direction.Z), 0);
                        player.SetControlInput(new Vector2(0, 1));
                        var prior = player.GlobalPosition;
                        var companionPrior = world.Companion.GlobalPosition;
                        await Frames(1);
                        var distance = new Vector2(player.Position.X - prior.X, player.Position.Z - prior.Z).Length();
                        walkedMetres += distance;
                        if (player.GlobalPosition.DistanceTo(prior) > 1) routeResets++;
                        if (world.Companion.GlobalPosition.DistanceTo(companionPrior) > 1) companionResets++;
                        maximumGap = Mathf.Max(maximumGap, player.GlobalPosition.DistanceTo(world.Companion.GlobalPosition));
                    }
                    player.SetControlInput(Vector2.Zero);
                    Check(reached, $"walks source route waypoint {index} without teleport or jumping");
                    if (!reached) { fullRoute = false; break; }
                }
                Check(fullRoute && routeResets == 0 && walkedMetres > 147,
                    "walks the full 150 m horizontal source route continuously");
                Check(fullRoute && companionResets == 0 && maximumGap <= 1.5f,
                    $"companion follows the whole route without recovery (maximum gap {maximumGap:F3} m)");
                if (fullRoute)
                {
                    Check(player.GlobalPosition.Y > world.SurfaceHeightQuery(player.Position.X, player.Position.Z) + 5,
                        "open-water route endpoint rests on independent elevated deck");
                }
            }
            var bridgePoint = world.Route[world.Route.Length / 2];
            Check(player.TryTeleportTo(bridgePoint + Vector3.Up * 0.02f), "recovery can select bridge surface separately from terrain");
            await Frames(10);
            Check(player.IsOnFloor(), "player remains supported on bridge deck");
            world.Companion.Stop();
            var stopped = world.Companion.GlobalPosition;
            await Frames(30);
            Check(new Vector2(world.Companion.GlobalPosition.X - stopped.X, world.Companion.GlobalPosition.Z - stopped.Z).Length() < 0.01f,
                "companion stops in the real scene");
            Check(player.TryTeleportTo(world.Spawn), "landing remains recoverable");
            if (!captureMode)
            {
                await Frames(3);
                var foundBareGround = false;
                for (var x = 145; x <= 285 && !foundBareGround; x += 20)
                {
                    var position = new Vector3(x, world.SurfaceHeightQuery(x, -230) + 0.01f, -230);
                    var ray = PhysicsRayQueryParameters3D.Create(position + Vector3.Up * 0.2f, position + Vector3.Down * 0.5f, 1);
                    var hit = world.GetWorld3D().DirectSpaceState.IntersectRay(ray);
                    var body = hit.Count > 0 ? hit["collider"].AsGodotObject() as Node : null;
                    if (body != null && body.Name.ToString().StartsWith("TerrainCollider_"))
                        foundBareGround = world.Companion.TryTeleportTo(position);
                }
                Check(foundBareGround, "waiting companion fixture uses bare source terrain, not a persistent deck");
                world.Companion.Stop();
                await Frames(10);
                var waitingPoint = world.Companion.GlobalPosition;
                Check(player.TryTeleportTo(world.Route[^1] + Vector3.Up * 0.02f) && player.Position.DistanceTo(waitingPoint) > 128,
                    "player travels beyond the waiting companion's terrain neighborhood");
                await Frames(180);
                Check(world.Companion.IsOnFloor() && world.Companion.GlobalPosition.DistanceTo(waitingPoint) < 0.03f,
                    "stopped companion keeps terrain support while the player is far away");
                Check(world.Companion.Recover() && world.Companion.GlobalPosition.DistanceTo(waitingPoint) < 0.03f,
                    "distant companion can recover to its retained supported checkpoint");
                var collision = world.GetNode<Node3D>("ValidatedSourceTerrainCollision");
                Check(collision.Get("active_patches").AsGodotDictionary().Count <= 18,
                    "two-actor terrain residency stays bounded to at most 18 patches");
                Check(player.TryTeleportTo(world.Spawn), "player returns from remote support fixture");
            }
            var hud = world.GetNode<PflugerHud>("PflugerHud");
            foreach (var mode in new[] { 0, 1, 2 })
            {
                hud.SetViewMode(mode);
                await Frames(2);
                Check(GetViewport().GetCamera3D() != null, "view has active camera " + mode);
            }
            hud.SetViewMode(1);
            if (captureMode)
            {
                // A supported point at the north loop's exit shows the route
                // toward the lake; the initial ramp remains the gameplay spawn.
                var landingIndex = System.Math.Min(13, world.Route.Length - 2);
                Check(player.TryTeleportTo(world.Route[landingIndex] + Vector3.Up * 0.02f), "capture uses a supported north landing route point");
                var viewDirection = world.Route[landingIndex + 1] - world.Route[landingIndex];
                viewDirection.Y = 0; viewDirection = viewDirection.Normalized();
                player.Rotation = new Vector3(0, Mathf.Atan2(-viewDirection.X, -viewDirection.Z), 0);
                Check(world.Companion.TryTeleportTo(player.Position + player.Basis.X * 0.5f + viewDirection * 0.65f), "capture companion stands on the same route surface");
                world.Companion.Stop();
                await Frames(30);
                await Capture("pfluger-small-avatar-preview.png");
                hud.Visible = false;
                hud.SetViewMode(0);
                await Capture("pfluger-eye-height-preview.png");
                var survey = new Camera3D { Position = new Vector3(-20, 124, 90), Far = 3000, Fov = 55 };
                world.AddChild(survey);
                survey.LookAt(new Vector3(135, PflugerWorld.InferredDeckHeightM, -25));
                survey.MakeCurrent();
                await Capture("pfluger-context-preview.png");
            }
            var scope = captureMode ? "PFLUGER_RENDER_CAPTURE; full traversal omitted, run the headless scene fixture for route proof" : "PFLUGER_SCENE; full 150 m player and companion traversal";
            GD.Print($"{scope}: {_checks - _failures}/{_checks} checks passed; source/proxy preview, art and fidelity acceptance open");
            GetTree().Quit(_failures == 0 ? 0 : 1);
        }
        catch (System.Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private async System.Threading.Tasks.Task Capture(string file)
    {
        for (var i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(!image.IsEmpty(), "renderer produced pixels");
        Check(image.SavePng(ProjectSettings.GlobalizePath("res://../docs/images/" + file)) == Error.Ok, "capture saved " + file);
        GD.Print("Pfluger render: " + file + " | " + RenderingServer.GetVideoAdapterName());
    }
    private async System.Threading.Tasks.Task Frames(int count)
    { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private void Check(bool ok, string message)
    { _checks++; if (!ok) { _failures++; GD.PushError("Pfluger scene: " + message); } }
}

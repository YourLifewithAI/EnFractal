using Godot;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using EnFractal.Native;
using EnFractal.Native.Kernel;
using EnFractal.Native.Look;
using EnFractal.Native.Room;

namespace EnFractal.Tests;

/// <summary>
/// The land's collision where the bodies are (S12b, TerrainCollision), on a wide synthetic island (IslandFixture.WriteWideLand:
/// 224 x 64 m of rolling ground in 56 tiles, a sea round it, a jetty at the far east end): every tile collides at boot; once the
/// bodies have stood a while the far tiles go, and come back before anything touches them: a body running fast, a thing flung
/// across the land, a body dropped from the air onto ground that was gone, the Gubble catching up beside the player far away,
/// the player sent home to the jetty from the other end, a fire burning where nobody is, a node that asks to keep its ground,
/// and a swimmer wrapped round the open sea. No body is ever below the ground.
/// </summary>
public partial class IslandLoadingTest
{
    private RoomWorld _land = null!;

    private async Task CheckGroundStreaming()
    {
        // Groups 1 and 2 both used (the contract numbers them with none unused): the first row is a patch of its own.
        var grass = IslandFixture.GrassRows(2_000, new Rect2(-16, -32, 224, 64), seed: 9).Select((r, i) => i == 0 ? r with { Group = 1 } : r).ToList();
        var directory = IslandFixture.WriteWideLand("island_streaming", -1, 12, -2, 1, new[] { IslandFixture.Grass("grass", grass) });
        _land = GD.Load<PackedScene>("res://scenes/room.tscn").Instantiate<RoomWorld>();
        _land.RoomDirectory = directory;
        AddChild(_land);
        await UntilReady(_land);
        Check(_land.WorldReady, "the wide island boots: " + _land.LoadError);
        if (!_land.WorldReady) return;
        var ground = _land.Built.GetNode<TerrainCollision>(TerrainCollision.ViewName);
        var part = _land.Built.GetNode<StaticBody3D>("Shell/terrain_grass");
        var player = _land.Player;
        player.ReadKeyboard = false;
        var far = OnGround(190, -24);

        // Boot: the whole land collides, as before, so whatever the boot asks of it far away (a save's creations checked on
        // their surfaces, the ponds surveyed) finds it.
        var bootHit = Ray(far + Vector3.Up * 5, far + Vector3.Down * 5, RoomBuilder.WorldLayer);
        Check(ground.TileCount == 56 && ground.TilesBuilt == 56 && bootHit.Count > 0 && Mathf.Abs(bootHit["position"].AsVector3().Y - far.Y) < 0.05f,
            $"every tile of the land collides at boot ({ground.TilesBuilt} of {ground.TileCount}), the far end too");

        // Released once the bodies have stood a while; asked for, a tile is there at once, and hits name the part.
        await Seconds(TerrainCollision.BootHoldS + TerrainCollision.ReleaseAfterS + 1);
        var kept = ground.TilesBuilt;
        Check(kept < 30 && TerrainCollision.CollidesAt(_land, player.GlobalPosition) && !TerrainCollision.CollidesAt(_land, far) &&
              Ray(far + Vector3.Up * 5, far + Vector3.Down * 5, RoomBuilder.WorldLayer).Count == 0,
            $"after the boot the land collides only round the bodies and the places a body may be put ({kept} of {ground.TileCount} tiles; {ground.Releases} released)");
        Check(TerrainCollision.EnsureAround(_land, far, 1f) > 0, "a tile asked for is built");
        var asked = Ray(far + Vector3.Up * 5, far + Vector3.Down * 5, RoomBuilder.WorldLayer);
        var fromBelow = Ray(far + Vector3.Down * 2, far + Vector3.Up * 5, RoomBuilder.WorldLayer);
        Check(asked.Count > 0 && Mathf.Abs(asked["position"].AsVector3().Y - far.Y) < 0.05f && asked["collider"].AsGodotObject() == part &&
              part.GetMeta("entity_id").AsString() == "shell:terrain_grass" && fromBelow.Count == 0,
            "a tile built on request answers a ray at once (no physics step between), names its shell part, and is one-sided");
        var tiled = _land.Room.Terrain!.Part("shell:terrain_grass")!;
        Check(tiled.GroundTriangles(0) == 128 && ground.TrianglesBuilt == Enumerable.Range(0, tiled.Tiles.Count).Where(i => TerrainCollision.CollidesAt(_land, Centre(tiled, i))).Sum(i => (long)tiled.GroundTriangles(i)),
            $"each tile holds its level-0 ground (128 triangles), and the triangles built are the built tiles' ({ground.TrianglesBuilt})");

        await HoldsItsGround();
        var copies = _land.Built.GetNode<PlantCopiesView>(PlantCopiesView.ViewName);
        var (fills, empties) = (copies.VisualFills, copies.VisualEmpties);
        await RunsAcross();
        await Frames(30);
        CopiesFollow(copies, fills, empties);
        await FlingsAThing();
        await DropsFromTheAir();
        await CatchesUp();
        // Everyone far from the start now (the Gubble caught up): the copies there are let go.
        await Frames(60);
        Check(copies.VisualEmpties > empties && copies.BandOf("grass", 0, -1) == -1 && copies.InstancesHeld < 1500,
            $"the copies nobody is near are let go ({copies.VisualEmpties - empties} tiles emptied; the start's tile empty; {copies.InstancesHeld} of 2000 held)");
        await GoesHome();
        await KeepsAFire();
        await WrapsRound();
        Measure($"ISLAND_GROUND streaming: {ground.Builds} tiles built, {ground.Releases} released, {ground.SyncBuilds} built in the tick a body needed them (longest {ground.LongestSyncMs:0.0} ms), at most {ground.MostTilesBuilt} of {ground.TileCount} at once");
        _land.QueueFree();
        await Frames(2);
    }

    /// <summary>A point on the wide island's ground (its rolling height).</summary>
    private static Vector3 OnGround(float x, float z) => new(x, (float)IslandFixture.Height(x, z), z);

    private static Vector3 Centre(TiledPart part, int tile) => new(part.Tiles[tile].Tx * 16 + 8, 0, part.Tiles[tile].Tz * 16 + 8);

    private async Task Seconds(double seconds) => await Frames((int)Math.Ceiling(seconds * Engine.PhysicsTicksPerSecond));

    /// <summary>The ground straight under a point (from 2 m over it), or null where nothing collides.</summary>
    private float? GroundUnder(Vector3 at)
    {
        var hit = Ray(at + Vector3.Up * 2, at + Vector3.Down * 3, RoomBuilder.WorldLayer, _land.Player.GetRid());
        return hit.Count > 0 ? hit["position"].AsVector3().Y : null;
    }

    /// <summary>A body's feet against the ground under them this tick: how far below it they are (0 when on or over it), and whether ground was there at all.</summary>
    private (float Below, bool Ground) Footing(Node3D body, float foot = 0f)
    {
        var at = body.GlobalPosition + Vector3.Down * foot;
        var y = GroundUnder(at);
        return y is { } g ? (Math.Max(0f, g - at.Y), true) : (0f, false);
    }

    /// <summary>A node in TerrainCollision.AnchorGroup keeps the ground under it, far from every body, and lets it go when it leaves the group.</summary>
    private async Task HoldsItsGround()
    {
        var at = OnGround(120, 20);
        var anchor = new Node3D { Name = "Anchor", Position = at };
        anchor.AddToGroup(TerrainCollision.AnchorGroup);
        _land.AddChild(anchor);
        await Seconds(0.2);
        var built = TerrainCollision.CollidesAt(_land, at);
        await Seconds(TerrainCollision.ReleaseAfterS + 1);
        var kept = TerrainCollision.CollidesAt(_land, at);
        anchor.QueueFree();
        await Seconds(TerrainCollision.ReleaseAfterS + 1);
        Check(built && kept && !TerrainCollision.CollidesAt(_land, at), "a node that asks to keep its ground (TerrainCollision.AnchorGroup) has it while it asks, far from the bodies");
    }

    /// <summary>The player runs east at 6 m/s for 8 s: the ground ahead is there before the feet, and they never go below it.</summary>
    private async Task RunsAcross()
    {
        var player = _land.Player;
        var ground = _land.Built.GetNode<TerrainCollision>(TerrainCollision.ViewName);
        var speed = player.RunSpeedMps;
        player.RunSpeedMps = 6f;
        player.SetMovementFrame(null);
        player.Rotation = new Vector3(0, -Mathf.Pi / 2, 0);
        var start = player.GlobalPosition;
        var syncBefore = ground.SyncBuilds;
        float worst = 0;
        var missing = 0;
        player.SetControlInput(new Vector2(0, 1), sprint: true);
        for (var tick = 0; tick < 8 * Engine.PhysicsTicksPerSecond; tick++)
        {
            await Frames(1);
            var (below, there) = Footing(player);
            worst = Math.Max(worst, below);
            if (!there || !TerrainCollision.CollidesAt(_land, player.GlobalPosition)) missing++;
        }
        player.SetControlInput(Vector2.Zero);
        player.RunSpeedMps = speed;
        await Frames(10);
        var ran = new Vector2(player.GlobalPosition.X - start.X, player.GlobalPosition.Z - start.Z).Length();
        Measure($"ISLAND_GROUND running: {ran:0.0} m in 8 s, feet at most {worst * 1000:0.0} mm under the ground, {missing} ticks without ground under them, {ground.SyncBuilds - syncBefore} tiles built in the tick they were needed");
        Check(ran > 30 && worst < 0.005f && missing == 0 && ground.SyncBuilds == syncBefore,
            $"running at 6 m/s the ground ahead is built before the feet reach it ({ran:0} m; {missing} ticks without ground, {ground.SyncBuilds - syncBefore} built at the last moment)");
    }

    /// <summary>
    /// The copies follow the player as the ground does (S12b): its own tile in full detail, one 30-odd metres off in the lightest
    /// mesh, one 100 m ahead not held at all; tiles were filled again and emptied as it ran.
    /// </summary>
    private void CopiesFollow(PlantCopiesView copies, int fills, int empties)
    {
        var at = _land.Player.GlobalPosition;
        var (tx, tz) = PlantCopies.TileOf(at.X, at.Z);
        var set = _land.Room.Copies!.Find("grass")!;
        var bands = DrawDistances.CopyBands(set.Kind, 0.13f, set.MeshPaths.Count);
        int Band(float x, float z) { var (a, b) = PlantCopies.TileOf(x, z); return set.FindTile(a, b) >= 0 ? copies.BandOf("grass", a, b) : -2; }
        var near = Band(at.X, at.Z);
        var mid = Band(at.X + 36, at.Z);
        var ahead = Band(at.X + 100, at.Z);
        Check(near == 0 && mid == bands.Length - 1 && ahead == -1 && copies.InstancesHeld < _land.Room.Copies.TotalCount && copies.VisualFills > fills,
            $"the copies are held round the player as it runs: its tile ({tx}, {tz}) in band {near}, 36 m ahead in band {mid}, 100 m ahead not held ({ahead}); {copies.InstancesHeld} of {_land.Room.Copies.TotalCount} held; {copies.VisualFills - fills} tiles filled on the way");
    }

    /// <summary>A ball flung east at 14 m/s across ground that had gone: its ground is built ahead of it, and it never drops through.</summary>
    private async Task FlingsAThing()
    {
        var at = OnGround(100, 20);
        Check(!TerrainCollision.CollidesAt(_land, at) && !TerrainCollision.CollidesAt(_land, OnGround(130, 20)), "the ball's way lies over ground that is not built");
        var ball = new RigidBody3D
        {
            Name = "Ball", Mass = 1f, ContinuousCd = true, CollisionLayer = 0, CollisionMask = RoomBuilder.WorldLayer, Position = at + Vector3.Up * 0.06f,
            PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0f, Bounce = 0f }, LinearVelocity = new Vector3(14, 0, 0),
        };
        ball.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 0.05f } });
        _land.AddChild(ball);
        float worst = 0;
        var missing = 0;
        var lowest = float.MaxValue;
        var worstAt = "";
        for (var tick = 0; tick < 4 * Engine.PhysicsTicksPerSecond; tick++)
        {
            await Frames(1);
            var (below, _) = Footing(ball, 0.05f);
            if (below > worst) worstAt = $"{ball.GlobalPosition} tick {tick}, its tile {(TerrainCollision.CollidesAt(_land, ball.GlobalPosition) ? "built" : "missing")}, moving {ball.LinearVelocity}";
            worst = Math.Max(worst, below);
            if (!TerrainCollision.CollidesAt(_land, ball.GlobalPosition)) missing++;
            lowest = Math.Min(lowest, ball.GlobalPosition.Y);
        }
        var travelled = ball.GlobalPosition.X - at.X;
        Measure($"ISLAND_GROUND a ball flung at 14 m/s: {travelled:0.0} m in 4 s, its bottom at most {worst * 1000:0.0} mm under the ground ({worstAt}), lowest at {lowest:0.000} m");
        // Jolt lets a fast contact sink by up to its penetration slop (2 cm) before it pushes back; through the ground is far more.
        Check(travelled > 25 && missing == 0 && worst < 0.025f && lowest > 0.05f, $"a thing flung across ground that had gone is carried on it ({travelled:0} m, {missing} ticks over missing ground, at most {worst * 1000:0} mm in), never through it");
        ball.QueueFree();
    }

    /// <summary>The player put 3 m over ground that had gone, far from everything: the ground is built in the tick it arrives, and it lands on it.</summary>
    private async Task DropsFromTheAir()
    {
        var player = _land.Player;
        var ground = _land.Built.GetNode<TerrainCollision>(TerrainCollision.ViewName);
        var spot = OnGround(150, -24);
        Check(!TerrainCollision.CollidesAt(_land, spot), "the ground under the drop is not built");
        var syncBefore = ground.SyncBuilds;
        player.GlobalPosition = spot + Vector3.Up * 3;
        player.Velocity = Vector3.Zero;
        player.ResetPhysicsInterpolation();
        float worst = 0;
        for (var tick = 0; tick < 3 * Engine.PhysicsTicksPerSecond && !(player.IsOnFloor() && tick > 10); tick++)
        {
            await Frames(1);
            worst = Math.Max(worst, Footing(player).Below);
        }
        var groundY = GroundUnder(player.GlobalPosition);
        Check(player.IsOnFloor() && groundY is { } y && Mathf.Abs(player.GlobalPosition.Y - y) < 0.02f && worst < 0.005f && ground.SyncBuilds > syncBefore,
            $"dropped onto ground that had gone, the player lands on it (at {player.GlobalPosition.Y:0.000} m over ground at {groundY:0.000}; built in the tick it arrived: {ground.SyncBuilds - syncBefore} tiles)");
    }

    /// <summary>The Gubble, left more than CatchUpM behind, catches up beside the player far away, onto ground that is there.</summary>
    private async Task CatchesUp()
    {
        var player = _land.Player;
        var companion = _land.Companion;
        companion.Follow();
        var before = companion.CatchUps;
        for (var waited = 0; waited < 8 * Engine.PhysicsTicksPerSecond && companion.CatchUps == before; waited++) await Frames(1);
        await Seconds(0.5);
        var gap = companion.GlobalPosition.DistanceTo(player.GlobalPosition);
        var (below, there) = Footing(companion);
        Check(companion.CatchUps > before && gap < 5 && there && below < 0.005f && TerrainCollision.CollidesAt(_land, companion.GlobalPosition),
            $"the Gubble catches up beside the player far away ({gap:0.00} m off) onto ground that is built, never under it");
    }

    /// <summary>B: the player goes home to the jetty at the far east end, and stands on its root.</summary>
    private async Task GoesHome()
    {
        var player = _land.Player;
        var jetty = _land.Room.Sea!.JettyData!.Value;
        player.GlobalPosition = OnGround(-10, 20) + Vector3.Up * 0.01f;
        player.Velocity = Vector3.Zero;
        player.ResetPhysicsInterpolation();
        await Seconds(TerrainCollision.ReleaseAfterS + 1);
        var pinned = TerrainCollision.CollidesAt(_land, jetty.RootM);
        var trips = player.HomeTrips;
        Check(player.RequestHome(), "the player asks to go home");
        for (var waited = 0; waited < 5 * Engine.PhysicsTicksPerSecond && player.HomeTrips == trips; waited++) await Frames(1);
        await Seconds(0.5);
        var (below, there) = Footing(player);
        Check(pinned && player.HomeTrips > trips && player.LastHome == "jetty" && player.GlobalPosition.DistanceTo(jetty.RootM) < 0.5f && player.IsOnFloor() && there && below < 0.005f,
            $"home from the far west, the player stands on the jetty's root 200 m away ({player.LastHome}, {player.GlobalPosition.DistanceTo(jetty.RootM):0.00} m from it), whose ground was kept all along");
    }

    /// <summary>A fire burning where nobody is keeps its ground, so nature's spread never runs out of land; it goes once the fire is out.</summary>
    private async Task KeepsAFire()
    {
        var host = _land.GetNode<CommandHost>("CommandHost");
        var at = OnGround(60, -24);
        Check(!TerrainCollision.CollidesAt(_land, at), "the fire's ground is not built before it burns");
        // A fire with no burning cells goes out at the next nature step; here it is kept lit every tick, as a fire that burns on.
        var fire = new NatureFire { Center = at, RadiusM = 1f, RemainingS = 600f };
        async Task Burn(double seconds)
        {
            for (var tick = 0; tick < seconds * Engine.PhysicsTicksPerSecond; tick++)
            {
                host.Nature.Fire = fire;
                await Frames(1);
            }
        }
        await Burn(1.5);
        var lit = TerrainCollision.CollidesAt(_land, at);
        await Burn(TerrainCollision.ReleaseAfterS + 1);
        var kept = TerrainCollision.CollidesAt(_land, at) && TerrainCollision.CollidesAt(_land, at + new Vector3(1, 0, 0));
        var burning = host.Nature.Fire != null;
        host.Nature.Fire = null;
        await Seconds(TerrainCollision.ReleaseAfterS + 2.5);        Check(lit && kept && !TerrainCollision.CollidesAt(_land, at), $"a fire burning far from everyone keeps the ground under it while it burns, and lets it go when it is out (lit {lit}, kept {kept}, burning then {burning})");
    }

    /// <summary>A swimmer out past the open sea's wrap comes back in on the far side: nothing it arrives on is missing, and it never sinks under the land.</summary>
    private async Task WrapsRound()
    {
        var player = _land.Player;
        var sea = _land.Room.Sea!;
        if (!float.IsFinite(player.WrapM)) { Check(false, "the wide island has a wrap"); return; }
        var outward = new Vector3(0, 0, 1);
        var at = new Vector3(sea.OpenSeaCentreM.X, sea.LevelM - 0.03f, sea.OpenSeaCentreM.Y) + outward * (player.WrapM + 0.5f);
        var wraps = player.Wraps;
        player.GlobalPosition = at;
        player.Velocity = outward * 0.3f;
        player.ResetPhysicsInterpolation();
        float worst = 0;
        for (var tick = 0; tick < Engine.PhysicsTicksPerSecond; tick++)
        {
            await Frames(1);
            worst = Math.Max(worst, Footing(player).Below);
        }
        var landed = player.GlobalPosition;
        Check(player.Wraps > wraps && landed.Z < sea.OpenSeaCentreM.Y && worst < 0.005f && TerrainCollision.CollidesAt(_land, landed),
            $"a swimmer wrapped round the open sea comes in on the far side ({landed.X:0} m, {landed.Z:0} m), nothing it reaches missing and never under the land");
    }
}

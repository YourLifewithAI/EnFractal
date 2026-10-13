using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using EnFractal.Native;
using EnFractal.Native.Look;
using EnFractal.Native.Room;

namespace EnFractal.Tests;

/// <summary>
/// The 10x world's loading (packet S6): a synthetic island (the contract's island example with a 24,000-copy grass table written
/// here) booted as the game boots a room. The copies are drawn per 8 m tile in distance bands and built over several frames
/// within the frame budget, their collision near the spawns before the bodies arrive; the terrain is drawn one level per tile by
/// distance and collides through level 0 alone, with the importer's own faces; the trees collide as their rows say (a solid
/// trunk, a hidden pole, one-sided caps) and a stone as its hull; every hit names its copy; and the player climbs a tree built
/// from its row to its crown, as LandscapePlayTest climbs the land's trees. "-- --scale" also writes and boots an island with
/// the 10x garage's numbers of copies (about 350,000) and prints what loading and building them cost on this machine.
/// Measurements of an exported island (S5's 10x garage), instead of the checks: "-- --room=DIR" boots it and prints the load,
/// the copies' arrival and memory, and in a window (not --headless) the frame time at the spawn; "-- --room=DIR --phases" times
/// RoomWorld's boot steps one by one (the shell part by part as it enters the scene). The 1080p frame times of record are the
/// look harness's (tools/look/capture-look.ps1 -Room DIR with the island's cameras).
/// </summary>
public partial class IslandLoadingTest : Node3D
{
    private int _checks;
    private int _failures;

    public override async void _Ready()
    {
        try
        {
            // "-- --room=DIR": boot an exported island (S5's) and measure it, nothing else.
            if (OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--room=", StringComparison.Ordinal)) is { } exported)
            {
                if (OS.GetCmdlineUserArgs().Contains("--phases")) await MeasurePhases(exported["--room=".Length..]);
                else await MeasureBoot(exported["--room=".Length..], "ISLAND_ROOM");
                Finish();
                return;
            }
            var area = new Rect2(-16, -16, 32, 16);
            var grassRows = IslandFixture.GrassRows(24_000, area, seed: 7);
            var trees = new[] { IslandFixture.Broadleaf(-5, -9, 0, 1, 11), IslandFixture.Broadleaf(-9.5f, -3.5f, 0, 1, 12) };
            var stones = new[]
            {
                new IslandFixture.Row(4f, 0, -12f, -100f, new Vector3(1.5f, 1.2f, 1.4f), Colors.White, 0.13f * 1.2f, 0, 0, 21),
                new IslandFixture.Row(12.5f, 0, -3f, 10f, Vector3.One, new Color(0.95f, 0.9f, 0.85f), 0.13f, 1, 0, 22),
            };
            var directory = IslandFixture.Write("island_loading", new[] { IslandFixture.Grass("grass", grassRows), IslandFixture.Trees(trees), IslandFixture.Stones(stones) }, groups: 2);
            var clock = Stopwatch.StartNew();
            var room = await RoomData.LoadAsync(directory);
            Measure($"ISLAND_LOADING read and checked {room.Copies!.TotalCount} copies in {room.Copies.Sets.Sum(s => s.TileCount)} tiles off the main thread in {clock.Elapsed.TotalMilliseconds:0} ms");
            var grass = room.Copies.Find("grass")!;
            Check(grass.Count == 24_000 && grass.TileCount == 8 && room.Copies.Find("broadleaf")!.Count == 2 && room.Copies.Find("stones")!.Count == 2,
                "the synthetic island loads: 24,000 grass copies in its eight 8 m tiles, two trees, two stones");
            var ordered = IslandFixture.Table(PlantKind.Plant, 0, grassRows).Ordered;
            var probe = ordered.Length / 3;
            Check(room.Copies.TryResolve(PlantCopies.IdOf("grass", probe), out _, out var probeRow) && grass.Position(probeRow) == new Vector3(ordered[probe].X, 0, ordered[probe].Z) &&
                  grass.Tint(probeRow) == ordered[probe].Tint && Mathf.IsEqualApprox(grass.YawDeg(probeRow), ordered[probe].Yaw),
                "a written row reads back by its id: its place, yaw and tint");

            var world = GD.Load<PackedScene>("res://scenes/room.tscn").Instantiate<RoomWorld>();
            world.RoomDirectory = directory;
            AddChild(world);
            for (var i = 0; i < 300 && !world.WorldReady && world.LoadError.Length == 0; i++) await Frames(1);
            Check(world.WorldReady, "the synthetic island boots: " + world.LoadError);
            if (!world.WorldReady) { Finish(); return; }
            var view = world.Built.GetNode<PlantCopiesView>(PlantCopiesView.ViewName);
            Check(view.NearCollisionTiles == 4 && view.CollisionTiles == 4 && view.VisualTilesBuilt == 0,
                $"the copies' collision near the spawns was built before the bodies were placed ({view.NearCollisionTiles} of {view.CollisionTiles} tiles, {view.EnterMs:0.0} ms), and nothing drawn yet");
            await CheckBuiltPerFrame(view, room.Copies);
            CheckTerrain(world, room);
            await CheckCollision(world, room);
            await ClimbATree(world, room);
            CheckNavigationSources(room);
            world.QueueFree();
            await Frames(2);
            if (OS.GetCmdlineUserArgs().Contains("--scale")) await MeasureScale();
            Finish();
        }
        catch (Exception exception)
        {
            GD.PushError("Island loading test exception: " + exception);
            GetTree().Quit(1);
        }
    }

    private void Finish()
    {
        GD.Print($"NATIVE_ISLAND_LOADING: {_checks - _failures}/{_checks} checks passed; plant copies and terrain tiles read off the main thread, drawn per tile in bands, built per frame, collided from their rows, a tree climbed");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    /// <summary>The copies arrive over several frames, nearest first, each frame within the budget, and every row is drawn exactly once per band.</summary>
    private async Task CheckBuiltPerFrame(PlantCopiesView view, PlantCopies copies)
    {
        var grass = copies.Find("grass")!;
        var frames = 0;
        for (; frames < 600 && !view.Complete; frames++) await Frames(1);
        Measure($"ISLAND_LOADING copies built over {view.BuildFrames} frames ({frames} physics frames after the world was ready): {view.BuildMs:0.0} ms in all, longest frame {view.LongestFrameMs:0.00} ms, at most {view.MostTilesInAFrame} tiles in one frame");
        Check(view.Complete && view.VisualTilesBuilt == view.VisualTiles && view.VisualTiles == copies.Sets.Sum(s => s.TileCount) && view.CollisionTilesBuilt == view.CollisionTiles,
            $"every tile is drawn and collided ({view.VisualTilesBuilt} of {view.VisualTiles} drawn, {view.CollisionTilesBuilt} of {view.CollisionTiles} collided)");
        Check(view.BuildFrames >= 2 && view.MostTilesInAFrame < view.VisualTiles, "the copies are built over several frames, never all in one");
        var bands = DrawDistances.CopyBands(PlantKind.Plant, grass.MeshHeightM, 2);
        var contiguous = bands[0].BeginM == 0 && bands.Zip(bands.Skip(1)).All(p => p.First.EndM == p.Second.BeginM) && bands.All(b => b.CastsShadow == b.EndM <= DrawDistances.CopyShadowM);
        Check(contiguous && bands.Length == 3 && bands[^1].EndM == DrawDistances.PlantM && bands.Select(b => b.Mesh).SequenceEqual(new[] { 0, 1, 1 }),
            $"the grass's bands run from the camera to {DrawDistances.PlantM} m without a gap, full detail first, shadows only near: " + string.Join("; ", bands));
        var tile = view.TileNode("grass", -1, -2)!;
        var instances = tile.GetChildren().OfType<MultiMeshInstance3D>().ToArray();
        var (_, _, first, count) = grass.Tile(grass.FindTile(-1, -2));
        Check(instances.Length == bands.Length && instances.All(m => m.Multimesh.InstanceCount == count) &&
              instances.Select(m => (m.VisibilityRangeBegin, m.VisibilityRangeEnd, m.CastShadow == GeometryInstance3D.ShadowCastingSetting.On)).SequenceEqual(bands.Select(b => (b.BeginM, b.EndM, b.CastsShadow))),
            "a grass tile is one MultiMesh per band, each holding every copy of the tile, drawn only within its band");
        var middle = count / 2;
        var row = first + middle;
        var buffer = PlantCopiesView.InstanceBuffer(grass, first, count);
        var o = middle * 16;
        // Godot's buffer layout: each basis row followed by the origin's coordinate on that axis, then the colour.
        var fromBuffer = new Transform3D(new Basis(new Vector3(buffer[o], buffer[o + 4], buffer[o + 8]), new Vector3(buffer[o + 1], buffer[o + 5], buffer[o + 9]),
            new Vector3(buffer[o + 2], buffer[o + 6], buffer[o + 10])), new Vector3(buffer[o + 3], buffer[o + 7], buffer[o + 11]));
        var expected = new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(grass.YawDeg(row))) * Basis.FromScale(grass.Scale(row)), grass.Position(row));
        var tint = new Color(buffer[o + 12], buffer[o + 13], buffer[o + 14], buffer[o + 15]);
        Check(fromBuffer.IsEqualApprox(expected) && fromBuffer.IsEqualApprox(grass.Transform(row)) && tint.IsEqualApprox(grass.Tint(row)) &&
              fromBuffer.Origin == new Vector3(grass.Position(row).X, 0, grass.Position(row).Z),
            "an instance is its row: its place, its yaw and scale, its tint as the instance colour, in Godot's buffer layout");
        var yaw = Mathf.DegToRad(grass.YawDeg(row));
        Check((grass.Basis(row) * Vector3.Right).Normalized().IsEqualApprox(new Vector3(Mathf.Cos(yaw), 0, -Mathf.Sin(yaw))),
            "a row's yaw is Godot's rotation.y: at 90 degrees +X turns toward -Z");
        if (RenderingServer.GetRenderingDevice() != null)
        {
            // With a renderer (not under --headless) the server keeps the buffer: read the instance back as Godot sees it.
            var multimesh = instances[0].Multimesh;
            Check(multimesh.GetInstanceTransform(middle).IsEqualApprox(expected) && multimesh.GetInstanceColor(middle).IsEqualApprox(grass.Tint(row)),
                $"the renderer reads the instance back as its row ({multimesh.GetInstanceTransform(middle)}, {multimesh.GetInstanceColor(middle)})");
        }
        Check(instances[0].Multimesh.Mesh.SurfaceGetMaterial(0) != null && instances[1].Multimesh.Mesh != instances[0].Multimesh.Mesh,
            "the bands draw the set's own meshes, painted: the full one near, the lighter one further out");
    }

    /// <summary>Terrain tiles: each tile's levels drawn only within their bands, and collision from level 0 alone, the faces the importer itself would give.</summary>
    private void CheckTerrain(RoomWorld world, RoomData room)
    {
        var part = world.Built.GetNode<StaticBody3D>("Shell/terrain_grass");
        var meshes = part.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().ToArray();
        var bands = Enumerable.Range(0, 3).Select(level => DrawDistances.TerrainBand(level, 3)).ToArray();
        Check(meshes.Length == 6 && meshes.All(m => m.HasMeta(RoomBuilder.TerrainLevelMeta) &&
              (m.VisibilityRangeBegin, m.VisibilityRangeEnd) == bands[m.GetMeta(RoomBuilder.TerrainLevelMeta).AsInt32()]),
            "the grass's two tiles are drawn at three levels each, each level only within its distance band");
        Check(bands[0].BeginM == 0 && bands[0].EndM == bands[1].BeginM && bands[1].EndM == bands[2].BeginM && bands[2].EndM == 0,
            $"one terrain level per tile at every distance: {bands[0]}, {bands[1]}, {bands[2]}");
        var shapes = part.GetChildren().OfType<CollisionShape3D>().ToArray();
        var lod0 = meshes.Where(m => m.GetMeta(RoomBuilder.TerrainLevelMeta).AsInt32() == 0).OrderBy(m => m.Name.ToString()).ToArray();
        var imported = lod0.Select(m => m.Mesh.CreateTrimeshShape().GetFaces()).ToArray();
        var ours = shapes.OrderBy(s => s.Name.ToString()).Select(s => ((ConcavePolygonShape3D)s.Shape).Data).ToArray();
        // The importer may store positions compressed (a fraction of a millimetre over a 16 m tile); the corners' order is the winding.
        var worst = imported.Zip(ours).SelectMany(p => p.First.Zip(p.Second).Select(v => v.First.DistanceTo(v.Second))).DefaultIfEmpty(float.MaxValue).Max();
        var same = imported.Length == ours.Length && imported.Zip(ours).All(p => p.First.Length == p.Second.Length) && worst < 1e-3f;
        Measure($"ISLAND_TERRAIN level-0 collision against the importer's faces: worst corner {worst * 1000:0.000} mm apart");
        Check(shapes.Length == 2 && shapes.All(s => s.Shape is ConcavePolygonShape3D { BackfaceCollision: false } c && c.Data.Length == 32 * 3) && same,
            $"the terrain collides through level 0 alone, one one-sided shape per tile, with the importer's own faces in its winding ({string.Join(", ", ours.Select(o => o.Length / 3))} triangles)");
        Check(part.GetMeta(RoomBuilder.TerrainTilesMeta).AsInt32() == 2 && part.GetMeta("entity_id").AsString() == "shell:terrain_grass", "a tiled part stays one shell part with its id");
    }

    /// <summary>The trees and stones collide as their rows say, on their layers, and every hit names its copy.</summary>
    private async Task CheckCollision(RoomWorld world, RoomData room)
    {
        await Frames(2);
        var trees = room.Copies!.Find("broadleaf")!;
        var tree = Enumerable.Range(0, trees.Count).First(r => trees.Position(r).IsEqualApprox(new Vector3(-5, 0, -9)));
        var id = PlantCopies.IdOf("broadleaf", tree);
        var side = Ray(new Vector3(-4.5f, 0.2f, -9), new Vector3(-5f, 0.2f, -9), RoomBuilder.WorldLayer);
        var radius = Mathf.Lerp(0.055f, 0.035f, 0.2f / 0.53f);
        Check(side.Count > 0 && Mathf.Abs(side["position"].AsVector3().X - (-5 + radius)) < 0.003f && Entity(side) == id,
            $"the trunk is solid on the world layer, as wide as its row says at that height, and names its tree ({Entity(side)} at {(side.Count > 0 ? side["position"].AsVector3().X : 0):0.000})");
        var crown = Ray(new Vector3(-5, 2, -9), new Vector3(-5, 0.5f, -9), RoomBuilder.HiddenLayer);
        Check(crown.Count > 0 && Mathf.Abs(crown["position"].AsVector3().Y - 0.94f) < 0.002f && Entity(crown) == id && crown["normal"].AsVector3().Y > 0.99f,
            "the cap's perch is on the hidden layer, flat at its row's 0.94 m, and names its tree");
        var sight = Ray(new Vector3(-5, 2, -9), new Vector3(-5, 0.3f, -9), RoomBuilder.WorldLayer);
        Check(sight.Count > 0 && Mathf.Abs(sight["position"].AsVector3().Y - 0.53f) < 0.002f,
            "the world layer sees through the hidden pole and caps to the trunk's top at 0.53 m, as sight and the cameras do");
        var fromInside = Ray(new Vector3(-4.92f, 0.85f, -9), new Vector3(-4.92f, 1.2f, -9), RoomBuilder.HiddenLayer);
        var fromOutside = Ray(new Vector3(-4.8f, 0.83f, -9.0f), new Vector3(-4.95f, 0.83f, -9.0f), RoomBuilder.HiddenLayer);
        Check(fromInside.Count == 0 && fromOutside.Count > 0 && Entity(fromOutside) == id,
            "a cap is one-sided: up through it from inside nothing stops a climber, while its collar is there from outside");
        var pole = Ray(new Vector3(-4.8f, 0.7f, -9), new Vector3(-5f, 0.7f, -9), RoomBuilder.HiddenLayer);
        Check(pole.Count > 0 && Mathf.Abs(pole["position"].AsVector3().X - (-5 + 0.035f)) < 0.003f && Entity(pole) == id, "the hidden pole stands between the trunk's top and the cap, as thin as its row says");
        var stones = room.Copies.Find("stones")!;
        var stone = Enumerable.Range(0, stones.Count).First(r => stones.Position(r).X == 4f);
        var top = Ray(new Vector3(4, 1, -12), new Vector3(4, -0.5f, -12), RoomBuilder.WorldLayer);
        Check(top.Count > 0 && Mathf.Abs(top["position"].AsVector3().Y - stones.TopM(stone)) < 0.003f && Entity(top) == PlantCopies.IdOf("stones", stone),
            $"a solid stone collides as its hull, its top at its row's top_m ({stones.TopM(stone):0.000} m), and names its copy");
        var holder = top.Count > 0 ? top["collider"].AsGodotObject() as Node : null;
        Check(holder != null && holder.GetMeta("material_role").AsString() == "rock" && !holder.HasMeta("entity_id"),
            "a copy's collider says what it is made of for nature's rules and is no room entity (no entity_id)");
        // The copies' bodies leave the physics space with their view and come back with it.
        var view = world.Built.GetNode<PlantCopiesView>(PlantCopiesView.ViewName);
        var parent = view.GetParent();
        parent.RemoveChild(view);
        await Frames(2);
        var whileOut = Ray(new Vector3(4, 1, -12), new Vector3(4, -0.5f, -12), RoomBuilder.WorldLayer);
        parent.AddChild(view);
        await Frames(2);
        var backIn = Ray(new Vector3(4, 1, -12), new Vector3(4, -0.5f, -12), RoomBuilder.WorldLayer);
        Check(whileOut.Count > 0 && Entity(whileOut) == "" && Entity(backIn) == PlantCopies.IdOf("stones", stone),
            "the copies' collision leaves the world with their view and comes back with it");
        var tuft = room.Copies.Find("grass")!.Position(0);
        var grass = Ray(tuft + Vector3.Up, tuft + Vector3.Down * 0.5f, RoomBuilder.WorldLayer | RoomBuilder.HiddenLayer);
        Check(grass.Count > 0 && Entity(grass) == "" && Mathf.Abs(grass["position"].AsVector3().Y) < 0.002f, "grass never collides: the ray goes through to the ground");
    }

    /// <summary>
    /// The player climbs a tree whose collision is built from its row, as LandscapePlayTest climbs the land's trees: walks into
    /// the trunk, grabs it, climbs the trunk and the hidden pole with W held, up through the one-sided cap, pulls over and stands
    /// on its perch, never carried down while W is held.
    /// </summary>
    private async Task ClimbATree(RoomWorld world, RoomData room)
    {
        var player = world.Player;
        var trees = room.Copies!.Find("broadleaf")!;
        var tree = Enumerable.Range(0, trees.Count).First(r => trees.Position(r).IsEqualApprox(new Vector3(-5, 0, -9)));
        player.ReadKeyboard = false;
        Check(player.TryTeleportTo(new Vector3(-4.85f, 0.005f, -9)), "the player at the foot of the tree");
        player.Rotation = new Vector3(0, Mathf.Atan2(1, 0), 0);
        await Frames(10);
        var start = player.GlobalPosition;
        var grabs = player.Grabs;
        var pulls = player.PullOvers;
        var downTicks = 0;
        var lastY = start.Y;
        var frames = 0;
        player.SetControlInput(new Vector2(0, 1));
        for (; frames < 1500 && !(player.PullOvers > pulls && player.IsOnFloor() && !player.IsClimbing); frames++)
        {
            await Frames(1);
            if (player.IsClimbing && !player.IsPullingOver && player.GlobalPosition.Y < lastY - 0.0005f) downTicks++;
            lastY = player.GlobalPosition.Y;
        }
        player.SetControlInput(Vector2.Zero);
        await Frames(30);
        var under = Ray(player.GlobalPosition + Vector3.Up * 0.02f, player.GlobalPosition + Vector3.Down * 0.03f, RoomBuilder.BodyMask, player.GetRid());
        var standingOn = Entity(under);
        Measure($"ISLAND_TREE climbed {(standingOn == PlantCopies.IdOf("broadleaf", tree) ? "to its crown" : "short of its crown")}: grabbed {player.Grabs - grabs}, pulled over {player.PullOvers - pulls}, " +
                $"rose {(player.GlobalPosition.Y - start.Y) * 100:0.0} cm in {frames / 60.0:0.0} s, carried down {downTicks} ticks while W was held, standing on {(standingOn.Length > 0 ? standingOn : "nothing")}");
        Check(player.Grabs > grabs && player.PullOvers > pulls && player.IsOnFloor() && standingOn == PlantCopies.IdOf("broadleaf", tree) && player.GlobalPosition.Y > 0.93f && downTicks == 0,
            "the player climbs a tree built from its row to its crown and stands on its perch, never carried down while W is held");
        player.TryTeleportTo(room.SpawnFor("player").PositionM);
        await Frames(5);
    }

    /// <summary>What navigation reads (IslandNavigation): the level-0 ground faces, and the trunks and stones as obstructions.</summary>
    private void CheckNavigationSources(RoomData room)
    {
        var all = new Rect2(-16, -16, 32, 16);
        var ground = IslandNavigation.GroundFaces(room, all);
        Check(ground.Length == (2 * 32 + 2) * 3, $"navigation's ground is every colliding tiled part's level 0 ({ground.Length / 3} triangles: 2 x 32 grass, 2 sand)");
        var obstacles = IslandNavigation.Obstacles(room, all);
        var trunk = obstacles.FirstOrDefault(o => o.EntityId.StartsWith("plant:broadleaf-", StringComparison.Ordinal));
        Check(obstacles.Count == 4 && trunk != null && trunk.Outline.Length == PlantCollision.Segments && Mathf.IsEqualApprox(trunk.HeightM, 0.53f) &&
              obstacles.Count(o => o.EntityId.StartsWith("plant:stones-", StringComparison.Ordinal)) == 2,
            $"navigation's obstacles are the two trunks and the two stones ({string.Join(", ", obstacles.Select(o => $"{o.EntityId} {o.Outline.Length} corners {o.HeightM:0.000} m"))})");
        Check(IslandNavigation.Obstacles(room, new Rect2(10, -6, 4, 4)).Select(o => o.EntityId).SequenceEqual(new[] { "plant:stones-1" }),
            "an area holds only the obstacles that reach into it");
        var source = new NavigationMeshSourceGeometryData3D();
        IslandNavigation.AddTo(room, source, all);
        Check(source.GetVertices().Length == ground.Length * 3 && source.GetProjectedObstructions().Count == 4, "AddTo puts the ground and the obstructions into a bake's source geometry");
    }

    /// <summary>
    /// An island with the 10x garage's numbers of copies (docs/runs/RUN-2-TEN-TIMES.md: about 350,000 plants and stones, 4,900
    /// trees) on a 64 x 72 m footprint, booted headless: what reading and checking the tables, entering the scene and building
    /// every tile cost the main thread here. Headless renders nothing, so this is the CPU's share only; frame times need the GPU.
    /// </summary>
    private async Task MeasureScale()
    {
        var area = new Rect2(-32, -36, 64, 72);
        var random = new Random(3);
        var trees = new List<IslandFixture.Row>();
        while (trees.Count < 4_900) trees.Add(IslandFixture.Broadleaf(area.Position.X + 0.5f + (float)random.NextDouble() * (area.Size.X - 1), area.Position.Y + 0.5f + (float)random.NextDouble() * (area.Size.Y - 1), 0, 1, (uint)random.Next()));
        var stones = new List<IslandFixture.Row>();
        while (stones.Count < 3_000) stones.Add(new IslandFixture.Row(area.Position.X + (float)random.NextDouble() * area.Size.X, 0, area.Position.Y + (float)random.NextDouble() * area.Size.Y,
            -180f + 360f * (float)random.NextDouble(), new Vector3(1.2f, 1f, 1.1f), Colors.White, 0.13f, 0, 0, (uint)random.Next()));
        var clock = Stopwatch.StartNew();
        var directory = IslandFixture.Write("island_scale", new[]
        {
            IslandFixture.Grass("grass", IslandFixture.GrassRows(250_000, area, 4)), IslandFixture.Grass("daisies", IslandFixture.GrassRows(45_000, area, 5)),
            IslandFixture.Grass("heather", IslandFixture.GrassRows(45_000, area, 6)), IslandFixture.Trees(trees), IslandFixture.Stones(stones),
        }, groups: 2, new Vector3(-32, -1, -36), new Vector3(32, 6, 36));
        Measure($"ISLAND_SCALE wrote the island in {clock.Elapsed.TotalMilliseconds:0} ms");
        await MeasureBoot(directory, "ISLAND_SCALE");
    }

    /// <summary>The boot's steps as RoomWorld takes them, each timed (the room's own pieces only: no players, HUD, kernel or navigation).</summary>
    private async Task MeasurePhases(string directory)
    {
        var clock = Stopwatch.StartNew();
        var lines = new List<string>();
        void Lap(string what) { lines.Add($"{what} {clock.Elapsed.TotalMilliseconds:0} ms"); clock.Restart(); }
        var room = RoomData.Load(directory);
        Lap("RoomData.Load on the main thread");
        var look = new LookDirector { Name = "Look" };
        var holder = new Node3D { Name = "Phases" };
        AddChild(holder);
        holder.AddChild(look);
        look.Apply(StylePreset.Resolve(RoomWorld.DefaultStyleId, RoomWorld.DefaultStyleVersion), room);
        Lap("Look.Apply");
        var built = RoomBuilder.Build(room);
        Lap("RoomBuilder.Build");
        var parts = built.GetChildren().OfType<Node>().ToArray();
        foreach (var part in parts) built.RemoveChild(part);
        holder.AddChild(built);
        foreach (var part in parts)
        {
            if (part.Name == "Shell")
            {
                // The shell part by part: what each costs entering the scene (Jolt builds a body's shapes as it enters).
                var shellParts = part.GetChildren().OfType<Node>().ToArray();
                foreach (var shellPart in shellParts) part.RemoveChild(shellPart);
                built.AddChild(part);
                var costs = new List<(double Ms, string Name, int Triangles)>();
                foreach (var shellPart in shellParts)
                {
                    var partClock = Stopwatch.StartNew();
                    part.AddChild(shellPart);
                    var triangles = shellPart.GetChildren().OfType<CollisionShape3D>().Sum(c => c.Shape is ConcavePolygonShape3D concave ? concave.Data.Length / 3 : 0);
                    costs.Add((partClock.Elapsed.TotalMilliseconds, shellPart.Name, triangles));
                }
                Lap("the shell into the scene (" + string.Join(", ", costs.OrderByDescending(c => c.Ms).Take(6).Select(c => $"{c.Name} {c.Ms:0} ms for {c.Triangles} collision triangles")) + ")");
                continue;
            }
            built.AddChild(part);
            Lap($"{part.Name} into the scene");
        }
        look.Dress(built);
        Lap("Look.Dress");
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        Lap("the next physics frame");
        Measure("ISLAND_PHASES " + string.Join("; ", lines));
        holder.QueueFree();
        await Frames(2);
    }

    /// <summary>
    /// Boot a room as the game does and measure: reading and checking it off the main thread, the boot to ROOM_WORLD_READY (the
    /// synchronous part, which today reads the room again and bakes navigation), the copies' arrival, memory, and with a renderer
    /// (not headless) the frame time at the spawn, vsync off, once everything is built.
    /// </summary>
    private async Task MeasureBoot(string directory, string label)
    {
        var clock = Stopwatch.StartNew();
        var room = await RoomData.LoadAsync(directory);
        var loadMs = clock.Elapsed.TotalMilliseconds;
        var world = GD.Load<PackedScene>("res://scenes/room.tscn").Instantiate<RoomWorld>();
        world.RoomDirectory = directory;
        clock.Restart();
        AddChild(world);
        var frames = 0;
        for (; frames < 600 && !world.WorldReady && world.LoadError.Length == 0; frames++) await Frames(1);
        var readyMs = clock.Elapsed.TotalMilliseconds;
        Check(world.WorldReady, $"{label}: the island boots: " + world.LoadError);
        if (!world.WorldReady) return;
        var view = world.Built.GetNodeOrNull<PlantCopiesView>(PlantCopiesView.ViewName);
        for (frames = 0; frames < 3600 && view is { Complete: false }; frames++) await Frames(1);
        var builtMs = clock.Elapsed.TotalMilliseconds;
        using var process = Process.GetCurrentProcess();
        Measure($"{label} {room.Copies?.TotalCount ?? 0} copies in {room.Copies?.Sets.Sum(s => s.TileCount) ?? 0} set tiles, {room.Terrain?.Parts.Sum(p => p.Tiles.Count) ?? 0} terrain tiles: RoomData.LoadAsync {loadMs:0} ms off the main thread (the boot reads it again), " +
                $"boot to ROOM_WORLD_READY {readyMs:0} ms (copies' collision near the spawns {view?.EnterMs ?? 0:0} ms of it), every tile built {builtMs - readyMs:0} ms later over {view?.BuildFrames ?? 0} frames: " +
                $"{view?.BuildMs ?? 0:0} ms of main-thread work, longest frame {view?.LongestFrameMs ?? 0:0.0} ms (budget {DrawDistances.BuildBudgetMs} ms), {view?.VisualTiles ?? 0} drawn tiles, {view?.CollisionTiles ?? 0} collided; " +
                $"memory {process.WorkingSet64 / 1048576.0:0} MB working set ({process.PrivateMemorySize64 / 1048576.0:0} MB private, the engine's {OS.GetStaticMemoryUsage() / 1048576.0:0} MB, C#'s {GC.GetTotalMemory(false) / 1048576.0:0} MB), {process.PeakWorkingSet64 / 1048576.0:0} MB at its peak; video memory {Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / 1048576.0:0} MB");
        Check(view == null || (view.Complete && view.LongestFrameMs < 50), $"{label}: every tile is built, no frame stalled for long");
        if (DisplayServer.GetName() != "headless" && view != null && room.Copies is { } copies)
        {
            // With a renderer the server keeps the MultiMesh buffers: an instance read back is its row.
            var set = copies.Sets[0];
            var (tx, tz, first, _) = set.Tile(0);
            var band = view.TileNode(set.Name, tx, tz)?.GetChildren().OfType<MultiMeshInstance3D>().FirstOrDefault();
            Check(band != null && band.Multimesh.GetInstanceTransform(0).IsEqualApprox(set.Transform(first)) && band.Multimesh.GetInstanceColor(0).IsEqualApprox(set.Tint(first)),
                $"{label}: the renderer reads a copy back as its row ({band?.Multimesh.GetInstanceTransform(0)} against {set.Transform(first)})");
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            Engine.MaxFps = 0;
            var viewport = GetViewport().GetViewportRid();
            RenderingServer.ViewportSetMeasureRenderTime(viewport, true);
            var times = new List<double>();
            var gpu = new List<double>();
            var tick = Stopwatch.StartNew();
            for (var i = 0; i < 420; i++)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                if (i >= 120)
                {
                    times.Add(tick.Elapsed.TotalMilliseconds);
                    gpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport));
                }
                tick.Restart();
            }
            times.Sort();
            Measure($"{label}_FRAMES at the spawn, the player's view, {GetViewport().GetVisibleRect().Size}: median {times[times.Count / 2]:0.00} ms, 95th {times[(int)(times.Count * 0.95)]:0.00} ms, worst {times[^1]:0.00} ms over {times.Count} frames; GPU {gpu.Average():0.00} ms mean; " +
                    $"video memory {Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / 1048576.0:0} MB, draw calls {Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame):0}, primitives {Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame) / 1e6:0.00} M");
        }
        world.QueueFree();
        await Frames(2);
    }

    private Godot.Collections.Dictionary Ray(Vector3 from, Vector3 to, uint mask, Rid? exclude = null)
    {
        var query = PhysicsRayQueryParameters3D.Create(from, to, mask);
        if (exclude is { } rid) query.Exclude = new Godot.Collections.Array<Rid> { rid };
        return GetWorld3D().DirectSpaceState.IntersectRay(query);
    }

    private static string Entity(Godot.Collections.Dictionary hit) =>
        hit.Count > 0 ? PlantCopiesView.EntityAt(hit["collider"].AsGodotObject(), hit["shape"].AsInt32()) : "";

    private async Task Frames(int count)
    {
        for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private static void Measure(string line) => GD.Print(line);

    private void Check(bool condition, string label)
    {
        _checks++;
        if (condition) return;
        _failures++;
        GD.PushError("Island loading: " + label);
    }
}

using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using EnFractal.Native;
using EnFractal.Native.Look;
using EnFractal.Native.Room;

namespace EnFractal.Tests.Look;

/// <summary>
/// S9, the look at 10x: how far you see by rule (the far plane, the aerial haze, the sun's shadows over the island, trees'
/// shadows), the 1x island and the rooms kept as they were, and the look suite's island cameras worked out from the island's data.
/// </summary>
public partial class LookPresetTest
{
    private async Task CheckTenTimes(StylePreset preset, RoomData testRoom)
    {
        CheckViewRules(testRoom);
        CheckTreeShadows();
        await CheckBigOpenLand(preset);
        await CheckRoomKeepsItsView(preset, testRoom);
    }

    /// <summary>The rules alone: the far plane, the shadows and the fog for islands of every size, and the 1x island's numbers unchanged.</summary>
    private void CheckViewRules(RoomData testRoom)
    {
        var small = ViewDistances.FarPlaneForSpan(11f);
        var big = ViewDistances.FarPlaneForSpan(102f);
        Check(ViewDistances.FarPlaneM(testRoom) == 100f && small == 100f && big == ViewDistances.MaxFarM && ViewDistances.MaxFarM == 250f,
            $"the far plane by rule: a room and the 1x island keep 100 m, the 10x island (102 m across) sees {big} m, the plan's 250 m");
        var rising = true;
        for (var span = 0f; span < 400f; span += 0.5f) rising &= ViewDistances.FarPlaneForSpan(span + 0.5f) >= ViewDistances.FarPlaneForSpan(span);
        Check(rising && ViewDistances.FarPlaneForSpan(float.NaN) == 100f, "a bigger island never sees less far, and a broken span falls back to 100 m");

        var before = ViewDistances.OpenLandShadows(11f);
        var after = ViewDistances.OpenLandShadows(102f);
        Check(before == new LandShadows(OpenLandLight.ShadowDistanceM, 2, OpenLandLight.ShadowSplit1, 0f, 0f) && before.DistanceM == 30f && before.Split1 == 0.25f,
            "the 1x island's sun keeps its shadows: 30 m in two splits, the first over a quarter");
        Check(after.DistanceM == 60f && after.Splits == 4 && after.Split1 < after.Split2 && after.Split2 < after.Split3 && after.Split3 < 1f
              && after.DistanceM * after.Split1 <= before.DistanceM * before.Split1,
            $"the 10x island's sun casts shadows over {after.DistanceM} m in four splits, the nearest ({after.DistanceM * after.Split1:0.#} m) as sharp as before or sharper");

        // The fog: off in a room until there is mist; on open land the haze; the mist closes in from it.
        var room = ViewDistances.Fog(0f, 100f, haze: false);
        Check(!room.Enabled && ViewDistances.FogAt(room, 99f) == 0f, "without haze and mist there is no fog");
        var haze = ViewDistances.Fog(0f, big, haze: true);
        Check(haze.Enabled && haze.BeginM == ViewDistances.HazeStartM && haze.EndM == big && haze.Density == ViewDistances.HazeMax && haze.Density < 1f,
            $"open land's haze begins at {haze.BeginM} m and reaches {haze.Density} of the horizon's colour at the far plane ({haze.EndM} m)");
        var reach = ViewDistances.FogAt(haze, 30f);
        var farShore = ViewDistances.FogAt(haze, 100f);
        Check(reach == 0f && ViewDistances.FogAt(ViewDistances.Fog(0f, small, true), 12f) == 0f && farShore is > 0.05f and < 0.35f,
            $"nothing within 40 m is hazed (the 1x island lies wholly inside it); the 10x island's far shore 100 m off takes {farShore:0.##}: a soft haze, not a fog");
        var full = ViewDistances.Fog(1f, big, haze: true);
        Check(full.Enabled && Mathf.IsEqualApprox(full.BeginM, OpenSea.MistNearM) && Mathf.IsEqualApprox(full.EndM, OpenSea.MistFullM) && full.Density == 1f && full.Curve == 1f,
            "at full mist the haze has become the sea mist: clear to 2 m, wholly mist past 8 m");
        var closing = true;
        var last = haze;
        for (var mist = 0.01f; mist <= 1f; mist += 0.01f)
        {
            var fog = ViewDistances.Fog(mist, big, haze: true);
            foreach (var d in new[] { 3f, 8f, 20f, 60f, 150f })
                closing &= ViewDistances.FogAt(fog, d) >= ViewDistances.FogAt(last, d) - 1e-4f && ViewDistances.FogAt(fog, d) - ViewDistances.FogAt(last, d) < 0.2f;
            closing &= fog.BeginM <= last.BeginM && fog.EndM <= last.EndM;
            last = fog;
        }
        Check(closing, "swimming out, the haze thickens into the mist a little at a time at every distance (never thinner, no jump)");
        var mistOnly = ViewDistances.Fog(1f, 100f, haze: false);
        var (mistBegin, mistEnd) = OpenSea.MistFog(1f, 100f);
        Check(mistOnly.Enabled && mistOnly.BeginM == mistBegin && mistOnly.EndM == mistEnd && mistOnly.Density == 1f, "without haze the mist is OpenSea's, as before");
    }

    /// <summary>Trees cast shadows across the island's shadow reach; plants and stones keep theirs near (the copies' bands, S6's test pins the plants').</summary>
    private void CheckTreeShadows()
    {
        var tree = DrawDistances.CopyBands(PlantKind.Tree, 1.2f, 4);
        var grass = DrawDistances.CopyBands(PlantKind.Plant, 0.05f, 4);
        var contiguous = tree.Zip(tree.Skip(1)).All(p => p.First.EndM == p.Second.BeginM) && tree[0].BeginM == 0f && tree[^1].EndM == DrawDistances.TreeM;
        Check(contiguous && tree.All(b => b.CastsShadow == b.EndM <= DrawDistances.TreeShadowM) && tree.Any(b => b.CastsShadow && b.EndM == ViewDistances.MaxShadowM),
            "a tree's bands run from the camera to 150 m without a gap, casting shadows to the island's 60 m shadow reach: " + string.Join("; ", tree));
        Check(grass.All(b => b.CastsShadow == b.EndM <= DrawDistances.CopyShadowM) && DrawDistances.CopyShadowRangeM(PlantKind.Stone) == DrawDistances.CopyShadowM,
            "plants and stones still cast shadows only within 20 m");
    }

    /// <summary>A big open land (the test room's floor 25 times wider, no sea): the far plane, the haze and the shadows the rules give it, and the game's camera kept at that far plane.</summary>
    private async Task CheckBigOpenLand(StylePreset preset)
    {
        var directory = LookFixtureRooms.Write("big_open_land", room =>
        {
            var parts = room["shell"]!["parts"]!.AsArray();
            foreach (var part in parts.Where(p => p!["role"]!.GetValue<string>() != "floor").ToArray()) parts.Remove(part);
            parts[0]!["role"] = "ground";
            foreach (var point in parts[0]!["geometry"]!["points_m"]!.AsArray())
            {
                point![0] = point[0]!.GetValue<double>() * 25;
                point[2] = point[2]!.GetValue<double>() * 25;
            }
            foreach (var corner in new[] { "min_m", "max_m" })
            {
                var c = room["bounds"]![corner]!.AsArray();
                c[0] = c[0]!.GetValue<double>() * 25;
                c[2] = c[2]!.GetValue<double>() * 25;
            }
            room["shell"]!["openings"] = new JsonArray();
            LookFixtureRooms.RemoveSunHint(room);
            var hints = LookFixtureRooms.Hints(room);
            foreach (var hint in hints.ToArray()) hints.Remove(hint);
        });
        var land = RoomData.Load(directory);
        var (holder, look) = NewDirector(preset, land, "BigOpenLandHolder");
        look.SetClock(12f, 172);
        var span = ViewDistances.SpanM(land);
        Check(look.OpenLand && span > 100f && look.FarPlaneM == 250f, $"a {span:0} m open land sees to {look.FarPlaneM} m");
        Check(look.Key.DirectionalShadowMaxDistance == 60f && look.Key.DirectionalShadowMode == DirectionalLight3D.ShadowMode.Parallel4Splits
              && Mathf.IsEqualApprox(look.Key.DirectionalShadowSplit1, ViewDistances.FourSplit1) && Mathf.IsEqualApprox(look.Key.DirectionalShadowSplit3, ViewDistances.FourSplit3),
            $"its sun casts shadows over {look.Key.DirectionalShadowMaxDistance} m in four splits");
        var env = look.Environment;
        Check(env.FogEnabled && env.FogMode == Godot.Environment.FogModeEnum.Depth && Mathf.IsEqualApprox(env.FogDepthBegin, ViewDistances.HazeStartM)
              && Mathf.IsEqualApprox(env.FogDepthEnd, 250f) && Mathf.IsEqualApprox(env.FogDensity, ViewDistances.HazeMax) && env.FogSkyAffect == 0f && env.FogLightEnergy > 0f,
            "with no sea the look draws the haze itself: from 40 m to the far plane, in the horizon's colour, never on the sky");
        // The game's camera (a HUD view made at 100 m) is kept at the far plane; a camera the harness framed keeps its own.
        var camera = new Camera3D { Far = 100f };
        holder.AddChild(camera);
        camera.MakeCurrent();
        var framed = new Camera3D { Far = 50f };
        holder.AddChild(framed);
        look.FrameCamera(framed, Vector3.Zero);
        await Frames(2);
        Check(camera.Far == 250f && framed.Far == 50f, $"the current camera sees to the far plane ({camera.Far} m); a framed review camera keeps its own ({framed.Far} m)");
        camera.ClearCurrent(false);
        holder.QueueFree();
        await Frames(1);
    }

    /// <summary>The test room keeps its view: 100 m, no fog, its shadows as the preset sets them.</summary>
    private async Task CheckRoomKeepsItsView(StylePreset preset, RoomData testRoom)
    {
        var (holder, look) = NewDirector(preset, testRoom, "RoomViewHolder");
        var s = preset.Tuning.Shadows;
        Check(look.FarPlaneM == 100f && !look.Environment.FogEnabled
              && Mathf.IsEqualApprox(look.Key.DirectionalShadowSplit1, s.KeySplit1)
              && Mathf.IsEqualApprox(look.Key.DirectionalShadowMaxDistance, Mathf.Max(s.KeyMinDistanceM, testRoom.Bounds.Size.Length() * s.KeyDistancePerDiagonal)),
            "the test room keeps its view: a 100 m far plane, no haze, its shadows as the preset sets them");
        holder.QueueFree();
        await Frames(1);
    }

    /// <summary>The look suite's island cameras worked out from the 1x garage's data: each stands on land where the data says.</summary>
    private void CheckIslandCameras(RoomData land, Node3D built)
    {
        var space = built.GetWorld3D().DirectSpaceState;
        var ground = IslandCameras.RayGround(space, land.Bounds);
        var notes = new List<string>();
        var cameras = IslandCameras.Derive(land, ground, IslandCameras.RaySky(space), notes);
        GD.Print("LOOK_INFO: the 1x garage's island cameras: " + string.Join("; ", cameras.Select(c => $"{c.Id} at {c.PlayerM} facing {c.YawDeg:0} degrees"))
                 + (notes.Count > 0 ? "; notes: " + string.Join("; ", notes) : ""));
        Check(cameras.Select(c => c.Id).SequenceEqual(IslandCameras.Ids), "the 1x garage has every island camera: " + string.Join(", ", cameras.Select(c => c.Id)));
        DerivedCamera Get(string id) => cameras.First(c => c.Id == id);
        var spawn = land.SpawnFor("player").PositionM;
        var middle = IslandCameras.Middle(land);
        Check(Get("spawn").PlayerM == spawn && Mathf.Abs(Mathf.AngleDifference(Mathf.DegToRad(Get("spawn").YawDeg), Mathf.Atan2(-(middle.X - spawn.X), -(middle.Y - spawn.Z)))) < 0.01f,
            "the spawn camera stands at the player's spawn, facing the island's middle");
        var samples = IslandCameras.SampleLand(land, ground);
        var summit = Get("summit").PlayerM;
        Check(samples.Count > 500 && samples.Count(s => s.Y > summit.Y + 1e-4f) < 0.05f * samples.Count && IslandCameras.RaySky(space)(summit) && IslandCameras.OnLand(land, new Vector2(summit.X, summit.Z)),
            $"the summit camera stands on the island's high ground ({summit.Y:0.###} m; {samples.Count(s => s.Y > summit.Y + 1e-4f)} of {samples.Count} ground samples higher) under open sky");
        var structures = land.Objects.Where(o => o.Asset.CategoryGroup == "structure").Select(o => new Vector2(o.PositionM.X, o.PositionM.Z)).ToArray();
        var hamlet = new Vector2(Get("hamlet").PlayerM.X, Get("hamlet").PlayerM.Z);
        Check(structures.Length >= 2 && structures.Count(s => s.DistanceTo(hamlet) < IslandCameras.StructureClusterM + IslandCameras.HamletStandOffM) >= 2 && ground(hamlet) != null,
            $"the hamlet camera stands on the ground beside the cottages ({structures.Count(s => s.DistanceTo(hamlet) < 2.5f)} within 2.5 m)");
        var beach = land.Sea!.NearestBeach(new Vector2(spawn.X, spawn.Z))!.Value;
        Check(new Vector2(Get("beach").PlayerM.X, Get("beach").PlayerM.Z).DistanceTo(new Vector2(beach.WashAshoreM.X, beach.WashAshoreM.Z)) < 0.01f,
            $"the beach camera stands on the beach nearest the spawn ({beach.Id})");
        var flight = Get("flight").Flight!.Value;
        Check(flight.FromM.Y > summit.Y && flight.FromM.DistanceTo(flight.ToM) > 1f, "the flight crosses the island above its summit");
    }
}

using Godot;
using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EnFractal.Native;
using EnFractal.Native.Look;
using EnFractal.Native.Room;

namespace EnFractal.Tests.Look;

/// <summary>
/// Run 2, the open sea round (Lane L): each view's blur (the island crisp in F1 to F3, the tilt-shift kept in F4 and observe), the
/// endless sea's surface and bed past the room's meshes, the distant islands that hold their place on the horizon, and the focus
/// highlight's API.
/// </summary>
public partial class LookPresetTest
{
    /// <summary>
    /// The founder, playing the island garage in F2: "players are going to want to see the landscape a little further out". v1 (locked)
    /// blurs every view as it always did; v2 keeps F1 to F3 crisp across the island and softens only what is far.
    /// </summary>
    private void CheckViewBlur(StylePreset v1, StylePreset v2)
    {
        Check(Enum.GetValues<LookView>().All(v => !v1.Tuning.BlurFor(v).Far) && v1.Tuning.Defaulted.Contains("x_look_views"),
            "v1, written before x_look_views, blurs every view with its own miniature depth of field (left to the code's defaults, which reproduce it)");
        Check(v2.Tuning.BlurFor(LookView.Eye).Far && v2.Tuning.BlurFor(LookView.Shoulder).Far && v2.Tuning.BlurFor(LookView.Diorama).Far && !v2.Tuning.BlurFor(LookView.Isometric).Far,
            "v2: F1 eye, F2 shoulder and F3 diorama soften only what is far; F4 isometric keeps the tilt-shift");
        // F2 as the founder played it: the player 0.32 m ahead, the arm looking about 10 degrees down.
        var down = Mathf.Sin(Mathf.DegToRad(10f));
        var before = LookDirector.DepthOfFieldFor(v2, 0.32f, down);
        var after = LookDirector.DepthOfFieldFor(v2, 0.32f, down, view: v2.Tuning.BlurFor(LookView.Shoulder));
        GD.Print($"LOOK_INFO: F2 depth of field before: crisp {before.NearDistance:0.###} to {before.FarDistance:0.###} m, fully soft from {before.FarDistance + before.FarTransition:0.##} m, amount {before.Amount:0.###}; after: crisp {after.NearDistance:0.###} to {after.FarDistance:0.##} m, fully soft from {after.FarDistance + after.FarTransition:0.#} m, amount {after.Amount:0.###}");
        Check(before.FarDistance + before.FarTransition < 1.2f, $"before, in F2 the island a metre away was already fully soft ({before.FarDistance + before.FarTransition:0.##} m)");
        Check(!after.NearEnabled && after.FarDistance >= 3f && after.FarDistance + after.FarTransition >= 15f, $"after, nothing near is soft and the island is crisp to {after.FarDistance:0.#} m and only the far shore, the distant islands and the horizon soften (fully from {after.FarDistance + after.FarTransition:0.#} m)");
        Check(after.Amount * after.Amount <= before.Amount * before.Amount, $"and the far blur costs no more than before (bokeh amount {after.Amount:0.###} against {before.Amount:0.###})");
        foreach (var view in new[] { LookView.Eye, LookView.Shoulder, LookView.Diorama })
            foreach (var distance in new[] { 0.06f, 0.3f, 1.4f, 2.4f })
                foreach (var pitch in new[] { 0f, 20f, 45f, 80f })
                {
                    var dof = LookDirector.DepthOfFieldFor(v2, distance, Mathf.Sin(Mathf.DegToRad(pitch)), 0.15f, distance * 1.5f, view: v2.Tuning.BlurFor(view));
                    if (!(!dof.NearEnabled && dof.FarDistance > 1.5f * distance + 2.9f && dof.FarTransition > 0f && dof.Amount is > 0f and <= 1f))
                        Check(false, $"a far view keeps everything from the lens to past the subject and the companion crisp, and does not tilt: {view} at {distance} m, {pitch} degrees: {dof}");
                }
        var iso = LookDirector.DepthOfFieldFor(v2, 2.7f, Mathf.Sin(Mathf.DegToRad(RoomHud.IsoPitchDeg)), view: v2.Tuning.BlurFor(LookView.Isometric));
        Check(iso == LookDirector.DepthOfFieldFor(v2, 2.7f, Mathf.Sin(Mathf.DegToRad(RoomHud.IsoPitchDeg))), "F4 keeps exactly the tilt-shift it had");
        var observe = LookDirector.DepthOfFieldFor(v2, 1.4f, 0.7f, observe: true, view: v2.Tuning.BlurFor(LookView.Diorama));
        Check(Mathf.IsEqualApprox(observe.FarDistance - observe.NearDistance, v2.Tuning.Dof.ObserveBandM), "and observe (O) keeps its sliver of focus in any view");
        // The block is read strictly: every view stated, no unknown view, a far view's numbers sensible.
        var text = Encoding.UTF8.GetString(Godot.FileAccess.GetFileAsBytes(StylePreset.PathFor(RoomWorld.DefaultStyleId, 2)));
        StylePreset Variant(string from, string to) => StylePreset.Parse(Encoding.UTF8.GetBytes(text.Replace(from, to)), "variant.json");
        CheckThrows(() => Variant("\"isometric\": { \"blur\": \"miniature\" }", "\"isometric\": { \"blur\": \"tilt\" }"), "is not miniature or far", "an unknown blur style is refused");
        CheckThrows(() => Variant("\"isometric\": { \"blur\": \"miniature\" }", "\"iso\": { \"blur\": \"miniature\" }"), "x_look_views.iso", "an unknown view is refused");
        CheckThrows(() => Variant("\"far_amount\": 0.07 }, \"isometric\"", "\"far_amount\": 0 }, \"isometric\""), "x_look_views.diorama", "a far view without a blur amount is refused");
    }

    /// <summary>The view the director blurs for: what it is told, else the player's eye, else the HUD's view key; a framed camera keeps the miniature.</summary>
    private async Task CheckViewChoice(StylePreset v2, RoomData room)
    {
        var (holder, look) = NewDirector(v2, room, "ViewChoiceHolder");
        look.SetProcess(false);
        var player = new SmallPlayerController { Name = "Player", ReadKeyboard = false };
        holder.AddChild(player);
        player.SetPhysicsProcess(false);
        look.FocusTarget = player;
        var camera = new Camera3D { Near = 0.01f, Position = new Vector3(0.3f, 0.2f, 1.2f) };
        holder.AddChild(camera);
        camera.MakeCurrent();
        await Frames(1);
        Check(look.ViewFor(player.EyeCamera) == LookView.Eye && look.ViewFor(camera) == null, "unset, the player's eye camera is the eye view and another camera without a HUD keeps the miniature");
        look.View = LookView.Shoulder;
        look._Process(1.0 / 60.0);
        var attributes = (CameraAttributesPractical)camera.Attributes!;
        Check(attributes.DofBlurFarDistance > 3f && look.ViewFor(camera) == LookView.Shoulder, $"told the shoulder view, the game's camera keeps the island crisp ({attributes.DofBlurFarDistance:0.##} m)");
        var review = new Camera3D { Near = 0.01f, Position = new Vector3(0.3f, 0.6f, 1.2f) };
        holder.AddChild(review);
        look.View = null;
        look.FrameCamera(review, Vector3.Zero);
        Check(look.ViewFor(review) == null && ((CameraAttributesPractical)review.Attributes!).DofBlurFarDistance < 2f, "a camera framed for a review keeps the miniature blur unless told a view");
        holder.QueueFree();
        await Frames(1);
    }

    /// <summary>Distant islands hold their place on the horizon: never closer than they keep, however a swimmer comes at them; unmoved near home.</summary>
    private void CheckIslandHolding()
    {
        var home = new Vector2(5.1f, 9.18f);
        const float keep = 6.2f;
        Check(OpenSea.IslandOffset(home, keep, Vector2.Zero) == home && OpenSea.IslandOffset(home, keep, new Vector2(-3f, 2f)) == home,
            "from the home island the distant island stands where the generator put it");
        var nearest = float.PositiveInfinity;
        var continuous = true;
        for (var angle = 0; angle < 360; angle += 5)
        {
            var heading = Vector2.FromAngle(Mathf.DegToRad(angle));
            var last = OpenSea.IslandOffset(home, keep, Vector2.Zero);
            for (var s = 0.1f; s < 2000f; s *= 1.05f)
            {
                var swimmer = heading * s;
                var at = OpenSea.IslandOffset(home, keep, swimmer);
                nearest = Mathf.Min(nearest, at.DistanceTo(swimmer));
                var step = swimmer.DistanceTo(heading * (s / 1.05f));
                continuous &= at.DistanceTo(last) <= step + Mathf.Sqrt(2f * keep * step) + 1e-3f;
                continuous &= at.Length() >= home.Length() - 1e-3f;
                last = at;
            }
        }
        {
            // Out wide past the island's side, round behind it and back toward home through where it was: no jump, never reached.
            var path = new[] { new Vector2(12f, 0f), new Vector2(20f, 20f), new Vector2(8f, 30f), new Vector2(4f, 6f), Vector2.Zero };
            var last = OpenSea.IslandOffset(home, keep, Vector2.Zero);
            for (var leg = 0; leg < path.Length; leg++)
                for (var t = 0f; t <= 1f; t += 0.002f)
                {
                    var from = path[(leg + path.Length - 1) % path.Length];
                    var swimmer = from.Lerp(path[leg], t);
                    var at = OpenSea.IslandOffset(home, keep, swimmer);
                    var step = from.DistanceTo(path[leg]) * 0.002f;
                    nearest = Mathf.Min(nearest, at.DistanceTo(swimmer));
                    continuous &= at.DistanceTo(last) <= step + Mathf.Sqrt(2f * keep * step) + 1e-3f;
                    last = at;
                }
        }
        Check(nearest >= keep - 1e-3f, $"swimming any way, as far as 2 km, the island's centre never comes nearer than {keep} m (nearest {nearest:0.###} m)");
        Check(continuous, "it slides without jumping (no faster than a swimmer slipping past its side makes it) and only ever outward from the home island (never through it)");
    }

    /// <summary>The real island garage, when exported: the surface follows the camera and leaves the room's sea to it, the bed lies under the backdrop's floor, the islands are lifted out and keep off.</summary>
    private async Task CheckRealOpenSea(StylePreset preset)
    {
        var directory = FindLandscapeFixture();
        if (directory == null) { GD.Print("LOOK_INFO: no island garage exported under .cache/landscape-fixture; the open sea's real-room checks were skipped"); return; }
        MaterialLibrary.Configure(preset);
        var land = RoomData.Load(directory);
        if (land.Sea == null) { GD.Print("LOOK_INFO: the exported garage has no sea; the open sea's real-room checks were skipped"); return; }
        var (holder, look) = NewDirector(preset, land, "OpenSeaHolder");
        var built = RoomBuilder.Build(land);
        holder.AddChild(built);
        look.Dress(built);
        await Frames(2);
        var open = look.OpenSea;
        Check(open != null && open.Surface != null && open.Note.Length == 0, "a room with a sea gets the open sea, surface and all: " + (open?.Note ?? "none"));
        if (open?.Surface == null) { holder.QueueFree(); return; }
        var surface = (ShaderMaterial)open.Surface.MaterialOverride!;
        Check(surface.GetShaderParameter("world_pattern").AsBool() && surface.GetShaderParameter("use_hole").AsBool()
            && open.Hole.X <= land.Bounds.Position.X - 20f && open.Hole.Z >= land.Bounds.End.X + 20f,
            $"the surface paints in world space and leaves the room's own sea ({open.Hole}) to it");
        var backdropFloor = built.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().Where(m => EntityOf(m) == "shell:scenery_moss").Select(m => (m.GlobalTransform * m.GetAabb()).Position.Y).DefaultIfEmpty(float.NaN).Min();
        Check(open.BedY < backdropFloor && open.BedY > backdropFloor - 0.1f && open.BedY < land.Sea.LevelM - 1f, $"the open bed lies just under the backdrop's deepest floor ({open.BedY:0.###} under {backdropFloor:0.###} m)");
        var floorMesh = built.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().First(m => EntityOf(m) == "shell:scenery_moss");
        var floorArrays = floorMesh.Mesh.SurfaceGetArrays(0);
        var floorVertices = floorArrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var floorTop = floorArrays[(int)Mesh.ArrayType.Index].AsInt32Array().Select(i => (floorMesh.GlobalTransform * floorVertices[i]).Y).DefaultIfEmpty(float.NaN).Max();
        // The garage's six islands (the package's records) come out as five pieces: two share a shoal above the cut and move as one.
        Check(open.Islands.Count >= 5 && floorTop < land.Sea.LevelM - OpenSea.IslandCutM + 0.25f,
            $"the distant islands are lifted out of the backdrop as {open.Islands.Count} pieces, and what stays (the floor) lies deep under the water (top {floorTop:0.###} m)");
        Check(open.Islands.All(i => i.KeepM >= i.HomeOffset.Length() - open.HomeReachM - 1e-3f && i.HomeOffset.Length() > open.HomeReachM), "each keeps off at least as far as it looks from the edge of the home waters");
        // Far out at sea: the surface and bed come along, the islands keep off.
        var camera = new Camera3D { Far = 100f };
        holder.AddChild(camera);
        foreach (var at in new[] { new Vector3(0f, 0.1f, -3.9f), new Vector3(40f, 0.05f, -70f), new Vector3(-600f, 0.05f, 900f), new Vector3(8.9f, 0.05f, -6.3f) })
        {
            camera.GlobalPosition = at;
            open.Follow(camera);
            var swimmer = new Vector2(at.X, at.Z);
            var nearest = open.Islands.Min(i => (new Vector2(i.Mesh.GlobalPosition.X, i.Mesh.GlobalPosition.Z) - new Vector2(i.BasePosition.X, i.BasePosition.Z) + open.Home + i.HomeOffset).DistanceTo(swimmer) - i.KeepM);
            Check(new Vector2(open.Surface.GlobalPosition.X, open.Surface.GlobalPosition.Z).IsEqualApprox(swimmer) && Mathf.IsEqualApprox(open.Surface.GlobalPosition.Y, land.Sea.LevelM)
                && new Vector2(open.Bed.GlobalPosition.X, open.Bed.GlobalPosition.Z).IsEqualApprox(swimmer) && nearest >= -1e-3f,
                $"at {at} the sea's surface and bed are under the swimmer and every island keeps off");
        }
        Check(Mathf.IsEqualApprox(surface.GetShaderParameter("horizon_fade_end_m").AsSingle(), 100f * OpenSea.HorizonFadeEnd), "the far sea turns into the horizon just before the camera's far plane");
        look.SetClock(19.5f, 172);
        var horizon = LookSky.At(look.Preset, look.Moment).Horizon;
        Check(surface.GetShaderParameter("horizon_color").AsColor().IsEqualApprox(horizon), "and into the sky's own horizon colour of the hour");
        holder.QueueFree();
        await Frames(1);
    }

    /// <summary>The focus highlight: one thing at a time wears the overlay; switching or clearing gives the meshes back what they had.</summary>
    private async Task CheckFocusHighlight(StylePreset preset, RoomData room)
    {
        var (holder, look) = NewDirector(preset, room, "FocusHolder");
        Node3D Thing(string name)
        {
            var thing = new Node3D { Name = name };
            thing.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.04f, 0.03f, 0.03f) } });
            thing.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.01f, BottomRadius = 0.01f, Height = 0.05f } });
            holder.AddChild(thing);
            return thing;
        }
        var crate = Thing("Crate");
        var log = Thing("Log");
        var earlier = new StandardMaterial3D();
        var logMeshes = log.GetChildren().OfType<MeshInstance3D>().ToArray();
        logMeshes[0].MaterialOverlay = earlier;
        look.SetFocusHighlight(crate);
        var crateMeshes = crate.GetChildren().OfType<MeshInstance3D>().ToArray();
        Check(look.Highlight.Target == crate && crateMeshes.All(m => m.MaterialOverlay == look.Highlight.Overlay) && logMeshes.All(m => m.MaterialOverlay != look.Highlight.Overlay),
            "the focus lights every mesh of the one thing in focus, nothing else");
        look.SetFocusHighlight(log);
        Check(crateMeshes.All(m => m.MaterialOverlay == null) && logMeshes.All(m => m.MaterialOverlay == look.Highlight.Overlay), "one thing at a time: moving the focus clears the last one");
        look.SetFocusHighlight(null);
        Check(look.Highlight.Target == null && logMeshes[0].MaterialOverlay == earlier && logMeshes[1].MaterialOverlay == null, "no focus: every mesh gets back the overlay it had");
        look.SetFocusHighlight(crate);
        crate.QueueFree();
        await Frames(2);
        look._Process(1.0 / 60.0);
        Check(look.Highlight.Target == null, "a thing that is freed loses the focus");
        // The overlay chain: a rim on the thing, a light line over a wider dark one; the shaders take what the code sets.
        var under = look.Highlight.Overlay;
        var line = under.NextPass as ShaderMaterial;
        var rim = (line?.NextPass as ShaderMaterial)!;
        Check(line != null && rim == look.Highlight.Rim && under.GetShaderParameter("width_px").AsSingle() > line.GetShaderParameter("width_px").AsSingle()
            && under.GetShaderParameter("line_color").AsColor().Luminance < 0.3f && line.GetShaderParameter("line_color").AsColor().Luminance > 0.8f
            && under.RenderPriority < line.RenderPriority && line.RenderPriority < rim.RenderPriority,
            "the focus is a warm rim over a light line over a wider dark one (it reads on pale sand as on grass)");
        foreach (var material in new[] { rim, line!, under! })
        {
            var declared = material.Shader.GetShaderUniformList().Select(u => u.AsGodotDictionary()["name"].AsString()).ToHashSet();
            var code = Regex.Replace(material.Shader.Code, @"//[^\n]*", "");
            Check(declared.Count >= 4 && declared.All(n => !material.GetShaderParameter(n).Equals(default(Variant)) || n == "hull_centre") && code.Contains("unshaded") && code.Contains("depth_draw_never"),
                $"{material.ResourceName}: every uniform is set, unlit, and it writes no depth");
        }
        holder.QueueFree();
        await Frames(1);
    }
}

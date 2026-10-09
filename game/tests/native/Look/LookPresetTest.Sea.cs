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
        Check(after.FarDistance >= 3f && after.FarDistance + after.FarTransition >= 15f, $"after, the island is crisp to {after.FarDistance:0.#} m and only the far shore, the distant islands and the horizon soften (fully from {after.FarDistance + after.FarTransition:0.#} m)");
        Check(after.Amount * after.Amount <= before.Amount * before.Amount, $"and the far blur costs no more than before (bokeh amount {after.Amount:0.###} against {before.Amount:0.###})");
        foreach (var view in new[] { LookView.Eye, LookView.Shoulder, LookView.Diorama })
            foreach (var distance in new[] { 0.06f, 0.3f, 1.4f, 2.4f })
                foreach (var pitch in new[] { 0f, 20f, 45f, 80f })
                {
                    var dof = LookDirector.DepthOfFieldFor(v2, distance, Mathf.Sin(Mathf.DegToRad(pitch)), 0.15f, distance * 1.5f, view: v2.Tuning.BlurFor(view));
                    if (!(dof.NearDistance < distance && dof.FarDistance > 1.5f * distance + 2.9f && dof.NearDistance <= v2.NearBlurDistanceM + 1e-4f && dof.FarTransition > 0f && dof.Amount is > 0f and <= 1f))
                        Check(false, $"a far view keeps the subject and the companion crisp, only the lens's near blur near, and does not tilt: {view} at {distance} m, {pitch} degrees: {dof}");
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
}

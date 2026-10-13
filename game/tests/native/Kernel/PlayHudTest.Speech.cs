using Godot;
using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using EnFractal.Native;
using EnFractal.Native.Kernel;

namespace EnFractal.Tests.Kernel;

public partial class PlayHudTest
{
    /// <summary>
    /// companion.say in the player's view: the Gubble's words over its head, above its thought bubble, in the cue's style; the
    /// grown-ups' test mode is the player's own setting (the Wishes page), off by default, saved in the profile, handed to the
    /// host and shown plainly while on. The AI's own words show in quotation marks, in another tint.
    /// </summary>
    private async Task TestGubbleSpeechCaption()
    {
        var host = CommandHost.Of(_world)!;
        var speech = _hud.Speech;
        Check(!_hud.CompanionOwnWords && !host.CompanionOwnWords && !speech.Banner.Visible,
            "the grown-ups' test mode is off by default, and nothing says it is on");
        Check(speech.GetParent() == _hud && speech.Caption.GetThemeFontSize("font_size") >= 18 && speech.Caption.GetThemeConstant("outline_size") >= 6,
            $"the Gubble's words are the HUD's screen text ({speech.Caption.GetThemeFontSize("font_size")} px), outlined dark like its bubble");
        _hud.SetViewMode(1);
        _hud.Cue.Think(GubbleMagic.LookIcon);
        Check(SaidOk(Say(host, "hud-say-0", new JsonObject { ["phrase"] = "wow" })), "the AI says a curated phrase");
        await Frames(3);
        var companion = _world.Companion;
        var camera = _hud.GetViewport().GetCamera3D();
        var feet = camera.UnprojectPosition(companion.GlobalPosition);
        var framed = !camera.IsPositionBehind(companion.GlobalPosition) && _hud.GetViewport().GetVisibleRect().Grow(-120).HasPoint(feet);
        var words = new Rect2(speech.Caption.Position, speech.Caption.Size);
        var bubble = new Rect2(_hud.Cue.Bubble.Position, _hud.Cue.Bubble.Size);
        if (!framed) GD.Print($"PLAY_HUD_INFO: F2 does not frame the Gubble here ({feet})");
        Check(speech.Words == "Wow!" && !speech.OwnWords && speech.Caption.Text == "Wow!" &&
              (!framed || speech.Caption.Visible && Mathf.Abs(words.GetCenter().X - feet.X) < 60f && words.End.Y <= bubble.Position.Y + 1f && words.End.Y < feet.Y),
            $"a curated phrase shows over the Gubble ({words}), above its thought bubble ({bubble}), over its feet at {feet}");
        _hud.Cue.Clear();

        Check(SaidCode(Say(host, "hud-say-1", new JsonObject { ["text"] = "Hello from the AI" })) == "permission_denied" && speech.Words == "Wow!",
            "with the mode off, the AI's own words are refused and nothing new shows");
        var toggle = (CheckButton)_hud.FindChild("OwnWordsToggle", true, false)!;
        Check(toggle.Text == RoomHud.OwnWordsSetting && !toggle.ButtonPressed, "the Wishes page has the grown-ups' setting, in the founder's words, unticked");
        toggle.ButtonPressed = true;
        await Frames(2);
        var saved = new ConfigFile();
        Check(_hud.CompanionOwnWords && host.CompanionOwnWords && speech.Banner.Visible && speech.Banner.Text == GubbleSpeechCaption.BannerText &&
              saved.Load(RoomHud.ProfilePath) == Error.Ok && saved.GetValue("profile", "ai_own_words", false).AsBool(),
            "the player's tick turns the mode on in the host, shows the banner, and saves it in the profile");
        Check(SaidOk(Say(host, "hud-say-2", new JsonObject { ["text"] = "That bridge looks wobbly!" })), "in the mode, the AI's own words are accepted");
        await Frames(2);
        Check(speech.Words == "That bridge looks wobbly!" && speech.OwnWords && speech.Caption.Text == "“That bridge looks wobbly!”",
            "the AI's own words show in quotation marks, never as the game's own: " + speech.Caption.Text);
        toggle.ButtonPressed = false;
        await Frames(2);
        saved = new ConfigFile();
        Check(!host.CompanionOwnWords && !speech.Banner.Visible && saved.Load(RoomHud.ProfilePath) == Error.Ok && !saved.GetValue("profile", "ai_own_words", true).AsBool(),
            "unticked, the mode is off in the host and the profile, and the banner goes");
    }

    private static JsonObject Say(CommandHost host, string actionId, JsonObject args) => host.HandleObject(new JsonObject
    {
        ["schema"] = "enfractal.command", ["version"] = 1, ["action_id"] = actionId, ["room_id"] = host.Room.RoomId, ["op"] = "companion.say", ["args"] = args,
    }.ToJsonString(), CommandHost.CompanionPrincipal);

    private static bool SaidOk(JsonObject result) => result["ok"]?.GetValue<bool>() == true;
    private static string? SaidCode(JsonObject result) => result["error"]?["code"]?.GetValue<string>();
}

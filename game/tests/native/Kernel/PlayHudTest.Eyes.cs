using System.Text.Json.Nodes;
using System.Threading.Tasks;
using EnFractal.Native;
using EnFractal.Native.Kernel;
using Godot;

namespace EnFractal.Tests.Kernel;

/// <summary>The player's setting "Let my AI see through the Gubble's eyes": a switch in Customize, kept in the profile, obeyed by the host.</summary>
public partial class PlayHudTest
{
    private async Task TestEyesSetting()
    {
        var host = CommandHost.Of(_world)!;
        var toggle = _hud.EyesToggle;
        Check(toggle != null && toggle.ButtonPressed && host.GubbleEyesAllowed && toggle.Text == "Let my AI see through the Gubble's eyes",
            "the Customize panel has the eyes setting, on by default, and the host agrees");
        toggle!.ButtonPressed = false;
        await Frames(1);
        var look = new JsonObject
        {
            ["schema"] = "enfractal.query", ["version"] = 1, ["query_id"] = "hud-look", ["room_id"] = _world.Room.RoomId, ["op"] = "view.look",
            ["args"] = new JsonObject { ["actor"] = CommandHost.CompanionAvatarId },
        };
        var refused = await host.HandleObjectAsync(look.ToJsonString(), CommandHost.CompanionPrincipal);
        Check(!host.GubbleEyesAllowed && !CommandHost.LoadGubbleEyes(RoomHud.ProfilePath) && refused["error"]?["code"]?.GetValue<string>() == "permission_denied",
            "switching it off is saved in the player's profile and the host refuses the AI's look");
        toggle.ButtonPressed = true;
        await Frames(1);
        Check(host.GubbleEyesAllowed && CommandHost.LoadGubbleEyes(RoomHud.ProfilePath), "switching it back on is saved too");
        await TestSavingAppearanceKeepsSettings(host);
    }

    /// <summary>"Save appearance" writes its own fields only: the player's settings saved beside them stay as they are.</summary>
    private async Task TestSavingAppearanceKeepsSettings(CommandHost host)
    {
        var fireSetting = (CheckButton)_hud.FindChild("GubbleFireSetting", true, false)!;
        _hud.EyesToggle!.ButtonPressed = false;
        fireSetting.EmitSignal(BaseButton.SignalName.Toggled, false);
        await Frames(1);
        var config = new ConfigFile();
        Check(config.Load(RoomHud.ProfilePath) == Error.Ok, "the profile holds the two settings");
        config.SetValue("profile", "a_later_setting", 7);
        Check(config.Save(RoomHud.ProfilePath) == Error.Ok, "and a setting a later build adds");
        _hud.SavePreferences();
        var saved = new ConfigFile();
        Check(saved.Load(RoomHud.ProfilePath) == Error.Ok && !CommandHost.LoadGubbleEyes(RoomHud.ProfilePath) && !CommandHost.LoadGubbleFire(RoomHud.ProfilePath) &&
            saved.GetValue("profile", "a_later_setting", 0).AsInt32() == 7 && saved.GetValue("profile", "player_color", -1).VariantType == Variant.Type.Int,
            "saving the appearance keeps \"Let my AI see\" and \"Let the Gubble use fire\" off, and every other setting, beside the appearance it writes");
        _hud.EyesToggle.ButtonPressed = true;
        fireSetting.EmitSignal(BaseButton.SignalName.Toggled, true);
        await Frames(1);
        Check(host.GubbleEyesAllowed && host.GubbleFireAllowed && CommandHost.LoadGubbleEyes(RoomHud.ProfilePath) && CommandHost.LoadGubbleFire(RoomHud.ProfilePath),
            "both settings switch back on for the tests that follow");
    }
}

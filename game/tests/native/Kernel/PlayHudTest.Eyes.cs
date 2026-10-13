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
    }
}

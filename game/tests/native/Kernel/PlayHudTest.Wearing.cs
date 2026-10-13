using Godot;
using System.Linq;
using System.Threading.Tasks;
using EnFractal.Native;
using EnFractal.Native.Characters;
using EnFractal.Native.Characters.Gaits;
using EnFractal.Native.Look;

namespace EnFractal.Tests.Kernel;

/// <summary>
/// The characters round's wiring in the room (RoomWorld, CharacterVisual): the shipped library, the HUD's choices, and the
/// player's body drawn as the character they choose, ticked with their state and back to the prototype figure on "".
/// </summary>
public partial class PlayHudTest
{
    private async Task TestWearingCharacters()
    {
        var player = _world.Player;
        var prototype = player.GetNode<Node3D>("OriginalPrototypeBody");
        var prototypeParts = prototype.GetChildren().OfType<Node3D>().ToList();
        var library = _world.Characters;
        Check(library != null && library.Entries.Count == 5 && library.Entries.All(e => e.IsLoaded),
            "the room loads the five shipped characters: " + string.Join(", ", library?.Entries.Select(e => e.Id + (e.IsLoaded ? "" : " (" + e.RefusalReason + ")")) ?? []));
        var offered = _hud.CharacterChoices?.Invoke()?.Select(c => c.Id).ToList() ?? new();
        Check(offered.SequenceEqual(new[] { "cloudpuff", "potato_man", "stickbear", "tomato_man" }),
            "the room offers the four drawn characters to wear, never the Gubble: " + string.Join(", ", offered));
        try
        {
            Check(player.SetCharacterId("stickbear"), "the player can choose Stickbear");
            await Frames(4);
            var stickbear = _world.PlayerVisual;
            Check(stickbear != null && stickbear.Body.Definition.Id == "stickbear" && player.BodyVisual == stickbear && stickbear.Root.GetParent() == prototype &&
                prototypeParts.All(p => !p.Visible), "choosing Stickbear draws the player as Stickbear, the prototype's parts hidden");
            Check(stickbear!.Body.FindChildren("*", nameof(GeometryInstance3D), true, false).OfType<GeometryInstance3D>().All(g => g.Layers == EyeView.PlayerBodyLayer),
                "the drawn body is on the player's own layer, so the player's eye leaves it out");
            var tick = stickbear.LastState.Tick;
            await Frames(5);
            Check(stickbear.LastState.Tick > tick && stickbear.LastState.Tick == player.LastBodyState.Tick && stickbear.Look.Surfaces.Count > 0 &&
                stickbear.Look.Count(CharacterSurfaceRole.Plain) > 0, "the drawn body is ticked with the player's state every physics tick and wears its look");
            Check(CharacterGaits.FindGait(stickbear.Stack) != null, "the drawn player walks with its own gait and breathes (CharacterMotion)");
            Check(GubbleCharacter.Of(_world.Companion) is { } gubble && CharacterGaits.FindGait(gubble.Visual.Stack) != null,
                "and the drawn Gubble floats with its own gait");
            Check(player.KnockoutRecoveryDelayS > 0 && _world.Companion.KnockoutRecoveryDelayS == 0,
                "the drawn player can tumble (a knockout's recovery waits for it); the floating Gubble never does");
            Check(player.SetCharacterId("cloudpuff"), "and can change to Cloudpuff");
            await Frames(3);
            Check(_world.PlayerVisual?.Body.Definition.Id == "cloudpuff" && !GodotObject.IsInstanceValid(stickbear.Root),
                "changing character frees the one worn before");
            Check(!player.SetCharacterId("../stickbear") && _world.PlayerVisual?.Body.Definition.Id == "cloudpuff", "a choice that is not a token changes nothing");
        }
        finally
        {
            player.SetCharacterId("");
        }
        await Frames(3);
        Check(_world.PlayerVisual == null && player.BodyVisual == null && prototypeParts.All(p => p.Visible) &&
            prototype.GetChildren().OfType<CharacterBody>().Count() == 0, "choosing the plain figure brings the prototype back and frees the drawn body");
        Check(player.KnockoutRecoveryDelayS == 0, "and the prototype has no tumble: a knockout recovers at once, as before");
    }
}

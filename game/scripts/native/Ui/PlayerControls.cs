using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace EnFractal.Native;

/// <summary>
/// The player's controls by name. Every binding is an input action in game/project.godot ([input]), bound to a physical key
/// (a position on the keyboard, whatever the layout), so the player can remap it and the key help follows. The handlers
/// (RoomHud, SmallPlayerController) ask for these actions and never for a keycode.
/// </summary>
public static class Act
{
    // Movement, read every physics tick.
    public static readonly StringName MoveForward = "move_forward";
    public static readonly StringName MoveBack = "move_back";
    public static readonly StringName MoveLeft = "move_left";
    public static readonly StringName MoveRight = "move_right";
    public static readonly StringName MoveSprint = "move_sprint";
    public static readonly StringName MoveDive = "move_dive";
    /// <summary>Jump on the ground; rise while diving.</summary>
    public static readonly StringName Jump = "jump";
    // The body.
    public static readonly StringName BodyRecover = "body_recover";
    public static readonly StringName BodyHome = "body_home";
    // The views.
    public static readonly StringName ViewEye = "view_eye";
    public static readonly StringName ViewShoulder = "view_shoulder";
    public static readonly StringName ViewDiorama = "view_diorama";
    public static readonly StringName ViewIso = "view_iso";
    public static readonly StringName ViewObserve = "view_observe";
    /// <summary>F3 only.</summary>
    public static readonly StringName ViewZoomIn = "view_zoom_in";
    /// <summary>F3 only.</summary>
    public static readonly StringName ViewZoomOut = "view_zoom_out";
    /// <summary>F4 only.</summary>
    public static readonly StringName IsoTurnLeft = "iso_turn_left";
    /// <summary>F4 only.</summary>
    public static readonly StringName IsoTurnRight = "iso_turn_right";
    // The companion (the magic design's "The keys", step 2). Enter is kept free for the wish box.
    /// <summary>Tap: the smart ask at the aim. Hold: the Gubble wheel.</summary>
    public static readonly StringName GubbleAsk = "gubble_ask";
    /// <summary>Come, then follow (the recall).</summary>
    public static readonly StringName GubbleRecall = "gubble_recall";
    /// <summary>Stop the Gubble's goal and its effects (goal.stop).</summary>
    public static readonly StringName GubbleStop = "gubble_stop";
    /// <summary>The five ability slots, fixed by category (GubbleSlots): 1 light, 2 growth, 3 float, 4 burst, 5 build.</summary>
    public static readonly StringName GubbleSlot1 = "gubble_slot_1";
    public static readonly StringName GubbleSlot2 = "gubble_slot_2";
    public static readonly StringName GubbleSlot3 = "gubble_slot_3";
    public static readonly StringName GubbleSlot4 = "gubble_slot_4";
    public static readonly StringName GubbleSlot5 = "gubble_slot_5";
    /// <summary>The slot actions in slot order (index 0 is slot 1).</summary>
    public static readonly StringName[] GubbleSlots = { GubbleSlot1, GubbleSlot2, GubbleSlot3, GubbleSlot4, GubbleSlot5 };
    // The hands.
    /// <summary>Pick up what the player faces, or put down what it holds.</summary>
    public static readonly StringName Hands = "hands";
    public static readonly StringName Push = "push";
    // The testing toggles (the help lists them under "Testing").
    public static readonly StringName PhysicsNext = "physics_next";
    public static readonly StringName TimeStep = "time_step";
    public static readonly StringName SeasonStep = "season_step";
    public static readonly StringName Lamps = "lamps";
    // The panels and the mouse. The cursor is always free (the Glow playtest, 9 October): it is the aim in every view.
    public static readonly StringName HudHelp = "hud_help";
    public static readonly StringName HudCustomize = "hud_customize";
    /// <summary>Esc: closes the appearance panel, cancels the wheel or a drag. (Its map name is from when it freed the captured mouse.)</summary>
    public static readonly StringName MouseRelease = "mouse_release";
    /// <summary>Hold and drag to look around (the left button); a click without a drag does nothing. (Its map name is from when a click captured the mouse.)</summary>
    public static readonly StringName LookDrag = "mouse_capture";
}

/// <summary>Where an action is live. The keys of one context must not share a key; two contexts may.</summary>
[Flags]
public enum ControlContext
{
    /// <summary>Every view, while the appearance panel is shut.</summary>
    Always = 1,
    /// <summary>The diorama view (F3) only.</summary>
    Diorama = 2,
    /// <summary>The isometric view (F4) only.</summary>
    Isometric = 4,
    /// <summary>While the appearance panel is open, which blocks every key but these.</summary>
    Customize = 8,
}

/// <summary>One action and where it is live.</summary>
public readonly record struct ControlSpec(StringName Action, ControlContext Live);

/// <summary>
/// Reading the controls: whether an event is an action's press, whether an action is held, and what key an action has now
/// (for the key help). Mouse motion stays event-based; actions are for buttons, keys and the wheel.
/// </summary>
public static class PlayerControls
{
    /// <summary>Every control and where it is live; the map in project.godot and this list name the same actions (PlayHudTest).</summary>
    public static readonly ControlSpec[] Specs =
    {
        new(Act.MoveForward, ControlContext.Always), new(Act.MoveBack, ControlContext.Always),
        new(Act.MoveLeft, ControlContext.Always), new(Act.MoveRight, ControlContext.Always),
        new(Act.MoveSprint, ControlContext.Always), new(Act.MoveDive, ControlContext.Always), new(Act.Jump, ControlContext.Always),
        new(Act.BodyRecover, ControlContext.Always), new(Act.BodyHome, ControlContext.Always),
        new(Act.ViewEye, ControlContext.Always), new(Act.ViewShoulder, ControlContext.Always),
        new(Act.ViewDiorama, ControlContext.Always), new(Act.ViewIso, ControlContext.Always), new(Act.ViewObserve, ControlContext.Always),
        new(Act.ViewZoomIn, ControlContext.Diorama), new(Act.ViewZoomOut, ControlContext.Diorama),
        new(Act.IsoTurnLeft, ControlContext.Isometric), new(Act.IsoTurnRight, ControlContext.Isometric),
        new(Act.GubbleAsk, ControlContext.Always), new(Act.GubbleRecall, ControlContext.Always), new(Act.GubbleStop, ControlContext.Always),
        new(Act.GubbleSlot1, ControlContext.Always), new(Act.GubbleSlot2, ControlContext.Always), new(Act.GubbleSlot3, ControlContext.Always),
        new(Act.GubbleSlot4, ControlContext.Always), new(Act.GubbleSlot5, ControlContext.Always),
        new(Act.Hands, ControlContext.Always), new(Act.Push, ControlContext.Always),
        new(Act.PhysicsNext, ControlContext.Always), new(Act.TimeStep, ControlContext.Always),
        new(Act.SeasonStep, ControlContext.Always), new(Act.Lamps, ControlContext.Always),
        new(Act.HudHelp, ControlContext.Always),
        new(Act.HudCustomize, ControlContext.Always | ControlContext.Customize),
        new(Act.MouseRelease, ControlContext.Always | ControlContext.Customize),
        new(Act.LookDrag, ControlContext.Always),
    };

    /// <summary>
    /// Pairs on one key whose modifiers nest: the first is checked before the second, so Shift+T is the season and a bare T the
    /// time of day. (Matching is not exact: Shift+T also satisfies a bare T, as it did when the keys were keycodes.)
    /// </summary>
    public static readonly (StringName First, StringName Second)[] CheckedBefore = { (Act.SeasonStep, Act.TimeStep) };

    /// <summary>When set (a test), every action a handler asks about is recorded here, to show the code uses the map's actions and no others.</summary>
    public static HashSet<string>? Trace { get; set; }

    /// <summary>The actions in the project's input map that are the game's own (the engine's ui_ actions are not).</summary>
    public static IEnumerable<string> MapActions() => InputMap.GetActions().Select(name => name.ToString()).Where(name => !name.StartsWith("ui_", StringComparison.Ordinal)).OrderBy(name => name, StringComparer.Ordinal);

    /// <summary>
    /// The event as the actions read it. A key event carries the key's position (PhysicalKeycode) and what the layout makes of
    /// it (Keycode). Actions are bound to the position; an event with only the second (a synthetic one) is read as the first,
    /// as the handlers did before the actions.
    /// </summary>
    public static InputEvent Normalise(InputEvent input)
    {
        if (input is InputEventKey { PhysicalKeycode: Key.None } key && key.Keycode != Key.None)
        {
            var copy = (InputEventKey)key.Duplicate();
            copy.PhysicalKeycode = key.Keycode;
            return copy;
        }
        return input;
    }

    /// <summary>Whether this is a press that has just happened: pressed, and not a held key repeating.</summary>
    public static bool Fresh(InputEvent input) => input.IsPressed() && !input.IsEcho();

    /// <summary>Whether the event is a fresh press of the action. Modifiers are not matched exactly: Shift+T is also a T.</summary>
    public static bool Hit(InputEvent input, StringName action)
    {
        Trace?.Add(action.ToString());
        return input.IsActionPressed(action);
    }

    /// <summary>Whether the event is the action's button or key letting go (the ask button's tap and hold end on it).</summary>
    public static bool Released(InputEvent input, StringName action)
    {
        Trace?.Add(action.ToString());
        return input.IsActionReleased(action);
    }

    /// <summary>Whether the action is held now (polled).</summary>
    public static bool Held(StringName action)
    {
        Trace?.Add(action.ToString());
        return Input.IsActionPressed(action);
    }

    /// <summary>
    /// The action's key as the help shows it: "W", "Shift+T", "click", "wheel up", named on the player's own keyboard layout;
    /// "unbound" when it has no key.
    /// </summary>
    public static string Label(StringName action)
    {
        if (!InputMap.HasAction(action)) return "unbound";
        foreach (var input in InputMap.ActionGetEvents(action))
            if (Describe(input) is { } text) return text;
        return "unbound";
    }

    /// <summary>The four movement keys together: "WASD" when each is one character (forward, left, back, right), else "W/A/S/D" with what they are.</summary>
    public static string MoveLabel()
    {
        var keys = new[] { Act.MoveForward, Act.MoveLeft, Act.MoveBack, Act.MoveRight }.Select(Label).ToArray();
        return keys.All(key => key.Length == 1) ? string.Concat(keys) : string.Join("/", keys);
    }

    /// <summary>The look drag as the help says it: "drag" when it is the left button, else its button or key and "drag" ("middle-click drag").</summary>
    public static string DragLabel()
    {
        var label = Label(Act.LookDrag);
        return label == "click" ? "drag" : label + " drag";
    }

    /// <summary>The zoom keys together: "wheel" when they are the wheel's two directions, else both ("U/I").</summary>
    public static string ZoomLabel()
    {
        var (zoomIn, zoomOut) = (Label(Act.ViewZoomIn), Label(Act.ViewZoomOut));
        return zoomIn == "wheel up" && zoomOut == "wheel down" ? "wheel" : $"{zoomIn}/{zoomOut}";
    }

    /// <summary>The desktop display servers can say what a key position is labelled; the others (headless among them) report an error if asked.</summary>
    private static bool KnowsKeyboardLayout => DisplayServer.GetName() is "Windows" or "X11" or "Wayland" or "macOS";

    private static string? Describe(InputEvent input)
    {
        switch (input)
        {
            case InputEventKey key:
            {
                var position = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
                if (position == Key.None) return null;
                // The label on the key as this keyboard prints it: the key at W's place reads Z on a French one.
                var shown = KnowsKeyboardLayout ? DisplayServer.KeyboardGetKeycodeFromPhysical(position) : Key.None;
                var code = shown != Key.None ? shown : position;
                // A key that prints a character is named by it ("[" rather than "BracketLeft"); the others by the engine's name.
                var name = code == Key.Escape ? "Esc" : (long)code is >= 33 and <= 126 ? ((char)(long)code).ToString() : OS.GetKeycodeString(code);
                return Modifiers(key, position) + name;
            }
            case InputEventMouseButton mouse:
                return Modifiers(mouse, Key.None) + mouse.ButtonIndex switch
                {
                    MouseButton.Left => "click",
                    MouseButton.Right => "right-click",
                    MouseButton.Middle => "middle-click",
                    MouseButton.WheelUp => "wheel up",
                    MouseButton.WheelDown => "wheel down",
                    var other => $"mouse button {(int)other}",
                };
            default:
                return input.AsText();
        }
    }

    /// <summary>"Ctrl+Alt+Shift+Meta+" for the modifiers the event asks for, leaving out the one that is the key itself.</summary>
    private static string Modifiers(InputEventWithModifiers input, Key own) =>
        (input.CtrlPressed && own != Key.Ctrl ? "Ctrl+" : "") + (input.AltPressed && own != Key.Alt ? "Alt+" : "") +
        (input.ShiftPressed && own != Key.Shift ? "Shift+" : "") + (input.MetaPressed && own != Key.Meta ? "Meta+" : "");
}

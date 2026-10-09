using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using EnFractal.Native;
using EnFractal.Native.Kernel;
using EnFractal.Native.Look;
using EnFractal.Native.Sandbox;

namespace EnFractal.Tests.Kernel;

/// <summary>
/// The second playtest's fix round, in the real room with the real look and HUD: Q and E turn the isometric view (the
/// invention workshop that also bound them is gone), T and Shift+T step the time of day and the season through the day's
/// own light and the solstices and equinoxes and end back on the real clock, the HUD says what the clock is, and the
/// companion's name tag is drawn solid so depth of field and temporal anti-aliasing cannot blur it. Keys are handed to the
/// HUD as the engine would hand them; nothing here needs a window, so it runs headless like the other room tests.
/// </summary>
public partial class PlayHudTest : Node
{
    /// <summary>The room's saves for this suite: the hand keys keep durable receipts, so they never touch the player's own saves.</summary>
    private const string HudSaves = "user://tests/play_hud/saves";
    private int _checks;
    private int _failures;
    private RoomWorld _world = null!;
    private RoomHud _hud = null!;

    public override async void _Ready()
    {
        try
        {
            RemoveSaves();
            CommandHost.SaveRoot = HudSaves;
            _world = GD.Load<PackedScene>("res://scenes/room.tscn").Instantiate<RoomWorld>();
            AddChild(_world);
            for (var i = 0; i < 900 && !_world.WorldReady && _world.LoadError.Length == 0; i++) await Frames(1);
            Check(_world.WorldReady, "the room boots with its HUD and look: " + _world.LoadError);
            _hud = _world.GetNode<RoomHud>("RoomHud");
            await Frames(10);
            GD.Print($"PLAY_HUD_INFO: physics interpolation {GetTree().PhysicsInterpolation}, TAA {GetViewport().UseTaa}, screen-space AA {GetViewport().ScreenSpaceAA}");

            await TestFoldedHelp();
            TestViewKeys();
            TestWorkshopGone();
            await TestTimeOfDay();
            await TestSeason();
            await TestClockCombined();
            await TestNoLookNoClock();
            TestNameTag();
            await TestNameTagSize();
            await TestControlsMap();
            await TestHelpFollowsMap();
            await TestRemappedKeys();
            await TestHandKeys();

            CommandHost.SaveRoot = CommandHost.DefaultSaveRoot;
            RemoveSaves();
            GD.Print($"NATIVE_KERNEL_PLAY_HUD: {_checks - _failures}/{_checks} checks passed; every key is a named input action with today's default and the help reads them (a remapped key changes both what it does and what the help shows), H folds the help, Q and E turn the isometric view, T and Shift+T step the time of day and the season back to the real clock, the companion's solid name tag stays small and hides near the camera, and F and V pick up, carry, set down on the box and push");
            GetTree().Quit(_failures == 0 ? 0 : 1);
        }
        catch (Exception error)
        {
            GD.PushError("Play HUD test exception: " + error);
            GetTree().Quit(1);
        }
    }

    private static InputEventKey Press(Key key, bool shift = false) => new() { PhysicalKeycode = key, Pressed = true, ShiftPressed = shift };

    // ---- The controls are input actions (game/project.godot, [input]) ----

    /// <summary>Today's bindings, written out here on purpose: the map must hold exactly these until the founder changes a default.</summary>
    private static readonly (string Action, Key Key, bool Shift)[] DefaultKeys =
    {
        ("move_forward", Key.W, false), ("move_back", Key.S, false), ("move_left", Key.A, false), ("move_right", Key.D, false),
        ("move_sprint", Key.Shift, false), ("move_dive", Key.Ctrl, false), ("jump", Key.Space, false),
        ("body_recover", Key.R, false), ("body_home", Key.B, false),
        ("view_eye", Key.F1, false), ("view_shoulder", Key.F2, false), ("view_diorama", Key.F3, false), ("view_iso", Key.F4, false),
        ("view_observe", Key.O, false), ("iso_turn_left", Key.Q, false), ("iso_turn_right", Key.E, false),
        ("gubble_follow", Key.Key1, false), ("gubble_stay", Key.Key2, false), ("gubble_come", Key.Key3, false),
        ("gubble_stop", Key.Key4, false), ("gubble_point", Key.Key5, false),
        ("hands", Key.F, false), ("push", Key.V, false),
        ("physics_next", Key.G, false), ("time_step", Key.T, false), ("season_step", Key.T, true), ("lamps", Key.L, false),
        ("hud_help", Key.H, false), ("hud_customize", Key.C, false), ("mouse_release", Key.Escape, false),
    };

    private readonly System.Collections.Generic.Dictionary<string, Godot.Collections.Array<InputEvent>> _savedMap = new();

    /// <summary>The map as it was, so a test that remaps keys leaves every later test the defaults.</summary>
    private void SaveMap()
    {
        _savedMap.Clear();
        foreach (var action in PlayerControls.MapActions()) _savedMap[action] = InputMap.ActionGetEvents(action);
    }

    private void RestoreMap()
    {
        foreach (var (action, events) in _savedMap)
        {
            InputMap.ActionEraseEvents(action);
            foreach (var input in events) InputMap.ActionAddEvent(action, input);
        }
    }

    private static void Bind(StringName action, Key key, bool shift = false)
    {
        InputMap.ActionEraseEvents(action);
        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key, ShiftPressed = shift });
    }

    /// <summary>What an action's key is, as a comparable string: the kind, the key and the modifiers it asks for.</summary>
    private static string Signature(string action)
    {
        var events = InputMap.ActionGetEvents(action);
        if (events.Count != 1) return $"{events.Count} events";
        return events[0] switch
        {
            InputEventKey key => $"key {key.PhysicalKeycode}/{key.Keycode} mods {key.GetModifiersMask()}",
            InputEventMouseButton mouse => $"mouse {mouse.ButtonIndex} mods {mouse.GetModifiersMask()}",
            var other => other.GetType().Name,
        };
    }

    /// <summary>Pairs of actions live in the same context on one key. A nested pair (Shift+T over T) is allowed only when the code checks the longer one first.</summary>
    private static System.Collections.Generic.List<string> KeyConflicts()
    {
        var found = new System.Collections.Generic.List<string>();
        var contexts = new (string Name, ControlContext Live, ControlContext Extra)[]
        {
            ("always", ControlContext.Always, 0), ("F3", ControlContext.Always, ControlContext.Diorama),
            ("F4", ControlContext.Always, ControlContext.Isometric), ("customise", ControlContext.Customize, 0),
        };
        foreach (var (name, live, extra) in contexts)
        {
            var specs = PlayerControls.Specs.Where(spec => (spec.Live & (live | extra)) != 0).ToArray();
            for (var i = 0; i < specs.Length; i++)
                for (var j = i + 1; j < specs.Length; j++)
                {
                    var (a, b) = (specs[i].Action, specs[j].Action);
                    var (ka, kb) = (InputMap.ActionGetEvents(a).FirstOrDefault(), InputMap.ActionGetEvents(b).FirstOrDefault());
                    if (ka == null || kb == null) { found.Add($"{name}: {(ka == null ? a : b)} has no key"); continue; }
                    if (Same(ka, kb) is not { } relation) continue;
                    // relation 0: same key and modifiers; 1: a's modifiers contain b's; -1: b's contain a's.
                    var allowed = relation switch
                    {
                        1 => PlayerControls.CheckedBefore.Any(pair => pair.First == a && pair.Second == b),
                        -1 => PlayerControls.CheckedBefore.Any(pair => pair.First == b && pair.Second == a),
                        _ => false,
                    };
                    if (!allowed) found.Add($"{name}: {a} and {b} share a key");
                }
        }
        return found;
    }

    /// <summary>Null when the two are different keys; else how their modifiers relate (see KeyConflicts).</summary>
    private static int? Same(InputEvent a, InputEvent b)
    {
        (long Code, ulong Mods)? Of(InputEvent input) => input switch
        {
            InputEventKey key => (((long)(key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode)), (ulong)key.GetModifiersMask()),
            InputEventMouseButton mouse => (-1000L - (long)mouse.ButtonIndex, (ulong)mouse.GetModifiersMask()),
            _ => null,
        };
        if (Of(a) is not { } x || Of(b) is not { } y || x.Code != y.Code) return null;
        if (x.Mods == y.Mods) return 0;
        if ((x.Mods & y.Mods) == y.Mods) return 1;
        return (x.Mods & y.Mods) == x.Mods ? -1 : null;
    }

    private async Task TestControlsMap()
    {
        SaveMap();
        try
        {
            // The map holds today's keys exactly, and nothing else but the mouse's three.
            var wrong = DefaultKeys.Where(row =>
                InputMap.ActionGetEvents(row.Action) is not { Count: 1 } events || events[0] is not InputEventKey key ||
                key.PhysicalKeycode != row.Key || key.Keycode != Key.None || key.GetModifiersMask() != (row.Shift ? KeyModifierMask.MaskShift : 0)).Select(row => row.Action).ToArray();
            Check(wrong.Length == 0, "every key action is bound to today's physical key, with only the modifiers it had: " + string.Join(", ", wrong));
            Check(Signature("mouse_capture") == "mouse Left mods 0" && Signature("view_zoom_in") == "mouse WheelUp mods 0" && Signature("view_zoom_out") == "mouse WheelDown mods 0",
                "the left button captures the mouse and the wheel zooms: both are actions too");
            var mapped = PlayerControls.MapActions().ToArray();
            var named = DefaultKeys.Select(row => row.Action).Concat(new[] { "mouse_capture", "view_zoom_in", "view_zoom_out" }).Order().ToArray();
            Check(mapped.SequenceEqual(named), $"the map holds exactly the {named.Length} controls (no more, no fewer): {string.Join(", ", mapped.Except(named).Concat(named.Except(mapped)))}");
            var specs = PlayerControls.Specs.Select(spec => spec.Action.ToString()).Order().ToArray();
            var constants = typeof(Act).GetFields().Where(f => f.FieldType == typeof(StringName)).Select(f => f.GetValue(null)!.ToString()!).Order().ToArray();
            Check(specs.SequenceEqual(mapped) && constants.SequenceEqual(mapped), "the map, the list of controls and the action names in code are the same set");

            // The code asks for the map's actions and for no others: record what the two handlers ask while a key that is none of
            // them goes through the HUD (in F3, where the zoom keys are live) and the body, which also polls its held keys.
            var player = _world.Player;
            PlayerControls.Trace = new System.Collections.Generic.HashSet<string>();
            _hud.SetViewMode(2);
            _hud._UnhandledInput(Press(Key.Backslash));
            player._UnhandledInput(Press(Key.Backslash));
            await Frames(4);
            var asked = PlayerControls.Trace;
            PlayerControls.Trace = null;
            _hud.SetViewMode(1);
            Check(!asked.Except(mapped).Any(), "every action the code asks for is in the map: " + string.Join(", ", asked.Except(mapped)));
            Check(!mapped.Except(asked).Any(), "every action in the map is asked for by the code: " + string.Join(", ", mapped.Except(asked)));

            // No two keys of a context are one key.
            var conflicts = KeyConflicts();
            Check(conflicts.Count == 0, "no two actions share a key in the same context (always, F3, F4, the appearance panel): " + string.Join("; ", conflicts));
            Bind(Act.Push, Key.F);
            Check(KeyConflicts().Any(c => c.Contains("hands", StringComparison.Ordinal) && c.Contains("push", StringComparison.Ordinal)), "and the check does see two actions put on one key");
            RestoreMap();
            Bind(Act.IsoTurnLeft, Key.F);
            Check(KeyConflicts().Any(c => c.StartsWith("F4", StringComparison.Ordinal)) && !KeyConflicts().Any(c => c.StartsWith("F3", StringComparison.Ordinal)),
                "a key two contexts use for different things is no conflict: F4's turn on the hands' key conflicts in F4 only");
            RestoreMap();
            Check(KeyConflicts().Count == 0, "the defaults are back");

            // A synthetic key event that names only the key's meaning (Keycode), not its place, is read as the place, as it was before the actions.
            var folded = _hud.GetNode<PanelContainer>("HelpFooter").GetChild<VBoxContainer>(0).GetNode<VBoxContainer>("KeyHelp").Visible;
            _hud._UnhandledInput(new InputEventKey { Keycode = Key.H, Pressed = true });
            var unfolded = _hud.GetNode<PanelContainer>("HelpFooter").GetChild<VBoxContainer>(0).GetNode<VBoxContainer>("KeyHelp").Visible;
            _hud._UnhandledInput(new InputEventKey { Keycode = Key.H, Pressed = true });
            Check(!folded && unfolded && !_hud.GetNode<PanelContainer>("HelpFooter").GetChild<VBoxContainer>(0).GetNode<VBoxContainer>("KeyHelp").Visible,
                "an event with only a Keycode (no physical key) still presses the key it names");
        }
        finally { RestoreMap(); PlayerControls.Trace = null; }
    }

    /// <summary>The key help, as it reads now: the hint, then each help line in order (the buttons are read separately).</summary>
    private string[] HelpLines()
    {
        var column = _hud.GetNode<PanelContainer>("HelpFooter").GetChild<VBoxContainer>(0);
        return new[] { column.GetChild<Label>(0).Text }.Concat(column.GetNode<VBoxContainer>("KeyHelp").GetChildren().OfType<Label>().Select(l => l.Text)).ToArray();
    }

    private string[] ButtonTexts(string row) => _hud.FindChild(row, true, false)!.GetChildren().OfType<Button>().Select(b => b.Text).ToArray();

    /// <summary>The help is built from the map: the same words as before for the default keys, and a remapped key shows in them.</summary>
    private async Task TestHelpFollowsMap()
    {
        SaveMap();
        try
        {
            var home = _world.Player.HomeName;
            await Frames(2);
            var expected = new[]
            {
                "H keys",
                $"WASD move · Shift run · Space jump · B back to {home} · R recover · click to look · Esc release",
                "Climb: keep walking into a steep face, W up, S down, A/D across, Space lets go · Swim: deep water floats you, Space leaps · Dive: hold Ctrl, Space rises, W swims the way you look, let go to drift up",
                "1 follow · 2 wait · 3 come · 4 stop · 5 point: the Gubble floats after you, over water and up cliffs",
                "F1 eye · F2 shoulder · F3 diorama: mouse orbits, wheel zooms, WASD follows the view · F4 isometric: Q/E turn the view",
                "O observe (a very tight tilt-shift view, best from F3 or F4) · C customize",
                "F pick up what you face · F again sets it down in front of you, or on top of what you face (the box, the book) · V push what you face 10 cm",
                "Testing",
                "G gravity · T time of day · Shift+T season (each steps round to the real clock) · L lamps",
            };
            var lines = HelpLines();
            Check(lines.SequenceEqual(expected), "with the default keys the help says what it said before, the testing toggles (G, T, Shift+T, L) now under a \"Testing\" heading:\n" + string.Join("\n", lines.Except(expected)));
            Check(ButtonTexts("CompanionActions").SequenceEqual(new[] { "1 Follow", "2 Wait", "3 Come", "4 Stop", "5 Point", "Customize" }) &&
                ButtonTexts("HandActions").SequenceEqual(new[] { "F Pick up / put down", "V Push" }), "and so do the buttons of the top panel");
            var state = ((Label)_hud.FindChild("State", true, false)!).Text;
            Check(state.Contains(" (G)", StringComparison.Ordinal) && _hud.ClockText().Contains("real clock (T)", StringComparison.Ordinal) && _hud.ClockText().Contains("real date (Shift+T)", StringComparison.Ordinal),
                $"and the state and clock lines name G, T and Shift+T: {state} | {_hud.ClockText()}");

            // Every control on a key of its own that is in no other word of the help: each shows its own key, and the old keys are gone.
            var pool = new[]
            {
                Key.F13, Key.F14, Key.F15, Key.F16, Key.F17, Key.F18, Key.F19, Key.F20, Key.F21, Key.F22, Key.F23, Key.F24,
                Key.Kp0, Key.Kp1, Key.Kp2, Key.Kp3, Key.Kp4, Key.Kp5, Key.Kp6, Key.Kp7, Key.Kp8, Key.Kp9,
                Key.KpAdd, Key.KpSubtract, Key.KpMultiply, Key.KpDivide, Key.KpPeriod, Key.KpEnter, Key.Insert, Key.Delete, Key.Pause, Key.Pageup, Key.Pagedown,
            };
            var actions = PlayerControls.Specs.Select(spec => spec.Action).ToArray();
            Check(pool.Length >= actions.Length, "enough spare keys to give every control its own");
            var own = actions.Select((action, i) => (Action: action, Key: pool[i], Name: OS.GetKeycodeString(pool[i]))).ToArray();
            foreach (var (action, key, _) in own) Bind(action, key);
            await Frames(2);
            var text = string.Join("\n", HelpLines());
            var missing = own.Where(o => !text.Contains(o.Name, StringComparison.Ordinal)).Select(o => $"{o.Action} ({o.Name})").ToArray();
            Check(missing.Length == 0, $"the help lists every one of the {own.Length} controls with its current key: missing {string.Join(", ", missing)}");
            Check(!text.Contains("WASD", StringComparison.Ordinal) && !text.Contains("Shift+T", StringComparison.Ordinal) && !text.Contains("Esc", StringComparison.Ordinal) && !text.Contains("click", StringComparison.Ordinal),
                "and none of the old keys is left in it");
            Check(ButtonTexts("CompanionActions")[0] == $"{own.First(o => o.Action == Act.GubbleFollow).Name} Follow", "the top panel's buttons follow the map too");
            var ownLabels = actions.Select(a => (Action: a, Label: PlayerControls.Label(a))).ToArray();
            Check(own.All(o => ownLabels.First(l => l.Action == o.Action).Label == o.Name), "Label(action) is the action's current key, named as the engine names it");
            RestoreMap();
            await Frames(2);
            Check(HelpLines().SequenceEqual(expected), "and back to the default keys, the help reads as before");
            Bind(Act.SeasonStep, Key.Y, shift: true);
            Check(PlayerControls.Label(Act.SeasonStep) == "Shift+Y", "a key with a modifier is named with it");
        }
        finally { RestoreMap(); }
    }

    /// <summary>Remapping an action changes what the key does (the old key goes dead) and what the help says, in the contexts it is live in.</summary>
    private async Task TestRemappedKeys()
    {
        SaveMap();
        var companion = _world.Companion;
        try
        {
            // A companion key: the default does it, then the same action on another key does it and the old key does not.
            _hud._UnhandledInput(Press(Key.Key2));
            Check(companion.CurrentIntent == "stay", "the Gubble waits (2)");
            _hud._UnhandledInput(Press(Key.Key1));
            Check(companion.CurrentIntent == "follow", "1 sends it following");
            _hud._UnhandledInput(Press(Key.Key2));
            Bind(Act.GubbleFollow, Key.J);
            await Frames(2);
            _hud._UnhandledInput(Press(Key.Key1));
            Check(companion.CurrentIntent == "stay", "with follow on J, 1 does nothing");
            _hud._UnhandledInput(Press(Key.J));
            Check(companion.CurrentIntent == "follow", "J sends it following");
            var echo = Press(Key.Key2); echo.Echo = true;
            _hud._UnhandledInput(echo);
            Check(companion.CurrentIntent == "follow", "a held key repeating (echo) does not count as a press");
            _hud._UnhandledInput(Press(Key.Key2));
            Check(HelpLines().Any(l => l.StartsWith("J follow · 2 wait · 3 come", StringComparison.Ordinal)) && ButtonTexts("CompanionActions")[0] == "J Follow",
                "and the help and the button say J follow");
            RestoreMap();

            // Time of day: T moves to Y, and Shift+T is still the season.
            var look = _world.Look;
            Bind(Act.TimeStep, Key.Y);
            await Frames(2);
            _hud._UnhandledInput(Press(Key.T));
            Check(_hud.TimeStop == -1 && _hud.SeasonStop == -1, "with time of day on Y, T steps nothing");
            _hud._UnhandledInput(Press(Key.Y));
            Check(_hud.TimeStop == 0 && _hud.SeasonStop == -1, "Y steps the time of day");
            _hud._UnhandledInput(Press(Key.T, shift: true));
            Check(_hud.TimeStop == 0 && _hud.SeasonStop == 0, "Shift+T is still the season");
            Check(HelpLines().Any(l => l.Contains("Y time of day · Shift+T season", StringComparison.Ordinal)) && _hud.ClockText().Contains("dawn (Y)", StringComparison.Ordinal),
                "and the help and the clock line say Y");
            for (var i = 0; i < RoomHud.TimeStopNames.Length; i++) _hud._UnhandledInput(Press(Key.Y));
            for (var i = 0; i < RoomHud.SeasonStops.Length; i++) _hud._UnhandledInput(Press(Key.T, shift: true));
            await Frames(2);
            Check(_hud.TimeStop == -1 && _hud.SeasonStop == -1 && look.ClockNote.StartsWith("real clock, real calendar", StringComparison.Ordinal), "both stepped round to the real clock again");
            RestoreMap();

            // Q and E (here Z and X) belong to F4 alone, wherever they are bound.
            Bind(Act.IsoTurnLeft, Key.Z);
            _hud.SetViewMode(3);
            var yaw = _hud.IsoYaw;
            _hud._UnhandledInput(Press(Key.Q));
            Check(Mathf.IsEqualApprox(yaw, _hud.IsoYaw), "with the left turn on Z, Q turns nothing in F4");
            _hud._UnhandledInput(Press(Key.Z));
            Check(Mathf.Abs(Mathf.AngleDifference(yaw, _hud.IsoYaw) + Mathf.Pi / 2) < 0.001f, "Z turns the view the way Q did");
            _hud.SetViewMode(1);
            yaw = _hud.IsoYaw;
            _hud._UnhandledInput(Press(Key.Z));
            Check(Mathf.IsEqualApprox(yaw, _hud.IsoYaw), "and outside F4 Z turns nothing");
            RestoreMap();

            // The wheel zooms F3 only; moved to keys it zooms the same way, and the wheel goes quiet.
            Func<MouseButton, InputEventMouseButton> wheel = which => new InputEventMouseButton { ButtonIndex = which, Pressed = true };
            _hud.SetViewMode(1);
            var distance = _hud.DioramaDistanceM;
            _hud._UnhandledInput(wheel(MouseButton.WheelUp));
            Check(Mathf.IsEqualApprox(distance, _hud.DioramaDistanceM), "the wheel zooms nothing outside F3");
            _hud.SetViewMode(2);
            distance = _hud.DioramaDistanceM;
            _hud._UnhandledInput(wheel(MouseButton.WheelUp));
            var closer = _hud.DioramaDistanceM;
            _hud._UnhandledInput(wheel(MouseButton.WheelDown));
            Check(closer < distance - 0.01f && Mathf.IsEqualApprox(distance, _hud.DioramaDistanceM), $"in F3 the wheel zooms in and out ({distance:0.00} to {closer:0.00} m and back)");
            Bind(Act.ViewZoomIn, Key.U);
            await Frames(2);
            _hud._UnhandledInput(wheel(MouseButton.WheelUp));
            Check(Mathf.IsEqualApprox(distance, _hud.DioramaDistanceM), "with zoom-in on U the wheel up zooms nothing");
            _hud._UnhandledInput(Press(Key.U));
            Check(_hud.DioramaDistanceM < distance - 0.01f, "U zooms in");
            Check(HelpLines().Any(l => l.Contains("mouse orbits, U/wheel down zooms", StringComparison.Ordinal)), "and the help names both zoom keys");
            RestoreMap();
            var zoomed = _hud.DioramaDistanceM;
            _hud._UnhandledInput(wheel(MouseButton.WheelUp));
            Check(_hud.DioramaDistanceM < zoomed - 0.01f, "the wheel zooms again with the defaults back");
            _hud.ZoomDiorama(Mathf.Log(distance / _hud.DioramaDistanceM) / Mathf.Log(0.88f));
            _hud.SetViewMode(1);
            Check(Mathf.IsEqualApprox(_hud.DioramaDistanceM, distance), "(the zoom put back where it was)");

            // Esc and the appearance panel: the panel blocks every key but its own two.
            Bind(Act.HudCustomize, Key.K);
            _hud._UnhandledInput(Press(Key.C));
            Check(!_hud.Customizing, "with the panel on K, C opens nothing");
            _hud._UnhandledInput(Press(Key.K));
            Check(_hud.Customizing, "K opens the panel");
            var before = _hud.ViewMode;
            _hud._UnhandledInput(Press(Key.F3));
            Check(_hud.ViewMode == before, "and with it open the view keys do nothing");
            _hud._UnhandledInput(Press(Key.Escape));
            Check(!_hud.Customizing, "Esc closes the panel");
            RestoreMap();
        }
        finally { RestoreMap(); _hud.SetViewMode(1); if (_hud.Customizing) _hud._UnhandledInput(Press(Key.C)); }
    }

    /// <summary>
    /// P3's acceptance in the real room through the HUD's keys, the same commands the companion sends: the player picks up the
    /// doorstop (F), carries it across the room to the big box and sets it on top (F, a durable receipt), pushes it along the
    /// box (V twice), and is told plainly when what it faces is too heavy to pick up.
    /// </summary>
    private async Task TestHandKeys()
    {
        var host = CommandHost.Of(_world)!;
        var player = _world.Player;
        player.ReadKeyboard = false;
        _hud.SetViewMode(1);
        var notice = (Label)_hud.FindChild("Notice", true, false)!;
        var state = (Label)_hud.FindChild("State", true, false)!;
        var help = string.Join("\n", _world.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text));
        Check(help.Contains("F pick up", StringComparison.Ordinal) && help.Contains("V push", StringComparison.Ordinal), "the help names the hand keys F and V");
        Check(_hud.FindChild("HandActions", true, false) is HBoxContainer { } buttons && buttons.GetChildCount() == 2, "and the top panel has their buttons");
        // The founder's name for the companion (8 October): the Gubble, in its name tag, the HUD's state line and the help.
        await Frames(1);
        var tag = _world.Companion.GetNode<Label3D>("CompanionLabel").Text;
        Check(_world.Companion.CompanionName == CompanionAvatar.DefaultName && tag == "The Gubble" && state.Text.Contains("the Gubble: ", StringComparison.Ordinal) &&
            help.Contains("the Gubble", StringComparison.Ordinal) && !help.Contains("Wisp", StringComparison.Ordinal),
            $"the companion is the Gubble in its name tag ({tag}), the state line and the help ({state.Text})");
        Check(_hud.FindChild("HomeVeil", true, false) is ColorRect { Color.A: 0, MouseFilter: Control.MouseFilterEnum.Ignore },
            "the veil of the way home is in place, clear and letting clicks through while nobody is going home");
        Check(help.Contains("B back to the start", StringComparison.Ordinal),
            "the help lists B, home from anywhere, all the time (a room without a sea: back to the start)");
        Check(CompanionAvatar.SavedName("Wisp") == "the Gubble" && CompanionAvatar.SavedName("Pip") == "Pip",
            "a profile or save still naming the companion Wisp (the former default) restores it as the Gubble; a name the player chose stays");
        Check(await WalkTo(player, new Vector2(-0.3f, 0.62f)), "the player walks over to the doorstop on the rug");
        // The focus (RUN-2-OPEN-SEA.md, "Things to touch"): facing away, nothing lit; facing the doorstop, it is lit, and its
        // tag beside it names the keys, never in the top-right corner kept for the minimap.
        await Face(player, Vector3.Back);
        await Frames(40);
        var look = _hud.Look;
        Check(_hud.FocusTarget == null && !_hud.FocusTag.Visible && (look == null || look.Highlight.Target == null),
            "with nothing the hand keys would act on in front, nothing is lit and no tag shows");
        await Face(player, Vector3.Forward);
        await Frames(40);
        var tagRect = new Rect2(_hud.FocusTag.Position, _hud.FocusTag.Size);
        var viewport = _hud.GetViewport().GetVisibleRect().Size;
        var inCorner = tagRect.End.X > viewport.X - SandboxControls.FocusWords.MinimapWidthPx && tagRect.Position.Y < SandboxControls.FocusWords.MinimapHeightPx;
        Check(_hud.FocusTarget?.GetMeta("entity_id", "").AsString() == "obj:doorstop" && (look == null || look.Highlight.Target == _hud.FocusTarget) &&
              _hud.FocusTag.Visible && _hud.FocusTag.Text == SandboxControls.FocusWords.PickUpOrPush && !inCorner,
            $"facing the doorstop it is lit ({(look == null ? "no look in this run" : "the look's highlight on it")}), its tag \"{_hud.FocusTag.Text}\" at {tagRect.Position}, clear of the minimap's corner");
        var queries = _hud.FocusQueries;
        await Frames(60);
        Check(_hud.FocusQueries - queries <= 3, $"standing still, the focus is asked for at most every half second ({_hud.FocusQueries - queries} times in 1 s)");
        _hud._UnhandledInput(Press(Key.F));
        await Frames(2);
        Check(host.HeldBy(CommandHost.PlayerAvatar) == "obj:doorstop" && notice.Text.StartsWith("Holding Doorstop", StringComparison.Ordinal) && state.Text.Contains("holding Doorstop (F)", StringComparison.Ordinal),
            $"F picks up the doorstop it faces; the HUD says so ({notice.Text} | {state.Text})");
        var revision = host.Revision;
        Check(await WalkTo(player, new Vector2(0.87f, 0.17f)), "the player carries it across the room to the big box");
        await Face(player, Vector3.Right);
        Check(Entity(host, "obj:doorstop")["held_by"]?.GetValue<string>() == CommandHost.PlayerAvatar, "still holding it on arrival");
        await Frames(40);
        Check(_hud.FocusTarget?.GetMeta("entity_id", "").AsString() == "obj:box" && _hud.FocusTag.Text == "F set Doorstop on Cardboard box",
            $"carrying it, facing the box, the box is lit, where F would set it: \"{_hud.FocusTag.Text}\" on {_hud.FocusTarget?.GetMeta("entity_id", "")}");
        _hud._UnhandledInput(Press(Key.F));
        await Frames(2);
        var onBox = Entity(host, "obj:doorstop");
        var at = Vec(onBox["position_m"]!);
        Check(host.HeldBy(CommandHost.PlayerAvatar) == null && onBox["held_by"] == null && Mathf.Abs(at.Y - 0.30f) < 0.0005f && at.X > 0.925f && at.X < 1.275f && at.Z > 0.025f && at.Z < 0.375f,
            $"F facing the box sets the doorstop on its top: {onBox["position_m"]!.ToJsonString()} ({notice.Text})");
        Check(host.Revision == revision + 1 && host.EntityRevision("obj:doorstop") == 1 && notice.Text == "Set Doorstop on Cardboard box.",
            "through a command with a durable receipt: the room's revision and the doorstop's moved by one");
        var saved = System.IO.File.ReadAllText(ProjectSettings.GlobalizePath(CommandHost.SavePathFor(_world.Room)));
        Check(saved.Contains("\"object_poses\"", StringComparison.Ordinal) && saved.Contains("\"obj:doorstop\"", StringComparison.Ordinal) && saved.Contains("entity.release", StringComparison.Ordinal),
            "the save holds its new place and the receipt");
        _hud._UnhandledInput(Press(Key.V));
        await Frames(2);
        _hud._UnhandledInput(Press(Key.V));
        await Frames(2);
        var pushed = Vec(Entity(host, "obj:doorstop")["position_m"]!);
        Check(Mathf.Abs(pushed.X - at.X - 0.2f) < 0.002f && Mathf.Abs(pushed.Y - 0.30f) < 0.0005f && Mathf.Abs(pushed.Z - at.Z) < 0.002f && notice.Text == "Pushed Doorstop.",
            $"V twice pushes it 20 cm along the box's top ({at.X:0.000} to {pushed.X:0.000} m)");
        await Frames(40);
        var heavyTag = _hud.FocusTag.Text;
        _hud._UnhandledInput(Press(Key.F));
        await Frames(2);
        Check(host.HeldBy(CommandHost.PlayerAvatar) == null && notice.Text.Contains("too heavy", StringComparison.Ordinal), "F on the big box says it is too heavy: " + notice.Text);
        Check(heavyTag.Contains("too heavy", StringComparison.Ordinal), $"and its tag said so before the key: \"{heavyTag}\"");
        // On the way home (B), as while climbing or diving, nothing is lit and no tag shows.
        Check(_hud.FocusTarget != null && player.RequestHome(), "still facing the box, B starts the way home");
        await Frames(3);
        var litOnTheWay = _hud.FocusTarget != null || _hud.FocusTag.Visible || (_hud.Look != null && _hud.Look.Highlight.Target != null);
        await Frames(80);
        Check(!litOnTheWay, "during the fade home nothing is lit and the tag is gone");
    }

    private static System.Text.Json.Nodes.JsonObject Entity(CommandHost host, string id) => host.Entities().First(e => e["id"]!.GetValue<string>() == id);

    private static Vector3 Vec(System.Text.Json.Nodes.JsonNode node) => new((float)node[0]!.GetValue<double>(), (float)node[1]!.GetValue<double>(), (float)node[2]!.GetValue<double>());

    private async Task Face(SmallPlayerController body, Vector3 facing)
    {
        body.Rotation = new Vector3(0, Mathf.Atan2(-facing.X, -facing.Z), 0);
        await Frames(4);
    }

    /// <summary>Walk a body to a spot on the floor plane, steering each tick: running while far, slowing near it.</summary>
    private async Task<bool> WalkTo(SmallPlayerController body, Vector2 target, int maxFrames = 900)
    {
        for (var i = 0; i < maxFrames; i++)
        {
            var to = target - new Vector2(body.GlobalPosition.X, body.GlobalPosition.Z);
            if (to.Length() < 0.008f)
            {
                body.SetControlInput(Vector2.Zero);
                await Frames(20);
                return true;
            }
            body.Rotation = new Vector3(0, Mathf.Atan2(-to.X, -to.Y), 0);
            body.SetControlInput(new Vector2(0, to.Length() > 0.1f ? 1 : 0.35f), sprint: to.Length() > 0.3f);
            await Frames(1);
        }
        body.SetControlInput(Vector2.Zero);
        return false;
    }

    private static void RemoveSaves()
    {
        var directory = ProjectSettings.GlobalizePath(HudSaves);
        if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true);
    }

    private async Task TestFoldedHelp()
    {
        var footer = _hud.GetNode<PanelContainer>("HelpFooter");
        var column = footer.GetChild<VBoxContainer>(0);
        var help = column.GetNode<VBoxContainer>("KeyHelp");
        var notice = column.GetNode<Label>("Notice");
        var hint = column.GetChild<Label>(0);
        var foldedHeight = footer.Size.Y;
        var noticeText = notice.Text;
        Check(!help.Visible && hint.Text == "H keys", "key help starts folded with one short H hint");
        Check(notice.IsVisibleInTree() && noticeText.Length > 0, "the notice is visible with the help folded");
        _hud._UnhandledInput(Press(Key.H));
        await Frames(3);
        Check(help.IsVisibleInTree() && help.GetChildren().OfType<Label>().All(l => l.IsVisibleInTree()), "H unfolds every key-help line");
        Check(footer.Size.Y > foldedHeight, "the footer grows to fit the unfolded help");
        Check(notice.IsVisibleInTree() && notice.Text == noticeText, "unfolding the help keeps the same notice visible");
        var repeat = Press(Key.H); repeat.Echo = true;
        _hud._UnhandledInput(repeat);
        Check(help.Visible, "holding H does not repeatedly toggle the help");
        _hud._UnhandledInput(Press(Key.H));
        await Frames(3);
        Check(!help.Visible && hint.Text == "H keys", "H refolds the key help");
        Check(Mathf.Abs(footer.Size.Y - foldedHeight) < 1, "the folded footer releases the space used by the full help");
        Check(notice.IsVisibleInTree() && notice.Text == noticeText, "refolding the help keeps the notice visible");
    }

    private void TestViewKeys()
    {
        _hud.SetViewMode(3);
        var yaw = _hud.IsoYaw;
        _hud._UnhandledInput(Press(Key.E));
        Check(Mathf.Abs(Mathf.AngleDifference(yaw, _hud.IsoYaw) - Mathf.Pi / 2) < 0.001f, "E turns the isometric view a quarter turn");
        _hud._UnhandledInput(Press(Key.Q));
        _hud._UnhandledInput(Press(Key.Q));
        Check(Mathf.Abs(Mathf.AngleDifference(yaw, _hud.IsoYaw) + Mathf.Pi / 2) < 0.001f, "Q turns it a quarter turn the other way");
        Check(_world.Player.MovementFrameYaw is { } frame && Mathf.IsEqualApprox(frame, _hud.IsoYaw), "movement follows the turned view");
        _hud.SetViewMode(1);
        var before = _hud.IsoYaw;
        _hud._UnhandledInput(Press(Key.Q));
        _hud._UnhandledInput(Press(Key.E));
        Check(Mathf.IsEqualApprox(before, _hud.IsoYaw), "outside the isometric view Q and E turn nothing");
    }

    private void TestWorkshopGone()
    {
        var texts = _world.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text).ToArray();
        Check(!texts.Any(t => t.Contains("INVENTIONS", StringComparison.Ordinal) || t.Contains("worn design", StringComparison.Ordinal)), "the room shows no INVENTIONS panel");
        var help = string.Join("\n", texts);
        Check(help.Contains("Q/E turn the view", StringComparison.Ordinal) && help.Contains("T time of day", StringComparison.Ordinal) && help.Contains("Shift+T season", StringComparison.Ordinal),
            "the help lines name Q and E, T and Shift+T");
        Check(!help.Contains("B Build", StringComparison.Ordinal), "and say nothing of the retired build keys");
    }

    /// <summary>T: seven times of day on the day shown (dawn to night, in order, lighter by day), then the real clock.</summary>
    private async Task TestTimeOfDay()
    {
        var look = _world.Look;
        Check(_hud.TimeStop == -1 && _hud.SeasonStop == -1 && look.ClockNote.StartsWith("real clock", StringComparison.Ordinal), "the room starts on the real clock and calendar: " + look.ClockNote);
        Check(_hud.ClockText().Contains("real clock", StringComparison.Ordinal) && _hud.ClockText().Contains("real date", StringComparison.Ordinal), "the HUD says so: " + _hud.ClockText());
        var hours = new float[RoomHud.TimeStopNames.Length];
        var daylight = new float[hours.Length];
        for (var stop = 0; stop < hours.Length; stop++)
        {
            _hud._UnhandledInput(Press(Key.T));
            await Frames(2);
            hours[stop] = look.Moment.Hour;
            daylight[stop] = look.Moment.Daylight;
            Check(_hud.TimeStop == stop && Mathf.Abs(hours[stop] - RoomHud.TimeStopHour(look.Preset, stop, look.Moment.DayOfYear)) < 0.01f && look.ClockNote.Contains("hour pinned", StringComparison.Ordinal),
                $"T step {stop + 1} is {RoomHud.TimeStopNames[stop]} ({hours[stop]:0.00} h, daylight {daylight[stop]:0.00}); the look is pinned to it");
            Check(_hud.ClockText().Contains(RoomHud.TimeStopNames[stop], StringComparison.Ordinal) && _hud.ClockText().StartsWith($"{(int)hours[stop]:00}:", StringComparison.Ordinal), "the HUD shows that time: " + _hud.ClockText());
        }
        // In order through the day (an hour past midnight for night is allowed to wrap), light by day and dark at night.
        var unwrapped = hours.Select((h, i) => i > 0 && h < hours[0] ? h + 24f : h).ToArray();
        Check(unwrapped.Zip(unwrapped.Skip(1), (a, b) => b > a).All(ok => ok), "the seven times run in order: " + string.Join(", ", hours.Select(h => h.ToString("0.0"))));
        Check(daylight[2] >= daylight[1] && daylight[2] >= daylight[3] && daylight[3] > daylight[4] && daylight[4] > daylight[6], $"noon is the brightest and the light falls through the afternoon to sunset ({string.Join(" ", daylight.Select(d => d.ToString("0.00")))})");
        Check(daylight[6] < 0.1f && daylight[5] <= daylight[4] && look.Moment.MoonWeight > 0.9f, "night is dark and lit by the moon");
        Check(look.LampsOn, "and the lamps are on at night");
        _hud._UnhandledInput(Press(Key.T));
        await Frames(2);
        Check(_hud.TimeStop == -1 && look.ClockNote.StartsWith("real clock", StringComparison.Ordinal), "one more T returns to the real clock: " + look.ClockNote);
    }

    /// <summary>Shift+T: the four solstices and equinoxes in order, then the real calendar.</summary>
    private async Task TestSeason()
    {
        var look = _world.Look;
        var seasons = new[] { "spring", "summer", "autumn", "winter" };
        var lengths = new float[4];
        for (var stop = 0; stop < RoomHud.SeasonStops.Length; stop++)
        {
            _hud._UnhandledInput(Press(Key.T, shift: true));
            await Frames(2);
            lengths[stop] = LookClock.DayLength(look.Preset, look.Moment.DayOfYear);
            Check(_hud.SeasonStop == stop && look.Moment.DayOfYear == RoomHud.SeasonStops[stop].DayOfYear && look.Moment.Season == seasons[stop],
                $"Shift+T step {stop + 1} is the {RoomHud.SeasonStops[stop].Name} (day {look.Moment.DayOfYear}, {look.Moment.Season}, a {lengths[stop]:0.0} h day)");
            Check(_hud.ClockText().Contains(RoomHud.SeasonStops[stop].Name, StringComparison.Ordinal) && _hud.ClockText().Contains(look.Moment.Season, StringComparison.Ordinal), "the HUD shows it: " + _hud.ClockText());
            Check(_hud.TimeStop == -1, "stepping the season does not step the time");
        }
        Check(lengths[1] > lengths[0] && lengths[0] > lengths[3] && lengths[2] > lengths[3], "the June day is the longest and the December day the shortest");
        _hud._UnhandledInput(Press(Key.T, shift: true));
        await Frames(2);
        Check(_hud.SeasonStop == -1 && look.ClockNote.StartsWith("real clock, real calendar", StringComparison.Ordinal), "one more Shift+T returns to the real calendar: " + look.ClockNote);
    }

    /// <summary>The two steps together: each time of day is found on the pinned date, and releasing one leaves the other pinned.</summary>
    private async Task TestClockCombined()
    {
        var look = _world.Look;
        _hud._UnhandledInput(Press(Key.T, shift: true));
        _hud._UnhandledInput(Press(Key.T, shift: true));
        _hud._UnhandledInput(Press(Key.T));
        _hud._UnhandledInput(Press(Key.T));
        _hud._UnhandledInput(Press(Key.T));
        await Frames(2);
        var june = RoomHud.SeasonStops[1].DayOfYear;
        Check(look.Moment.DayOfYear == june && Mathf.Abs(look.Moment.Hour - RoomHud.TimeStopHour(look.Preset, 2, june)) < 0.01f,
            $"noon on the June solstice is found from that day's own sun, not the real date's ({look.Moment.Hour:0.00} h)");
        var (rise, set) = LookClock.SunTimes(look.Preset.Tuning.Sun, june);
        Check(Mathf.Abs(look.Moment.Hour - (rise + set) * 0.5f) < 0.01f && look.Moment.SunElevationDeg > 70f, $"which is high sun (elevation {look.Moment.SunElevationDeg:0} degrees)");
        for (var i = 0; i < 5; i++) _hud._UnhandledInput(Press(Key.T));
        await Frames(2);
        Check(_hud.TimeStop == -1 && _hud.SeasonStop == 1 && look.Moment.DayOfYear == june && !look.ClockNote.Contains("real calendar", StringComparison.Ordinal),
            "back on the real clock the date stays on the June solstice");
        // Esc-style safety: keys do nothing while the appearance panel owns the keyboard.
        _hud._UnhandledInput(Press(Key.C));
        var stop = _hud.TimeStop;
        _hud._UnhandledInput(Press(Key.T));
        Check(_hud.Customizing && _hud.TimeStop == stop, "T does nothing while the appearance panel is open");
        _hud._UnhandledInput(Press(Key.C));
        for (var i = 0; i < 3; i++) _hud._UnhandledInput(Press(Key.T, shift: true));
        await Frames(2);
        Check(_hud.SeasonStop == -1 && _hud.TimeStop == -1 && look.ClockNote.StartsWith("real clock, real calendar", StringComparison.Ordinal), "both released, the look follows the real clock again: " + look.ClockNote);
    }

    private async Task TestNoLookNoClock()
    {
        var world = new Node3D();
        AddChild(world);
        var player = new SmallPlayerController { ReadKeyboard = false };
        world.AddChild(player);
        var companion = new CompanionAvatar();
        world.AddChild(companion);
        var hud = new RoomHud { Player = player, Companion = companion, RoomTitle = "NO LOOK" };
        world.AddChild(hud);
        await Frames(2);
        hud._UnhandledInput(Press(Key.T));
        hud._UnhandledInput(Press(Key.T, shift: true));
        Check(hud.TimeStop == -1 && hud.SeasonStop == -1 && hud.ClockText().Length == 0, "without a look the clock keys do nothing and the HUD shows no clock");
        world.QueueFree();
    }

    /// <summary>
    /// The name tag over the companion was see-through: depth of field read the depth behind it and blurred it, and temporal
    /// anti-aliasing smeared it in motion. Drawn with an alpha cut it writes depth and motion like a solid, so both treat it as an object.
    /// </summary>
    private void TestNameTag()
    {
        var label = _world.Companion.GetNode<Label3D>("CompanionLabel");
        Check(label.AlphaCut != Label3D.AlphaCutMode.Disabled, $"the name tag is drawn solid (alpha cut {label.AlphaCut}), not as see-through blending");
        Check(!label.NoDepthTest && label.Billboard == BaseMaterial3D.BillboardModeEnum.Enabled, "it still faces the camera and hides behind what is in front of it");
    }

    private async Task TestNameTagSize()
    {
        // An isolated, stationary fixture avoids moving the room's avatars or changing its cameras.
        var fixture = new Node3D();
        AddChild(fixture);
        var companion = new CompanionAvatar();
        fixture.AddChild(companion);
        companion.SetPhysicsProcess(false);
        var camera = new Camera3D { Near = 0.005f };
        fixture.AddChild(camera);
        camera.MakeCurrent();
        var label = companion.GetNode<Label3D>("CompanionLabel");
        Check(label.FixedSize, "the tag uses distance-independent rendering");
        foreach (var (view, distance, fov) in new[]
        {
            ("F2 close", 0.26f, 68f), ("F2", 0.5f, 68f),
            ("F3 near", RoomHud.DioramaMinDistanceM, RoomHud.DioramaFovDeg),
            ("F3", RoomHud.DioramaDefaultDistanceM, RoomHud.DioramaFovDeg),
            ("F3 far", RoomHud.DioramaMaxDistanceM, RoomHud.DioramaFovDeg),
            ("F4", RoomHud.IsoDistanceM, RoomHud.IsoFovDeg)
        })
        {
            camera.Position = Vector3.Back * distance;
            camera.Fov = fov;
            await Frames(3);
            var fraction = ProjectedTagHeight(label, camera);
            Check(label.Visible && fraction > 0.015f && fraction <= 0.03f,
                $"{view}: tag is readable and below 3% screen height at {distance:0.00} m ({fraction * 100:0.00}%)");
        }
        // Include the maximum allowed name length so its new glyph bounds are also sized.
        companion.SetDisplayName(new string('W', 40));
        await Frames(3);
        Check(ProjectedTagHeight(label, camera) <= 0.03f, "a renamed tag stays below 3% screen height");
        foreach (var distance in new[] { 0.249f, 0.1f, 0.01f })
        {
            camera.Position = Vector3.Back * distance;
            await Frames(2);
            Check(!label.Visible, $"the tag hides inside 0.25 m ({distance:0.000} m)");
        }
        camera.Position = Vector3.Back * 0.251f;
        await Frames(2);
        Check(label.Visible && ProjectedTagHeight(label, camera) <= 0.03f, "the tag reappears small just outside 0.25 m");
        _hud.SetViewMode(1);
        fixture.QueueFree();
    }

    // Label3D.FixedSize renders its geometry as if its origin were one metre into the view.
    // Project its actual bounds (with an outline allowance) using the camera's billboard axes;
    // this checks projected size without needing GPU pixels in the headless suite.
    private static float ProjectedTagHeight(Label3D label, Camera3D camera)
    {
        var screen = camera.GetViewport().GetVisibleRect();
        var origin = camera.ProjectPosition(screen.GetCenter(), 1);
        var bounds = label.GetAabb();
        var outline = label.OutlineSize * label.PixelSize;
        var up = camera.GetCameraTransform().Basis.Y.Normalized() * label.GlobalBasis.Scale.Y;
        var top = camera.UnprojectPosition(origin + up * (bounds.End.Y + outline));
        var bottom = camera.UnprojectPosition(origin + up * (bounds.Position.Y - outline));
        return Mathf.Abs(top.Y - bottom.Y) / screen.Size.Y;
    }

    private async Task Frames(int count)
    {
        for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void Check(bool condition, string label)
    {
        _checks++;
        if (!condition) _failures++;
        GD.Print($"{(condition ? "PASS" : "FAIL")}: {label}");
    }
}

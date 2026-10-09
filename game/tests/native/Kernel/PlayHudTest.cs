using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using EnFractal.Native;
using EnFractal.Native.Kernel;
using EnFractal.Native.Look;

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
            await TestHandKeys();

            CommandHost.SaveRoot = CommandHost.DefaultSaveRoot;
            RemoveSaves();
            GD.Print($"NATIVE_KERNEL_PLAY_HUD: {_checks - _failures}/{_checks} checks passed; H folds the help, Q and E turn the isometric view, T and Shift+T step the time of day and the season back to the real clock, the companion's solid name tag stays small and hides near the camera, and F and V pick up, carry, set down on the box and push");
            GetTree().Quit(_failures == 0 ? 0 : 1);
        }
        catch (Exception error)
        {
            GD.PushError("Play HUD test exception: " + error);
            GetTree().Quit(1);
        }
    }

    private static InputEventKey Press(Key key, bool shift = false) => new() { PhysicalKeycode = key, Pressed = true, ShiftPressed = shift };

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
        Check(_hud.FindChild("WashAshoreVeil", true, false) is ColorRect { Color.A: 0, MouseFilter: Control.MouseFilterEnum.Ignore },
            "the wash-ashore veil is in place, clear and letting clicks through while nobody is washing ashore");
        Check(CompanionAvatar.SavedName("Wisp") == "the Gubble" && CompanionAvatar.SavedName("Pip") == "Pip",
            "a profile or save still naming the companion Wisp (the former default) restores it as the Gubble; a name the player chose stays");
        Check(await WalkTo(player, new Vector2(-0.3f, 0.62f)), "the player walks over to the doorstop on the rug");
        await Face(player, Vector3.Forward);
        _hud._UnhandledInput(Press(Key.F));
        await Frames(2);
        Check(host.HeldBy(CommandHost.PlayerAvatar) == "obj:doorstop" && notice.Text.StartsWith("Holding Doorstop", StringComparison.Ordinal) && state.Text.Contains("holding Doorstop (F)", StringComparison.Ordinal),
            $"F picks up the doorstop it faces; the HUD says so ({notice.Text} | {state.Text})");
        var revision = host.Revision;
        Check(await WalkTo(player, new Vector2(0.87f, 0.17f)), "the player carries it across the room to the big box");
        await Face(player, Vector3.Right);
        Check(Entity(host, "obj:doorstop")["held_by"]?.GetValue<string>() == CommandHost.PlayerAvatar, "still holding it on arrival");
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
        _hud._UnhandledInput(Press(Key.F));
        await Frames(2);
        Check(host.HeldBy(CommandHost.PlayerAvatar) == null && notice.Text.Contains("too heavy", StringComparison.Ordinal), "F on the big box says it is too heavy: " + notice.Text);
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

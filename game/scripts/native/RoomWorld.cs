using Godot;
using System;
using System.Linq;
using EnFractal.Native.Companion;
using EnFractal.Native.Look;
using EnFractal.Native.Look.Fauna;
using EnFractal.Native.Room;
using FileAccess = Godot.FileAccess;

namespace EnFractal.Native;

/// <summary>
/// Composition root for one room: load the room data and style preset, build the derived scene,
/// then place the player and companion at the manifest's spawns. Integrator-owned; lanes extend the
/// pieces it composes (Room/, Look/, avatars, HUD) rather than this file.
/// </summary>
public partial class RoomWorld : Node3D
{
    public const string DefaultRoom = "res://rooms/test_room";
    public const string DefaultStyleId = "storybook_painterly";
    public const int DefaultStyleVersion = 2;
    public static readonly string DefaultStyle = StylePreset.PathFor(DefaultStyleId, DefaultStyleVersion);
    /// <summary>The island's rules every room uses until a room names its own (res://rules/&lt;id&gt;/v&lt;N&gt;.json).</summary>
    public const string DefaultRulesId = "storybook_wild";
    public const int DefaultRulesVersion = 4;
    [Export] public string RoomDirectory { get; set; } = DefaultRoom;
    /// <summary>Overrides the room's pinned style when set (tests, previews).</summary>
    [Export] public string StylePresetPath { get; set; } = "";
    public RoomData Room { get; private set; } = null!;
    public LookDirector Look { get; private set; } = null!;
    public Node3D Built { get; private set; } = null!;
    public SmallPlayerController Player { get; private set; } = null!;
    public CompanionAvatar Companion { get; private set; } = null!;
    /// <summary>
    /// True once the room is built and the bodies placed. The room is read on a worker thread (RoomData.LoadAsync), so a caller
    /// waits for this by the clock, never a count of frames: under --fixed-fps frames pass faster than a room loads.
    /// </summary>
    public bool WorldReady { get; private set; }
    public string LoadError { get; private set; } = "";
    /// <summary>Set when the room's pinned style could not be used and the default look was applied instead.</summary>
    public string StyleNote { get; private set; } = "";

    /// <summary>A bare room id resolves to the player's captured rooms first, then to rooms shipped with the game.</summary>
    public static string ResolveRoom(string idOrPath)
    {
        if (idOrPath.Contains('/')) return idOrPath;
        var captured = $"user://rooms/{idOrPath}";
        return FileAccess.FileExists(captured + "/room.json") ? captured : $"res://rooms/{idOrPath}";
    }

    /// <summary>An explicit override, else the room's pinned style, else the default. A pin that does not verify falls back visibly, never silently.</summary>
    private StylePreset ChooseStyle()
    {
        if (StylePresetPath.Length > 0) return StylePreset.Load(StylePresetPath);
        if (Room.DefaultStyle is { } pin)
        {
            try { return StylePreset.Resolve(pin.PresetId, pin.PresetVersion, pin.PresetSha256); }
            catch (Exception error)
            {
                StyleNote = $"Room style {pin.PresetId} v{pin.PresetVersion} is unavailable ({error.Message}); showing the default look.";
                GD.Print("LOOK: " + StyleNote);
            }
        }
        return StylePreset.Resolve(DefaultStyleId, DefaultStyleVersion);
    }

    /// <summary>The room being read on its worker thread (see _ExitTree).</summary>
    private System.Threading.Tasks.Task<RoomData>? _loading;

    /// <summary>
    /// "-- --quit-after-ready=N": quit N frames after the world is ready (exit code 0, after printing ROOM_WORLD_QUIT), or as
    /// soon as the room fails to load (exit code 1). The boot check of the runners and of a smoke boot (tools/smoke-boot.ps1):
    /// Godot's own --quit-after counts frames from the start, and frames pass faster than a room loads on its worker thread
    /// (under --fixed-fps, a few milliseconds a frame). Godot runs the C# last built, never the source: a game built before
    /// this option ignores it and never quits, so boot checks build first and look for ROOM_WORLD_QUIT (13 October).
    /// </summary>
    private static int? QuitAfterReadyFrames() =>
        OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--quit-after-ready=", StringComparison.Ordinal)) is { } argument &&
        int.TryParse(argument["--quit-after-ready=".Length..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var frames)
            ? frames : null;

    public override async void _Ready()
    {
        try
        {
            // Off the main thread: a 10x island's tables are 350,000 rows to read, hash and check (S6).
            _loading = RoomData.LoadAsync(RoomDirectory);
            Room = await _loading;
            var preset = ChooseStyle();
            Look = new LookDirector { Name = "Look" };
            AddChild(Look);
            Look.Apply(preset, Room);
            Built = RoomBuilder.Build(Room);
            AddChild(Built);
            Look.Dress(Built);
            // Let PhysicsServer register every collider before placing actors on them.
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var lift = Vector3.Up * 0.008f;
            var playerSpawn = Room.SpawnFor("player");
            var companionSpawn = Room.SpawnFor("companion", playerSpawn);
            Player = new SmallPlayerController { Name = "Player", Position = playerSpawn.PositionM + lift, Rotation = new Vector3(0, Mathf.DegToRad(playerSpawn.YawDeg), 0) };
            AddChild(Player);
            Look.FocusTarget = Player;
            Player.SetSpawnPoint(Player.Position);
            Companion = new CompanionAvatar { Name = "Companion", Position = companionSpawn.PositionM + lift, Rotation = new Vector3(0, Mathf.DegToRad(companionSpawn.YawDeg), 0) };
            Companion.ConfigureIdentity("local_companion");
            AddChild(Companion);
            Companion.SetSpawnPoint(Companion.Position);
            Companion.BindPlayer(Player);
            Companion.Follow();
            // Living scenery (Run 2, Lane L): fish in the ponds and the veil under the water. Only the player frightens
            // fish; the Gubble is set apart from the world, a ghost only the player can see (the founder, 8 October).
            var pondLife = PondLife.Create(Room.RoomId, Built);
            AddChild(pondLife);
            pondLife.SetAvatars(Player);
            AddChild(new RoomHud
            {
                Player = Player, Companion = Companion, RoomTitle = Room.DisplayName.ToUpperInvariant(),
                // Never a silent fallback: a style pin that did not verify, or a renderer the look was not designed for.
                LookNotice = string.Join(" ", new[] { StyleNote, Look.PlayerNotice }.Where(note => note.Length > 0)),
                Look = Look,
            });
            var commandHost = Kernel.CommandHost.Attach(this);
            commandHost.LoadRules(DefaultRulesId, DefaultRulesVersion);
            commandHost.ConfigureNature();
            var natureLook = new NatureLook { Name = "NatureLook" };
            AddChild(natureLook);
            natureLook.Configure(commandHost, Built);
            // The player's AI: the companion link in front of the command host (A2).
            CompanionBridge.Attach(this);
            Navigation.RoomNavigation.Attach(this);
            SetMeta("room_id", Room.RoomId);
            SetMeta("room_manifest_sha256", Room.ManifestSha256);
            SetMeta("style", $"{preset.PresetId}@{preset.PresetVersion}");
            DisplayServer.WindowSetTitle($"EnFractal - {Room.DisplayName}");
            WorldReady = true;
            GD.Print($"ROOM_WORLD_READY room={Room.RoomId} shell={Room.Shell.Count} objects={Room.Objects.Count} lights={Look.RoomLightCount} style={preset.PresetId}@{preset.PresetVersion} spawn={Player.Position}");
            if (QuitAfterReadyFrames() is { } frames) QuitAfter(frames);
        }
        catch (Exception exception)
        {
            LoadError = exception.Message;
            GD.PushError("Room failed to load: " + exception.Message);
            if (QuitAfterReadyFrames() != null) GetTree().Quit(1);
        }
    }

    /// <summary>
    /// Counted on the scene tree's own frame signal, not on this node, so the quit comes even if the world is freed or replaced
    /// meanwhile. The wrappers left to the finalizer are let go first: pending at exit, they can abort Godot's shutdown (the
    /// test suites do the same before they quit).
    /// </summary>
    private void QuitAfter(int frames)
    {
        var tree = GetTree();
        var left = frames;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Action? tick = null;
        tick = () =>
        {
            if (--left > 0) return;
            tree.ProcessFrame -= tick;
            GD.Print(System.FormattableString.Invariant($"ROOM_WORLD_QUIT {frames} frames after ready ({clock.Elapsed.TotalSeconds:0.0} s)"));
            GC.Collect();
            GC.WaitForPendingFinalizers();
            tree.Quit(0);
        };
        tree.ProcessFrame += tick;
    }

    /// <summary>
    /// A room still being read when the world leaves the tree (the game quit during a load) is let finish first: its worker reads
    /// files through the engine, which crashed in FileAccess once the engine had shut down under it.
    /// </summary>
    public override void _ExitTree()
    {
        if (_loading is not { IsCompleted: false } loading) return;
        try { loading.Wait(TimeSpan.FromSeconds(60)); }
        catch (AggregateException) { /* the load's own failure is reported where it is awaited */ }
    }
}

using Godot;
using System;
using EnFractal.Native.Look;
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
    public const string DefaultStyle = "res://styles/storybook_painterly.json";
    [Export] public string RoomDirectory { get; set; } = DefaultRoom;
    [Export] public string StylePresetPath { get; set; } = DefaultStyle;
    public RoomData Room { get; private set; } = null!;
    public LookDirector Look { get; private set; } = null!;
    public Node3D Built { get; private set; } = null!;
    public SmallPlayerController Player { get; private set; } = null!;
    public CompanionAvatar Companion { get; private set; } = null!;
    public bool WorldReady { get; private set; }
    public string LoadError { get; private set; } = "";

    /// <summary>A bare room id resolves to the player's captured rooms first, then to rooms shipped with the game.</summary>
    public static string ResolveRoom(string idOrPath)
    {
        if (idOrPath.Contains('/')) return idOrPath;
        var captured = $"user://rooms/{idOrPath}";
        return FileAccess.FileExists(captured + "/room.json") ? captured : $"res://rooms/{idOrPath}";
    }

    public override async void _Ready()
    {
        try
        {
            Room = RoomData.Load(RoomDirectory);
            var preset = StylePreset.Load(StylePresetPath);
            Look = new LookDirector { Name = "Look" };
            AddChild(Look);
            Look.Apply(preset, Room);
            Built = RoomBuilder.Build(Room);
            AddChild(Built);
            // Let PhysicsServer register every collider before placing actors on them.
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var lift = Vector3.Up * 0.008f;
            var playerSpawn = Room.SpawnFor("player");
            var companionSpawn = Room.SpawnFor("companion", playerSpawn);
            Player = new SmallPlayerController { Name = "Player", Position = playerSpawn.PositionM + lift, Rotation = new Vector3(0, Mathf.DegToRad(playerSpawn.YawDeg), 0) };
            AddChild(Player);
            Player.SetSpawnPoint(Player.Position);
            Companion = new CompanionAvatar { Name = "Companion", Position = companionSpawn.PositionM + lift, Rotation = new Vector3(0, Mathf.DegToRad(companionSpawn.YawDeg), 0) };
            Companion.ConfigureIdentity("local_companion");
            AddChild(Companion);
            Companion.SetSpawnPoint(Companion.Position);
            Companion.BindPlayer(Player);
            Companion.Follow();
            AddChild(new RoomHud { Player = Player, Companion = Companion, RoomTitle = Room.DisplayName.ToUpperInvariant() });
            SetMeta("room_id", Room.RoomId);
            SetMeta("room_manifest_sha256", Room.ManifestSha256);
            SetMeta("style", $"{preset.PresetId}@{preset.PresetVersion}");
            DisplayServer.WindowSetTitle($"EnFractal - {Room.DisplayName}");
            WorldReady = true;
            GD.Print($"ROOM_WORLD_READY room={Room.RoomId} shell={Room.Shell.Count} objects={Room.Objects.Count} lights={Look.RoomLightCount} style={preset.PresetId}@{preset.PresetVersion} spawn={Player.Position}");
        }
        catch (Exception exception)
        {
            LoadError = exception.Message;
            GD.PushError("Room failed to load: " + exception.Message);
        }
    }
}

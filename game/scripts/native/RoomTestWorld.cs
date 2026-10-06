using Godot;

namespace EnFractal.Native;

/// <summary>
/// Hand-built placeholder room so the project boots and the avatars have a floor.
/// Nothing here is captured or styled; the capture pipeline replaces it with real rooms.
/// Dimensions are metres. The player profile is still the 0.30 m prototype until R0 retunes it.
/// </summary>
public partial class RoomTestWorld : Node3D
{
    public const float WidthM = 4.0f;   // along X
    public const float DepthM = 3.0f;   // along Z
    public const float HeightM = 2.4f;  // along Y
    public SmallPlayerController Player { get; private set; } = null!;
    public CompanionAvatar Companion { get; private set; } = null!;
    public bool WorldReady { get; private set; }
    public Vector3 Spawn { get; private set; } = new(0, 0.008f, 0.6f);
    public int PropCount { get; private set; }

    public override async void _Ready()
    {
        BuildLight();
        BuildShell();
        BuildProps();
        // Let PhysicsServer register the colliders before placing actors on them.
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        Player = new SmallPlayerController { Name = "Player", Position = Spawn };
        AddChild(Player);
        Player.SetSpawnPoint(Spawn);
        Companion = new CompanionAvatar { Name = "Companion", Position = Spawn + Vector3.Right * 0.45f };
        Companion.ConfigureIdentity("local_companion");
        AddChild(Companion);
        Companion.SetSpawnPoint(Companion.Position);
        Companion.BindPlayer(Player);
        Companion.Follow();
        AddChild(new RoomHud { Player = Player, Companion = Companion });
        SetMeta("room_source", "hand-built placeholder; not a captured space");
        WorldReady = true;
        GD.Print($"ROOM_WORLD_READY props={PropCount} spawn={Spawn}");
    }

    private void BuildLight()
    {
        AddChild(new WorldEnvironment { Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color("2a2f33"),
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color("c9c2b3"),
            AmbientLightEnergy = 0.35f, TonemapMode = Godot.Environment.ToneMapper.Filmic, TonemapExposure = 0.9f
        }});
        AddChild(new DirectionalLight3D { Name = "WindowSun", RotationDegrees = new Vector3(-42, 35, 0),
            LightColor = new Color("fff1d2"), LightEnergy = 0.9f, ShadowEnabled = true, DirectionalShadowMaxDistance = 12, ShadowBias = 0.02f });
        AddChild(new OmniLight3D { Name = "CeilingLamp", Position = new Vector3(0, HeightM - 0.15f, 0),
            LightColor = new Color("ffe7bf"), LightEnergy = 1.6f, OmniRange = 5.0f, ShadowEnabled = true });
    }

    private void BuildShell()
    {
        const float t = 0.10f;
        Slab("Floor", new Vector3(WidthM + 2 * t, t, DepthM + 2 * t), new Vector3(0, -t * 0.5f, 0), Material("8a7a66"), "floor");
        Slab("Ceiling", new Vector3(WidthM + 2 * t, t, DepthM + 2 * t), new Vector3(0, HeightM + t * 0.5f, 0), Material("e9e4da"), "ceiling");
        Slab("WallNorth", new Vector3(WidthM + 2 * t, HeightM, t), new Vector3(0, HeightM * 0.5f, -(DepthM + t) * 0.5f), Material("b9b4ab"), "wall");
        Slab("WallSouth", new Vector3(WidthM + 2 * t, HeightM, t), new Vector3(0, HeightM * 0.5f, (DepthM + t) * 0.5f), Material("b9b4ab"), "wall");
        Slab("WallEast", new Vector3(t, HeightM, DepthM), new Vector3((WidthM + t) * 0.5f, HeightM * 0.5f, 0), Material("b3aea4"), "wall");
        Slab("WallWest", new Vector3(t, HeightM, DepthM), new Vector3(-(WidthM + t) * 0.5f, HeightM * 0.5f, 0), Material("b3aea4"), "wall");
    }

    private void BuildProps()
    {
        // Proxies at real furniture sizes so scale reads from the small avatar.
        Prop("TableProxy", new Vector3(1.2f, 0.75f, 0.6f), new Vector3(-0.9f, 0, -0.9f), Material("9a6f45"));
        Prop("BoxProxy", new Vector3(0.35f, 0.30f, 0.35f), new Vector3(1.1f, 0, 0.2f), Material("b08a5a"));
        Prop("BookProxy", new Vector3(0.22f, 0.04f, 0.15f), new Vector3(0.45f, 0, 0.1f), Material("5b6f8a"));
        Prop("RugProxy", new Vector3(1.6f, 0.006f, 1.1f), new Vector3(0.2f, 0, 0.7f), Material("7a5b63"));
        Prop("DoorstopProxy", new Vector3(0.12f, 0.04f, 0.08f), new Vector3(-0.3f, 0, 0.5f), Material("6d6d6d"));
    }

    private void Prop(string name, Vector3 size, Vector3 footPosition, Material material)
    {
        Slab(name, size, footPosition + Vector3.Up * size.Y * 0.5f, material, "prop");
        PropCount++;
    }

    private void Slab(string name, Vector3 size, Vector3 center, Material material, string role)
    {
        var body = new StaticBody3D { Name = name, Position = center, CollisionLayer = 1, CollisionMask = 0 };
        body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material });
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        body.SetMeta("surface_role", role);
        AddChild(body);
    }

    private static StandardMaterial3D Material(string color) => new() { AlbedoColor = new Color(color), Roughness = 0.92f };
}

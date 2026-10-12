using Godot;

namespace EnFractal.Native.Look;

/// <summary>Presentation only: bounded GPU plumes, smoke and embers. The command host owns effect lifetime and motion.</summary>
public partial class FireVisual : Node3D
{
    public const int FlameParticles = 48;
    public const int SmokeParticles = 12;
    public const int EmberParticles = 16;
    private Node3D _plume = null!;
    private OmniLight3D _light = null!;
    private static Shader? _fireShader, _smokeShader;

    public override void _Ready()
    {
        _fireShader ??= GD.Load<Shader>("res://scripts/native/Look/fire_particle.gdshader");
        // Smoke absorbs rather than adds light. Share the shaping code, change only compositing.
        _smokeShader ??= new Shader { Code = _fireShader.Code.Replace("blend_add", "blend_mix") };
        _plume = new Node3D { Name = "Plume" }; AddChild(_plume);
        Particles("Flame", FlameParticles, 0.7, new Vector2(0.037f, 0.065f), 0.007f, 0.075f, 0.12f, 0);
        Particles("Smoke", SmokeParticles, 1.25, new Vector2(0.047f, 0.06f), 0.008f, 0.065f, 0.09f, 1);
        Particles("Embers", EmberParticles, 0.95, new Vector2(0.0012f, 0.003f), 0.014f, 0.13f, 0.20f, 2);
        _light = new OmniLight3D { LightColor = new Color("ff9745"), LightEnergy = 0.035f,
            OmniRange = 0.32f, Position = Vector3.Up * 0.035f, ShadowEnabled = false };
        AddChild(_light);
    }

    private void Particles(string name, int count, double lifetime, Vector2 size, float radius, float speedMin, float speedMax, int kind)
    {
        var material = new ShaderMaterial { Shader = kind == 1 ? _smokeShader : _fireShader };
        material.SetShaderParameter("kind", kind);
        var movement = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = radius, Direction = Vector3.Up, Spread = kind == 2 ? 24 : 9,
            InitialVelocityMin = speedMin, InitialVelocityMax = speedMax,
            Gravity = new Vector3(0, kind == 2 ? -0.05f : 0.018f, 0),
            ScaleMin = 0.65f, ScaleMax = 1.15f,
            // Detail comes from cheap advected sprite noise, not a room-wide fluid/noise simulation.
            TurbulenceEnabled = false
        };
        var particles = new GpuParticles3D
        {
            Name = name, Amount = count, Lifetime = lifetime, Preprocess = lifetime,
            LocalCoords = true, FixedFps = 60, Interpolate = true,
            ProcessMaterial = movement, DrawPass1 = new QuadMesh { Size = size, Material = material },
            VisibilityAabb = new Aabb(new Vector3(-0.12f, -0.06f, -0.12f), new Vector3(0.24f, 0.42f, 0.24f)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = Vector3.Up * (kind == 1 ? 0.055f : 0.019f)
        };
        _plume.AddChild(particles);
    }

    public void ShowAge(float age, bool bolt)
    {
        if (_plume == null) return;
        _plume.Scale = Vector3.One * (bolt ? 0.7f : 1);
        var flicker = Mathf.Sin(age * 17) * 0.004f + Mathf.Sin(age * 29 + 1.3f) * 0.002f;
        _light.LightEnergy = (bolt ? 0.025f : 0.035f) + flicker;
    }
}

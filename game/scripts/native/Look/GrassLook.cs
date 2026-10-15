using Godot;
using System;
using System.Collections.Generic;
using EnFractal.Native.Room;

namespace EnFractal.Native.Look;

/// <summary>
/// Procedural grass (a prototype for the founder, 15 October; branch run2/look-grass-proto). The island's grass copies (the plant set
/// whose prototype is grass: about 234,000 tufts on the 10x garage, 3 cm tall and 14 cm across) were the kit's tuft: eight solid
/// spikes, lit face by face, so the meadow at the 10 cm eye read as dark triangles on a painted floor. Here each tuft is built in
/// code: thin tapered blades that lean out and curve, each with its own height, width and lean, the same tuft for every copy (each
/// copy turns, scales and tints it, and the shader varies it by where it stands). grass_blades.gdshader lights a blade as the ground
/// it grows from, colours it from a dark cool root to a light warm tip, bends it in the wind and takes the land's season (straw,
/// frost, spring freshness) from LandSeason's globals.
///
/// The copies view keeps the set's distance bands; each band's mesh is built here with fewer blades the farther it is drawn. The
/// kit's tuft comes back with --look-grass=kit after "--" (or ENFRACTAL_LOOK_GRASS=kit), for a before and after in one build.
/// Every number is in Tuning.
/// </summary>
public static class GrassLook
{
    public static class Tuning
    {
        /// <summary>Blades in a tuft, by the band's mesh: the finest first (the copies' near band, to 8 m), the lightest last (to 40 m).</summary>
        public static readonly int[] Blades = { 128, 48, 20, 10 };
        /// <summary>Rows along a blade, by the band's mesh: 2 rows and a tip make 3 triangles; 1 row and a tip, one.</summary>
        public static readonly int[] Rows = { 2, 2, 1, 1 };
        /// <summary>How far from the tuft's centre a blade grows (m): wider than the kit's 14 cm tuft, so neighbouring tufts overlap into a carpet.</summary>
        public const float RootRadiusM = 0.1f;
        /// <summary>
        /// A blade's height (m), from the shortest to the tallest: knee-high on the 10 cm body, a meadow like Breath of the Wild's (the
        /// founder, 15 October). The copies scale it by about 1.1 (from 0.85 to 1.5), so most blades stand 1 to 2 cm.
        /// </summary>
        public const float MinHeightM = 0.009f, MaxHeightM = 0.016f;
        /// <summary>A blade's width at its root (m).</summary>
        public const float MinWidthM = 0.0024f, MaxWidthM = 0.0042f;
        /// <summary>How far a blade's tip leans, as a share of its height; and how much the lean points away from the tuft's centre.</summary>
        public const float MinLean = 0.2f, MaxLean = 0.7f, Outward = 0.6f;

        /// <summary>The blade's colour from its root (in the tuft's shade) to its tip in the light (linear albedo).</summary>
        public static readonly Color Root = new(0.2f, 0.38f, 0.1f);
        public static readonly Color Middle = new(0.31f, 0.54f, 0.14f);
        public static readonly Color Tip = new(0.62f, 0.76f, 0.32f);
        /// <summary>How dark a blade's root stays in the tuft's own shade (ambient occlusion: 1 none).</summary>
        public const float RootShade = 0.9f;
        /// <summary>The meadow floor's colour as it reads under the light (linear): far blades fade toward it, from FadeStartM to FadeEndM.</summary>
        public static readonly Color Ground = new(0.28f, 0.5f, 0.13f);
        public const float FadeStartM = 2f, FadeEndM = 12f, FadeAmount = 0.7f;
        /// <summary>Where along the blade the tip's colour starts.</summary>
        public const float TipStart = 0.5f;
        /// <summary>Broad patches across the meadow (m across), and how far they push the colour toward yellow-green or blue-green.</summary>
        public const float PatchM = 2.2f, PatchAmount = 0.18f;
        /// <summary>How much a blade is lit as the ground it grows from (1) rather than as its own flat face (0).</summary>
        public const float GroundNormal = 0.95f;
        /// <summary>Light through the blades from behind, as a share of their colour.</summary>
        public const float Backlight = 0.5f;
        /// <summary>The wind: how far a blade's tip sways, as a share of its height; how fast; over how long a gust (m).</summary>
        public const float WindShare = 0.22f, WindSpeed = 1.3f, GustM = 1.6f;
    }

    public const string ShaderPath = "res://shaders/grass_blades.gdshader";
    private static ShaderMaterial? _material;
    private static readonly Dictionary<int, Mesh> Meshes = new();

    /// <summary>Whether the copies view should draw this set with procedural blades: the grass, unless pinned back to the kit's tuft.</summary>
    public static bool Draws(PlantSet set) => set.Kind == PlantKind.Plant && set.Prototype == "grass" && Mode() != "kit";

    /// <summary>The pin: --look-grass=kit|blades after "--", or ENFRACTAL_LOOK_GRASS.</summary>
    public static string Mode()
    {
        foreach (var argument in OS.GetCmdlineUserArgs())
            if (argument.StartsWith("--look-grass=", StringComparison.Ordinal)) return argument["--look-grass=".Length..];
        var value = OS.GetEnvironment("ENFRACTAL_LOOK_GRASS");
        return string.IsNullOrWhiteSpace(value) ? "blades" : value.Trim();
    }

    /// <summary>The tuft for each of a set's meshes (finest first), as many as the set has, so its bands stay as they were.</summary>
    public static Mesh[] TuftMeshes(int count)
    {
        var meshes = new Mesh[count];
        for (var i = 0; i < count; i++)
        {
            var level = Math.Min(i == count - 1 ? Tuning.Blades.Length - 1 : i, Tuning.Blades.Length - 1);
            if (!Meshes.TryGetValue(level, out var mesh)) Meshes[level] = mesh = Tuft(Tuning.Blades[level], Tuning.Rows[level]);
            meshes[i] = mesh;
        }
        return meshes;
    }

    /// <summary>
    /// One tuft of blades: each a tapered strip from its root to a single tip vertex, curving as it leans (its lean grows with the
    /// square of the height). UV.y is how far up the blade (0 root, 1 tip), UV.x across it; UV2.x is the blade's own number. The
    /// blades are drawn from a fixed seed, so every tuft is the same and every run too.
    /// </summary>
    public static Mesh Tuft(int blades, int rows)
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        var random = new Random(1501);
        float Next(float low, float high) => low + (float)random.NextDouble() * (high - low);
        var index = 0;
        for (var b = 0; b < blades; b++)
        {
            // Spread the roots evenly over the tuft (a sunflower spiral), each nudged a little.
            var r = Tuning.RootRadiusM * Mathf.Sqrt((b + 0.5f) / blades) * Next(0.8f, 1.05f);
            var a = b * 2.39996f + Next(-0.3f, 0.3f);
            var root = new Vector3(r * Mathf.Cos(a), 0f, r * Mathf.Sin(a));
            var outward = r > 1e-4f ? new Vector3(root.X, 0f, root.Z).Normalized() : Vector3.Right;
            var free = Vector3.Right.Rotated(Vector3.Up, Next(0f, Mathf.Tau));
            var lean = (outward * Tuning.Outward + free * (1f - Tuning.Outward)).Normalized();
            var height = Next(Tuning.MinHeightM, Tuning.MaxHeightM);
            var width = Next(Tuning.MinWidthM, Tuning.MaxWidthM);
            var reach = height * Next(Tuning.MinLean, Tuning.MaxLean);
            var side = lean.Cross(Vector3.Up).Normalized().Rotated(Vector3.Up, Next(-0.5f, 0.5f));
            var number = Next(0f, 1f);
            Vector3 At(float t) => root + Vector3.Up * height * t * (1f - 0.12f * t * t) + lean * reach * t * t;
            for (var row = 0; row < rows; row++)
            {
                var t = (float)row / rows;
                var centre = At(t);
                var half = 0.5f * width * Mathf.Pow(1f - t, 0.8f);
                var tangent = (At(t + 0.05f) - centre).Normalized();
                var normal = side.Cross(tangent).Normalized();
                foreach (var across in new[] { -1f, 1f })
                {
                    tool.SetUV(new Vector2(0.5f + 0.5f * across, t));
                    tool.SetUV2(new Vector2(number, height));
                    tool.SetNormal(normal);
                    tool.SetColor(Colors.White);
                    tool.AddVertex(centre + side * half * across);
                }
            }
            var tipNormal = side.Cross((At(1f) - At(0.95f)).Normalized()).Normalized();
            tool.SetUV(new Vector2(0.5f, 1f));
            tool.SetUV2(new Vector2(number, height));
            tool.SetNormal(tipNormal);
            tool.SetColor(Colors.White);
            tool.AddVertex(At(1f));
            for (var row = 0; row < rows - 1; row++)
            {
                var a0 = index + row * 2;
                tool.AddIndex(a0); tool.AddIndex(a0 + 2); tool.AddIndex(a0 + 1);
                tool.AddIndex(a0 + 1); tool.AddIndex(a0 + 2); tool.AddIndex(a0 + 3);
            }
            var last = index + (rows - 1) * 2;
            tool.AddIndex(last); tool.AddIndex(index + rows * 2); tool.AddIndex(last + 1);
            index += rows * 2 + 1;
        }
        tool.SetMaterial(Material());
        return tool.Commit();
    }

    /// <summary>The blades' material, one for every tuft.</summary>
    public static ShaderMaterial Material()
    {
        if (_material != null) return _material;
        var material = new ShaderMaterial { Shader = GD.Load<Shader>(ShaderPath), ResourceName = "procedural grass" };
        material.SetShaderParameter("root_color", Tuning.Root);
        material.SetShaderParameter("middle_color", Tuning.Middle);
        material.SetShaderParameter("tip_color", Tuning.Tip);
        material.SetShaderParameter("tip_start", Tuning.TipStart);
        material.SetShaderParameter("root_shade", Tuning.RootShade);
        material.SetShaderParameter("ground_color", Tuning.Ground);
        material.SetShaderParameter("fade_start_m", Tuning.FadeStartM);
        material.SetShaderParameter("fade_end_m", Tuning.FadeEndM);
        material.SetShaderParameter("fade_amount", Tuning.FadeAmount);
        material.SetShaderParameter("patch_m", Tuning.PatchM);
        material.SetShaderParameter("patch_amount", Tuning.PatchAmount);
        material.SetShaderParameter("ground_normal", Tuning.GroundNormal);
        material.SetShaderParameter("backlight", Tuning.Backlight);
        material.SetShaderParameter("wind_axis", new Vector3(0.7071f, 0f, 0.7071f));
        material.SetShaderParameter("wind_share", Tuning.WindShare);
        material.SetShaderParameter("wind_speed", Tuning.WindSpeed);
        material.SetShaderParameter("gust_m", Tuning.GustM);
        return _material = material;
    }
}

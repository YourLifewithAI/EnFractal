using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using EnFractal.Native.Room;

namespace EnFractal.Native.Look;

/// <summary>
/// Procedural grass (a prototype for the founder, 15 October; branch run2/look-grass-proto). The island's grass copies (the plant set
/// whose prototype is grass: about 234,000 tufts on the 10x garage) were the kit's tuft: eight solid spikes, lit face by face. Here
/// each tuft is built in code, and the meadow grows from a canopy, after Outerra's procedural grass (outerra.blogspot.com, May 2012):
/// <list type="bullet">
/// <item>The canopy: three broad fractal fields over the land say how tall the grass stands, where it thins to bare earth and where it
/// has gone dry. They are baked once per island into a small texture (a cell every 25 cm or so, as Outerra's canopy is), each field
/// turned into its rank so a share of the meadow is bare or dry exactly as Tuning says; a blade reads it at its own root, so the
/// grass's height changes smoothly across the meadow and thins blade by blade toward a bare patch's edge.</item>
/// <item>The meadow floor draws the same canopy (painterly_land.gdshader, the meadow): lighter where the grass stands, warmer where it
/// is dry, bare earth where it is gone. So the floor between the blades and the meadow far away, where no blade is drawn, read as the
/// grass's own top, and a bare patch is bare near and far.</item>
/// <item>Folded blades: each blade of a tuft is a V, two short blades from one root, which doubles how dense short grass looks.</item>
/// <item>The lighter bands keep fewer blades but wider ones, so the grass still covers the ground with distance.</item>
/// </list>
/// grass_blades.gdshader lights a blade as the ground it grows from, colours it from root to tip in the land's own greens, bends it in
/// the wind and takes the season (LandSeason's globals). --look-grass=kit after "--" (or ENFRACTAL_LOOK_GRASS=kit) brings back the kit's
/// tuft and leaves the meadow floor as it was, for a before and after in one build. Every number is in Tuning.
/// </summary>
public static class GrassLook
{
    public static class Tuning
    {
        /// <summary>Folded blades (a V each, two blades from one root) in a tuft, by the band's mesh: the finest first (the copies' near band, to 8 m), the lightest last (to 40 m).</summary>
        public static readonly int[] Pairs = { 96, 40, 16, 7 };
        /// <summary>Rows along each half of a V, by the band's mesh: 2 rows and a tip make 3 triangles (a curved blade); 1 row and a tip, one.</summary>
        public static readonly int[] Rows = { 2, 1, 1, 1 };
        /// <summary>How much wider the blades of each band are than the near band's: fewer blades, each wider, so the grass still covers the ground.</summary>
        public static readonly float[] WidthScale = { 1f, 1.5f, 2.1f, 2.6f };
        /// <summary>How far from the tuft's centre a blade grows (m): neighbouring tufts overlap into a carpet.</summary>
        public const float RootRadiusM = 0.1f;
        /// <summary>
        /// A blade's height (m), before the canopy: knee-high on the 10 cm body, a meadow like Breath of the Wild's (the founder,
        /// 15 October). The copies scale it by about 1.1 and the canopy by ShortestShare to TallestShare.
        /// </summary>
        public const float MinHeightM = 0.009f, MaxHeightM = 0.016f;
        /// <summary>A blade's width at its root (m), in the near band.</summary>
        public const float MinWidthM = 0.0022f, MaxWidthM = 0.0038f;
        /// <summary>How far a blade's tip leans, as a share of its height; and how much the lean points away from the tuft's centre.</summary>
        public const float MinLean = 0.2f, MaxLean = 0.7f, Outward = 0.6f;
        /// <summary>How far the two halves of a folded blade turn apart from its lean (radians).</summary>
        public const float FoldAngle = 0.5f;

        /// <summary>The canopy's height field (m across a swell), and the grass's height from its shortest to its tallest, as a share of the blade's own.</summary>
        public const float CanopyM = 3.2f, ShortestShare = 0.65f, TallestShare = 1.3f;
        /// <summary>The bare-earth field (m across a patch), and the share of the meadow that is bare.</summary>
        public const float BareM = 2.6f, BareShare = 0.07f;
        /// <summary>The dry field (m across a patch), the share of the meadow that is dry, and how far the driest patches go toward the dry colour.</summary>
        public const float DryM = 4.5f, DryShare = 0.3f, Driest = 0.55f;
        /// <summary>How soft a bare or dry patch's edge is, in the field's rank (0 to 1).</summary>
        public const float EdgeSoftness = 0.05f;
        /// <summary>The canopy map's cell (m): about Outerra's 30 cm on the 10x island; at most CanopyCells along a side.</summary>
        public const float CanopyCellM = 0.25f;
        public const int CanopyCells = 1024;
        /// <summary>
        /// How far the meadow floor goes toward the grass's own layers where the grass stands: near, the thatch under the blades (a
        /// deep green in the hollows, lighter strokes of fallen blades, straw), so the gaps between blades read as deeper grass rather
        /// than a flat green; far, past the grass's distance, the canopy's top, as the grass is seen from above.
        /// </summary>
        public const float FloorCanopy = 0.85f;
        /// <summary>The thatch under the blades: its deep green (in the hollows) and its straw (dead grass), linear albedo.</summary>
        public static readonly Color Under = new(0.07f, 0.145f, 0.04f);
        public static readonly Color Thatch = new(0.34f, 0.29f, 0.13f);
        /// <summary>How far the low herbs' leaves (daisies, buttercups, ferns: the land shader's grass habit) blend into the canopy's top, so they sit in the meadow rather than dot it.</summary>
        public const float HerbsToCanopy = 0.5f;

        /// <summary>The blade's colour from its root (in the tuft's shade) to its tip in the light (linear albedo): the land's own greens, light and soft.</summary>
        public static readonly Color Root = new(0.2f, 0.38f, 0.1f);
        public static readonly Color Middle = new(0.31f, 0.54f, 0.14f);
        public static readonly Color Tip = new(0.62f, 0.76f, 0.32f);
        /// <summary>The canopy's top seen from above (the floor where grass stands), dry grass (a warm pale yellow-green, never brown) and bare earth (the worn path's warm light earth).</summary>
        public static readonly Color Top = new(0.3f, 0.52f, 0.13f);
        public static readonly Color Dry = new(0.56f, 0.58f, 0.22f);
        public static readonly Color Bare = new(0.42f, 0.33f, 0.21f);
        /// <summary>How dark a blade's root stays in the tuft's own shade (ambient occlusion: 1 none).</summary>
        public const float RootShade = 0.72f;
        /// <summary>
        /// Light and colour that vary clump by clump (after Malmquist's grass): each tuft a little lighter or darker (by up to this
        /// share), short grass warmer and sunnier, tall grass lusher and bluer (multipliers by the canopy's height).
        /// </summary>
        public const float TuftShade = 0.26f;
        public static readonly Vector3 SunTint = new(1.07f, 1.02f, 0.86f), LushTint = new(0.9f, 0.97f, 1.1f);
        /// <summary>Far blades fade toward the canopy's top, from FadeStartM to FadeEndM.</summary>
        public const float FadeStartM = 1.5f, FadeEndM = 8f, FadeAmount = 0.75f;
        /// <summary>The grass's distance, as Outerra's: blades shrink away from ShrinkStartM to ShrinkEndM, and beyond it the meadow floor alone carries the canopy.</summary>
        public const float ShrinkStartM = 4f, ShrinkEndM = 12f;
        /// <summary>
        /// The land's pastel softening (the preset's paint block gives the meadow floor the same: 0.82 of its chroma, 0.94 of its value
        /// and a 0.03 lift, a quarter of the way), so the blades read as the same paint as the floor they stand on.
        /// </summary>
        public const float PastelChroma = 0.82f, PastelScale = 0.94f, PastelLift = 0.03f, PastelAmount = 0.3f;
        /// <summary>Where along the blade the tip's colour starts.</summary>
        public const float TipStart = 0.5f;
        /// <summary>Small patches of yellow-green and blue-green within the canopy (m across), and how far they push the colour.</summary>
        public const float PatchM = 1.4f, PatchAmount = 0.1f;
        /// <summary>How much a blade is lit as the ground it grows from (1) rather than as its own flat face (0).</summary>
        public const float GroundNormal = 0.65f;
        /// <summary>Light through the blades from behind, as a share of their colour.</summary>
        public const float Backlight = 0.5f;
        /// <summary>The wind: how far a blade's tip sways, as a share of its height; how fast; over how long a gust (m).</summary>
        public const float WindShare = 0.22f, WindSpeed = 1.3f, GustM = 1.6f;
    }

    public const string ShaderPath = "res://shaders/grass_blades.gdshader";

    /// <summary>The canopy's global shader parameters, read by the blades and by the meadow floor (painterly_land.gdshader), in declaration order.</summary>
    public static readonly IReadOnlyList<(string Name, RenderingServer.GlobalShaderParameterType Type)> Globals = new[]
    {
        ("grass_canopy", RenderingServer.GlobalShaderParameterType.Vec4),
        ("grass_shape", RenderingServer.GlobalShaderParameterType.Vec4),
        ("grass_floor", RenderingServer.GlobalShaderParameterType.Vec4),
        ("grass_top", RenderingServer.GlobalShaderParameterType.Vec3),
        ("grass_dry", RenderingServer.GlobalShaderParameterType.Vec3),
        ("grass_bare", RenderingServer.GlobalShaderParameterType.Vec3),
        ("grass_canopy_area", RenderingServer.GlobalShaderParameterType.Vec4),
        ("grass_canopy_map", RenderingServer.GlobalShaderParameterType.Sampler2D),
        ("grass_under", RenderingServer.GlobalShaderParameterType.Vec3),
        ("grass_thatch", RenderingServer.GlobalShaderParameterType.Vec3),
    };

    private static ShaderMaterial? _material;
    private static readonly Dictionary<int, Mesh> Meshes = new();
    private static readonly StringName[] Names = Globals.Select(g => new StringName(g.Name)).ToArray();
    private static bool _registered;
    private static ImageTexture? _canopyMap, _emptyMap;
    private static Vector4 _canopyArea;

    /// <summary>Whether the copies view should draw this set with procedural blades: the grass, unless pinned back to the kit's tuft.</summary>
    public static bool Draws(PlantSet set) => set.Kind == PlantKind.Plant && set.Prototype == "grass" && On;

    /// <summary>Whether the procedural grass (and the canopy on the meadow floor) is on: unless pinned to the kit's tuft.</summary>
    public static bool On => Mode() != "kit";

    /// <summary>The pin: --look-grass=kit|blades after "--", or ENFRACTAL_LOOK_GRASS.</summary>
    public static string Mode()
    {
        foreach (var argument in OS.GetCmdlineUserArgs())
            if (argument.StartsWith("--look-grass=", StringComparison.Ordinal)) return argument["--look-grass=".Length..];
        var value = OS.GetEnvironment("ENFRACTAL_LOOK_GRASS");
        return string.IsNullOrWhiteSpace(value) ? "blades" : value.Trim();
    }

    /// <summary>
    /// The canopy into the globals the blades and the meadow floor read (registered at run time on the first call, as LandSeason's are;
    /// all zero leaves the meadow floor as it was). LandSeason.Apply calls this once per moment.
    /// </summary>
    public static void ApplyGlobals()
    {
        Register();
        // On only once an island's canopy is baked (a room without grass, or the kit's tufts, leaves the meadow floor as it was).
        RenderingServer.GlobalShaderParameterSet(Names[0], new Vector4(Tuning.BareShare, Tuning.DryShare, Tuning.EdgeSoftness, On && _canopyMap != null ? 1f : 0f));
        RenderingServer.GlobalShaderParameterSet(Names[1], new Vector4(Tuning.ShortestShare, Tuning.TallestShare, Tuning.Driest, 0f));
        RenderingServer.GlobalShaderParameterSet(Names[2], new Vector4(Tuning.FloorCanopy, Tuning.HerbsToCanopy, Tuning.ShrinkStartM, Tuning.ShrinkEndM));
        RenderingServer.GlobalShaderParameterSet(Names[3], new Vector3(Tuning.Top.R, Tuning.Top.G, Tuning.Top.B));
        RenderingServer.GlobalShaderParameterSet(Names[4], new Vector3(Tuning.Dry.R, Tuning.Dry.G, Tuning.Dry.B));
        RenderingServer.GlobalShaderParameterSet(Names[5], new Vector3(Tuning.Bare.R, Tuning.Bare.G, Tuning.Bare.B));
        RenderingServer.GlobalShaderParameterSet(Names[6], _canopyArea);
        RenderingServer.GlobalShaderParameterSet(Names[7], (_canopyMap ?? EmptyMap()).GetRid());
        RenderingServer.GlobalShaderParameterSet(Names[8], new Vector3(Tuning.Under.R, Tuning.Under.G, Tuning.Under.B));
        RenderingServer.GlobalShaderParameterSet(Names[9], new Vector3(Tuning.Thatch.R, Tuning.Thatch.G, Tuning.Thatch.B));
    }

    private static void Register()
    {
        if (_registered) return;
        _registered = true;
        if (Globals.All(g => ProjectSettings.HasSetting("shader_globals/" + g.Name))) return;
        for (var i = 0; i < Globals.Count; i++)
        {
            Variant initial = Globals[i].Type switch
            {
                RenderingServer.GlobalShaderParameterType.Vec4 => Vector4.Zero,
                RenderingServer.GlobalShaderParameterType.Sampler2D => EmptyMap().GetRid(),
                _ => Vector3.Zero,
            };
            RenderingServer.GlobalShaderParameterAdd(Names[i], Globals[i].Type, initial);
        }
    }

    private static ImageTexture EmptyMap()
    {
        if (_emptyMap != null) return _emptyMap;
        var image = Image.CreateEmpty(1, 1, false, Image.Format.Rgb8);
        image.Fill(new Color(0.5f, 1f, 0f));
        return _emptyMap = ImageTexture.CreateFromImage(image);
    }

    /// <summary>
    /// Bakes an island's canopy (once, as its copies view is made): over the room's bounds, a cell every CanopyCellM (at most
    /// CanopyCells a side), three fractal fields (Godot's FastNoiseLite, fixed seeds, so the same island always grows the same meadow),
    /// each turned into its rank across the island so it is even from 0 to 1: red how tall the grass stands, green how surely it stands
    /// (bare below BareShare), blue how dry it is (dry above 1 - DryShare). The blades and the meadow floor read it at a point's x and z.
    /// </summary>
    public static void BuildCanopy(RoomData room)
    {
        if (!On) return;
        var bounds = room.Bounds;
        var cell = Math.Max(Tuning.CanopyCellM, Math.Max(bounds.Size.X, bounds.Size.Z) / Tuning.CanopyCells);
        var width = Math.Max(2, (int)Math.Ceiling(bounds.Size.X / cell) + 1);
        var depth = Math.Max(2, (int)Math.Ceiling(bounds.Size.Z / cell) + 1);
        FastNoiseLite Field(int seed, float scaleM) => new()
        {
            NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin, Seed = seed, Frequency = 1f / scaleM,
            FractalType = FastNoiseLite.FractalTypeEnum.Fbm, FractalOctaves = 3, FractalGain = 0.5f, FractalLacunarity = 2.1f,
        };
        var fields = new[] { Field(31, Tuning.CanopyM), Field(57, Tuning.BareM), Field(83, Tuning.DryM) };
        var values = new float[3][];
        for (var f = 0; f < 3; f++)
        {
            var raw = new float[width * depth];
            for (var z = 0; z < depth; z++)
                for (var x = 0; x < width; x++)
                    raw[z * width + x] = fields[f].GetNoise2D(bounds.Position.X + x * cell, bounds.Position.Z + z * cell);
            // Each field as its rank across the island, so its shares are exact.
            var order = Enumerable.Range(0, raw.Length).OrderBy(i => raw[i]).ToArray();
            var ranked = new float[order.Length];
            for (var r = 0; r < order.Length; r++) ranked[order[r]] = (r + 0.5f) / order.Length;
            values[f] = ranked;
        }
        var image = Image.CreateEmpty(width, depth, false, Image.Format.Rgb8);
        for (var z = 0; z < depth; z++)
            for (var x = 0; x < width; x++)
            {
                var i = z * width + x;
                image.SetPixel(x, z, new Color(values[0][i], values[1][i], values[2][i]));
            }
        _canopyMap = ImageTexture.CreateFromImage(image);
        // x0, z0 and one over the map's width and depth (m): a point's uv is (xz - (x0, z0)) times them.
        _canopyArea = new Vector4(bounds.Position.X - 0.5f * cell, bounds.Position.Z - 0.5f * cell, 1f / (width * cell), 1f / (depth * cell));
        ApplyGlobals();
    }

    /// <summary>The tuft for each of a set's meshes (finest first), as many as the set has, so its bands stay as they were.</summary>
    public static Mesh[] TuftMeshes(int count)
    {
        var meshes = new Mesh[count];
        for (var i = 0; i < count; i++)
        {
            var level = Math.Min(i == count - 1 ? Tuning.Pairs.Length - 1 : i, Tuning.Pairs.Length - 1);
            if (!Meshes.TryGetValue(level, out var mesh)) Meshes[level] = mesh = Tuft(Tuning.Pairs[level], Tuning.Rows[level], Tuning.WidthScale[level]);
            meshes[i] = mesh;
        }
        return meshes;
    }

    /// <summary>
    /// One tuft of folded blades: each a V, two tapered strips from one root that turn apart from its lean, each curving as it leans
    /// (its lean grows with the square of the height). UV.y is how far up a blade (0 root, 1 tip), UV.x across it; UV2 is (the blade's
    /// own number, its height); CUSTOM0 is (its root's x and z in the tuft, its height, its number), so the shader can read the canopy
    /// at the root and grow or thin the whole blade about it. A fixed seed: every tuft is the same and every run too.
    /// </summary>
    public static Mesh Tuft(int pairs, int rows, float widthScale)
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        tool.SetCustomFormat(0, SurfaceTool.CustomFormat.RgbaFloat);
        var random = new Random(1501);
        float Next(float low, float high) => low + (float)random.NextDouble() * (high - low);
        var index = 0;
        for (var b = 0; b < pairs; b++)
        {
            // Spread the roots evenly over the tuft (a sunflower spiral), each nudged a little.
            var r = Tuning.RootRadiusM * Mathf.Sqrt((b + 0.5f) / pairs) * Next(0.8f, 1.05f);
            var a = b * 2.39996f + Next(-0.3f, 0.3f);
            var root = new Vector3(r * Mathf.Cos(a), 0f, r * Mathf.Sin(a));
            var outward = r > 1e-4f ? new Vector3(root.X, 0f, root.Z).Normalized() : Vector3.Right;
            var free = Vector3.Right.Rotated(Vector3.Up, Next(0f, Mathf.Tau));
            var lean = (outward * Tuning.Outward + free * (1f - Tuning.Outward)).Normalized();
            var height = Next(Tuning.MinHeightM, Tuning.MaxHeightM);
            var width = Next(Tuning.MinWidthM, Tuning.MaxWidthM) * widthScale;
            var number = Next(0f, 1f);
            foreach (var turn in new[] { -Tuning.FoldAngle, Tuning.FoldAngle })
            {
                var halfLean = lean.Rotated(Vector3.Up, turn + Next(-0.2f, 0.2f)).Normalized();
                var halfHeight = height * Next(0.8f, 1f);
                var reach = halfHeight * Next(Tuning.MinLean, Tuning.MaxLean);
                var side = halfLean.Cross(Vector3.Up).Normalized().Rotated(Vector3.Up, Next(-0.4f, 0.4f));
                Vector3 At(float t) => root + Vector3.Up * halfHeight * t * (1f - 0.12f * t * t) + halfLean * reach * t * t;
                void Vertex(Vector3 at, Vector2 uv, Vector3 normal)
                {
                    tool.SetUV(uv);
                    tool.SetUV2(new Vector2(number, halfHeight));
                    tool.SetCustom(0, new Color(root.X, root.Z, halfHeight, number));
                    tool.SetNormal(normal);
                    tool.SetColor(Colors.White);
                    tool.AddVertex(at);
                }
                for (var row = 0; row < rows; row++)
                {
                    var t = (float)row / rows;
                    var centre = At(t);
                    var half = 0.5f * width * Mathf.Pow(1f - t, 0.8f);
                    var normal = side.Cross((At(t + 0.05f) - centre).Normalized()).Normalized();
                    Vertex(centre - side * half, new Vector2(0f, t), normal);
                    Vertex(centre + side * half, new Vector2(1f, t), normal);
                }
                Vertex(At(1f), new Vector2(0.5f, 1f), side.Cross((At(1f) - At(0.95f)).Normalized()).Normalized());
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
        material.SetShaderParameter("tuft_shade", Tuning.TuftShade);
        material.SetShaderParameter("sun_tint", Tuning.SunTint);
        material.SetShaderParameter("lush_tint", Tuning.LushTint);
        material.SetShaderParameter("fade_start_m", Tuning.FadeStartM);
        material.SetShaderParameter("fade_end_m", Tuning.FadeEndM);
        material.SetShaderParameter("fade_amount", Tuning.FadeAmount);
        material.SetShaderParameter("shrink_start_m", Tuning.ShrinkStartM);
        material.SetShaderParameter("shrink_end_m", Tuning.ShrinkEndM);
        material.SetShaderParameter("pastel", new Vector4(Tuning.PastelChroma, Tuning.PastelScale, Tuning.PastelLift, Tuning.PastelAmount));
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

using System;
using EnFractal.Native.Room;

namespace EnFractal.Native.Look;

/// <summary>One distance band of a plant set's copies: which of its meshes (0 full detail, then its lods) is drawn from BeginM to EndM (0: no end), and whether it casts shadows.</summary>
public readonly record struct CopyBand(int Mesh, float BeginM, float EndM, bool CastsShadow);

/// <summary>
/// How far the 10x world is drawn and in which detail (packet S6; the numbers are the look's to tune, S9): each plant set's copies
/// out to a range set by its kind and size, its lighter meshes further out, shadows only from near copies; the terrain one level
/// per 16 m tile by distance. Every switch is a visibility range measured from the camera to the middle of a tile's bounds, with
/// a hysteresis margin, so neighbouring bands swap at one distance whichever way the camera moves (never both, never neither).
/// The defaults follow the run's recommendation to the founder (docs/runs/RUN-2-TEN-TIMES.md): small plants to 40-60 m, trees
/// to about 150 m. The data names no ranges; these numbers are the game's.
/// </summary>
public static class DrawDistances
{
    /// <summary>Plants no taller than SmallPlantHeightM (a 3 cm tuft is under a pixel there) are drawn to SmallPlantM, taller ones to PlantM.</summary>
    public const float SmallPlantM = 40f, PlantM = 60f, SmallPlantHeightM = 0.08f;
    public const float StoneM = 80f;
    public const float TreeM = 150f;
    /// <summary>Copies cast shadows only nearer than this: past it a 10 cm plant's shadow is a few pixels, and 350,000 casters cost the shadow passes.</summary>
    public const float CopyShadowM = 20f;
    /// <summary>Hysteresis at every copy band's edge.</summary>
    public const float CopyMarginM = 1f;
    /// <summary>Where each lighter mesh takes over, as fractions of the set's range, for a set with 1 to 4 meshes.</summary>
    private static readonly float[][] DetailEnds =
    {
        new[] { 1f },
        new[] { 0.25f, 1f },
        new[] { 0.15f, 0.4f, 1f },
        new[] { 0.12f, 0.25f, 0.5f, 1f },
    };

    /// <summary>
    /// Where each terrain level ends (0: drawn to the camera's far plane), for 1 to 4 levels, measured to a 16 m tile's middle.
    /// Level 0 reaches past the tiles round the camera (a tile's middle is at most 11.3 m from a body standing on it, its
    /// neighbours' at most 34 m), so the ground under every nearby foot is drawn as it collides.
    /// </summary>
    private static readonly float[][] TerrainEnds =
    {
        new[] { 0f },
        new[] { 48f, 0f },
        new[] { 48f, 112f, 0f },
        new[] { 40f, 80f, 160f, 0f },
    };
    public const float TerrainMarginM = 4f;

    /// <summary>The main thread's time for building copies each frame (PlantCopiesView), so a 350,000-copy island arrives over a few seconds without a hitch.</summary>
    public const double BuildBudgetMs = 2.5;
    /// <summary>Copies' collision within this distance of a spawn is built before the bodies are placed; the rest follows nearest first.</summary>
    public const float NearCollisionM = 16f;

    /// <summary>How far a set's copies are drawn: by kind, and for plants by their typical height (the mesh's height times a typical y scale).</summary>
    public static float CopyRangeM(PlantKind kind, float typicalHeightM) => kind switch
    {
        PlantKind.Tree => TreeM,
        PlantKind.Stone => StoneM,
        _ => typicalHeightM <= SmallPlantHeightM ? SmallPlantM : PlantM,
    };

    /// <summary>A set's bands: each mesh from where the one before ends, the full-detail band split at CopyShadowM so only near copies cast shadows.</summary>
    public static CopyBand[] CopyBands(PlantKind kind, float typicalHeightM, int meshes)
    {
        var range = CopyRangeM(kind, typicalHeightM);
        var ends = DetailEnds[Math.Clamp(meshes, 1, DetailEnds.Length) - 1];
        var bands = new System.Collections.Generic.List<CopyBand>();
        var begin = 0f;
        for (var mesh = 0; mesh < ends.Length; mesh++)
        {
            var end = ends[mesh] * range;
            if (begin < CopyShadowM && end > CopyShadowM)
            {
                bands.Add(new CopyBand(mesh, begin, CopyShadowM, true));
                bands.Add(new CopyBand(mesh, CopyShadowM, end, false));
            }
            else bands.Add(new CopyBand(mesh, begin, end, end <= CopyShadowM));
            begin = end;
        }
        return bands.ToArray();
    }

    /// <summary>The distances a terrain level is drawn between (an end of 0: to the far plane).</summary>
    public static (float BeginM, float EndM) TerrainBand(int level, int levels)
    {
        var ends = TerrainEnds[Math.Clamp(levels, 1, TerrainEnds.Length) - 1];
        level = Math.Clamp(level, 0, ends.Length - 1);
        return (level == 0 ? 0f : ends[level - 1], ends[level]);
    }
}

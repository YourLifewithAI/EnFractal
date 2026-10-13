using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace EnFractal.Tests.Look;

/// <summary>Where a character sheet tile stands its character: the feet, the ground's normal, the water's surface and the wall it climbs, if any.</summary>
public sealed record CharacterSheetStage(Vector3 FeetM, Vector3 GroundNormal, float? WaterLevelM, Plane? Wall);

/// <summary>
/// What a pose sees: the tile it poses (CharacterSheetPoses.Tiles), the node standing in for the capsule (its origin is the feet,
/// facing -Z), the character's visual root under it, the character's folder and motion kind ("float", "hop", "waddle", "stride"),
/// the seconds since the tile began (a gait can run to its phase), and the stage.
/// </summary>
public sealed record CharacterSheetPoseContext(string TileId, Node3D Holder, Node3D Visual, string Folder, string MotionKind, double Seconds, CharacterSheetStage Stage);

/// <summary>
/// The character sheet's pose hook (tools/look/character_sheet.ps1). The poses come from later packets (gaits, contact, the
/// Gubble, the tumble); until one registers a pose for a tile, the tile shows the rest pose and says so in its label.
/// A packet registers without editing this file: any public static method named RegisterCharacterSheetPoses() taking nothing,
/// on any type in the game's assembly, is called once before the sheet renders, and calls Register (or sets BuildBody).
/// A pose is called every frame of its tile's warm-up and returns whether it posed the character.
/// </summary>
public static class CharacterSheetPoses
{
    public delegate bool Pose(CharacterSheetPoseContext context);

    /// <summary>The nine tiles: id and label. The last is "float_cast" for a floating character and "close" (the face) for the others.</summary>
    public static readonly IReadOnlyList<(string Id, string Label)> Tiles = new[]
    {
        ("idle", "idle"), ("walk", "walk"), ("run", "run"), ("hop_landing", "a hop landing"), ("slope_30", "a 30° slope"),
        ("wading", "wading"), ("climbing", "climbing"), ("tumble", "a tumble, mid-flop"), ("ninth", "floating and casting / a close look"),
    };

    private static readonly Dictionary<string, Pose> Registry = new(StringComparer.Ordinal);
    private static bool _discovered;

    /// <summary>
    /// Builds the character's body from its folder (the rig, once it exists), or null to load the bare GLB. CharacterLook is
    /// applied to whatever it returns.
    /// </summary>
    public static Func<string, Node3D?>? BuildBody { get; set; }

    public static void Register(string tileId, Pose pose) => Registry[tileId] = pose;

    public static bool Has(string tileId) => Registry.ContainsKey(tileId);

    /// <summary>Pose the character for its tile; false (the rest pose) when no pose is registered or the pose declined.</summary>
    public static bool TryPose(CharacterSheetPoseContext context) => Registry.TryGetValue(context.TileId, out var pose) && pose(context);

    /// <summary>Call every RegisterCharacterSheetPoses() in the assembly, once. Returns the types that registered.</summary>
    public static IReadOnlyList<string> Discover()
    {
        if (_discovered) return Array.Empty<string>();
        _discovered = true;
        var found = new List<string>();
        foreach (var type in typeof(CharacterSheetPoses).Assembly.GetTypes())
        {
            var method = type.GetMethod("RegisterCharacterSheetPoses", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes);
            if (method == null) continue;
            method.Invoke(null, null);
            found.Add(type.FullName ?? type.Name);
        }
        return found.OrderBy(n => n, StringComparer.Ordinal).ToList();
    }
}

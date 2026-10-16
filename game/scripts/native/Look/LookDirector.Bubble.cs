using Godot;
using System.Collections.Generic;

namespace EnFractal.Native.Look;

/// <summary>
/// The Bubble's look (Kernel/Bubble/CommandHostBubble.cs): a placeholder a look pass replaces. A carry or a lift is a translucent
/// sphere round the body; the boat a translucent dome over a disc at the water; a ghost preview a fainter sphere. The kernel says
/// where each is (ShowBubble, MoveBubble, HideBubble); the look owns nothing about it. A handful at most (BubbleLaws.MaxBubbles and
/// MaxGhosts), each one mesh, moved by setting its position: no allocation while a bubble drifts.
/// </summary>
public partial class LookDirector
{
    private readonly Dictionary<string, MeshInstance3D> _bubbleLooks = new();
    private StandardMaterial3D? _bubbleSkin;
    private StandardMaterial3D? _bubbleGhostSkin;

    /// <summary>Draw a bubble (or its ghost) at a place. An id already drawn is moved instead.</summary>
    public void ShowBubble(string id, Vector3 center, float radiusM, bool boat, bool ghost)
    {
        if (string.IsNullOrEmpty(id) || !center.IsFinite() || !float.IsFinite(radiusM) || radiusM <= 0) return;
        if (_bubbleLooks.TryGetValue(id, out var drawn))
        {
            drawn.Position = center;
            return;
        }
        _bubbleSkin ??= new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
            AlbedoColor = new Color(0.72f, 0.9f, 1f, 0.22f), Roughness = 0.05f, Metallic = 0.1f, RimEnabled = true, Rim = 0.8f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _bubbleGhostSkin ??= new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.85f, 0.95f, 1f, 0.1f), CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        Mesh mesh = boat
            ? new SphereMesh { Radius = radiusM, Height = radiusM, IsHemisphere = true, RadialSegments = 24, Rings = 8 }
            : new SphereMesh { Radius = radiusM, Height = radiusM * 2, RadialSegments = 24, Rings = 12 };
        var look = new MeshInstance3D
        {
            Name = "Bubble", Mesh = mesh, MaterialOverride = ghost ? _bubbleGhostSkin : _bubbleSkin, Position = center,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _bubbleLooks[id] = look;
        AddChild(look);
    }

    /// <summary>A drifting bubble's place, every tick it moves.</summary>
    public void MoveBubble(string id, Vector3 center)
    {
        if (id != null && center.IsFinite() && _bubbleLooks.TryGetValue(id, out var drawn)) drawn.Position = center;
    }

    /// <summary>A bubble popped or a ghost went: its drawing goes. An unknown id is a no-op.</summary>
    public void HideBubble(string id)
    {
        if (id == null || !_bubbleLooks.Remove(id, out var drawn)) return;
        if (IsInstanceValid(drawn)) drawn.QueueFree();
    }
}

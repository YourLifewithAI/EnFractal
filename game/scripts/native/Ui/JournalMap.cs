using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;
using EnFractal.Native.Kernel;
using EnFractal.Native.Look;
using EnFractal.Native.Room;

namespace EnFractal.Native.Ui;

/// <summary>
/// The journal's map: only the team's saved discoveries (the host's discovered block), never the room manifest's objects or the
/// camera. Discovered ground is inked, darker on the player's own level; discovered things are outlined where they were last
/// seen; the two avatars are marked where the host has them. The map lies as the room does from above, -Z at the top; its
/// compass points to the room's own north (the manifest's site.neg_z_bearing_deg), and a room that does not say which way it
/// faces gets no compass rather than a guess.
/// </summary>
public partial class JournalMap : Control
{
    /// <summary>
    /// Discovered ground this close to the player's height is inked darker as their own level. Body-sized (S8 of the 10x world kept
    /// it): a level is a surface the 10 cm body could stand on beside them (CompanionAvatar.LevelGapM), whatever the room's size.
    /// </summary>
    public const float OwnLevelM = CompanionAvatar.LevelGapM;
    private readonly List<(Rect2 Area, float Height)> _land = new();
    private readonly List<(Rect2 Area, string Name)> _objects = new();
    private Vector2 _player;
    private Vector2 _companion;
    private float _height;
    private RoomData? _siteOf;
    private RoomSite? _site;
    public int KnownObjectCount => _objects.Count;
    public int DiscoveredSpanCount => _land.Count;
    /// <summary>Whether the map shows a compass: only for a room whose manifest declares its site.</summary>
    public bool ShowsCompass => _site is { Declared: true };
    /// <summary>Where north lies on the page, as a unit vector (+Y down); zero without a compass.</summary>
    public Vector2 North => _site is { Declared: true } site ? NorthOnPage(site.NegZBearingDeg) : Vector2.Zero;

    /// <summary>
    /// North on a map drawn with the room's -Z at the top and +X to the right, for a room whose -Z axis faces the compass bearing
    /// negZBearingDeg (0: -Z is north). Bearings turn clockwise, so north is the top turned back (anticlockwise) by the bearing:
    /// -Z facing east (90) puts north on the left.
    /// </summary>
    public static Vector2 NorthOnPage(float negZBearingDeg)
    {
        var bearing = Mathf.DegToRad(negZBearingDeg);
        return new Vector2(-Mathf.Sin(bearing), -Mathf.Cos(bearing));
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Resized += QueueRedraw;
    }

    /// <summary>Read the team's discovered map and the avatars' places from the host.</summary>
    public void UpdateFrom(CommandHost host)
    {
        _land.Clear();
        _objects.Clear();
        var data = host.ExportDiscovered();
        var cell = (float)data["cell_m"]!.GetValue<double>();
        foreach (var level in data["levels"]!.AsArray().OfType<JsonObject>())
        {
            var x = (float)level["min_xz_m"]![0]!.GetValue<double>();
            var z = (float)level["min_xz_m"]![1]!.GetValue<double>();
            var columns = level["columns"]!.GetValue<int>();
            var rows = level["rows"]!.GetValue<int>();
            var height = (float)level["height_m"]!.GetValue<double>();
            var bits = Convert.FromBase64String(level["cells"]!.GetValue<string>());
            bool Seen(int c, int r)
            {
                var index = r * columns + c;
                return (bits[index >> 3] & (1 << (index & 7))) != 0;
            }
            // Each row's runs of discovered cells become one strip.
            for (var r = 0; r < rows; r++)
                for (var c = 0; c < columns; c++)
                {
                    if (!Seen(c, r)) continue;
                    var start = c;
                    while (c + 1 < columns && Seen(c + 1, r)) c++;
                    _land.Add((new Rect2(x + start * cell, z + r * cell, (c - start + 1) * cell, cell), height));
                }
        }
        foreach (var known in data["entities"]!.AsObject())
        {
            var entity = known.Value!["entity"]!;
            var low = Read(entity["bounds_m"]!["min_m"]!);
            var high = Read(entity["bounds_m"]!["max_m"]!);
            _objects.Add((new Rect2(low.X, low.Z, high.X - low.X, high.Z - low.Z), entity["display_name"]!.GetValue<string>()));
        }
        var avatars = host.Entities().Where(e => e["kind"]!.GetValue<string>() == "avatar").ToDictionary(e => e["id"]!.GetValue<string>(), e => Read(e["position_m"]!));
        var player = avatars.TryGetValue(CommandHost.PlayerAvatar, out var at) ? at : Vector3.Zero;
        var companion = avatars.TryGetValue(CommandHost.CompanionAvatarId, out var beside) ? beside : player;
        _player = new Vector2(player.X, player.Z);
        _companion = new Vector2(companion.X, companion.Z);
        _height = player.Y;
        // The site is read once per room: from its manifest, as the room loaded it.
        if (_siteOf != host.Room)
        {
            _siteOf = host.Room;
            _site = RoomSite.For(host.Room, out _);
        }
        QueueRedraw();
    }

    private static Vector3 Read(JsonNode node) => new((float)node[0]!.GetValue<double>(), (float)node[1]!.GetValue<double>(), (float)node[2]!.GetValue<double>());

    public override void _Draw()
    {
        var box = new Rect2(_player - Vector2.One * 0.2f, Vector2.One * 0.4f).Expand(_companion);
        foreach (var land in _land) box = box.Merge(land.Area);
        foreach (var thing in _objects) box = box.Merge(thing.Area);
        box = box.Grow(0.15f);
        var scale = MathF.Min((Size.X - 30) / box.Size.X, (Size.Y - 40) / box.Size.Y);
        var margin = (Size - box.Size * scale) * 0.5f;
        Vector2 At(Vector2 p) => margin + (p - box.Position) * scale;
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.55f, 0.40f, 0.18f, 0.10f), false, 1);
        foreach (var land in _land)
            DrawRect(new Rect2(At(land.Area.Position), land.Area.Size * scale), new Color(0.35f, 0.42f, 0.26f, MathF.Abs(land.Height - _height) < OwnLevelM ? 0.45f : 0.16f));
        foreach (var thing in _objects)
            DrawRect(new Rect2(At(thing.Area.Position), thing.Area.Size * scale), new Color(0.4f, 0.28f, 0.15f, 0.65f), false, 1.2f);
        var ink = new Color("694533");
        DrawCircle(At(_player), 5, ink);
        var g = At(_companion);
        DrawPolyline(new[] { g + Vector2.Up * 6, g + Vector2.Right * 6, g + Vector2.Down * 6, g + Vector2.Left * 6, g + Vector2.Up * 6 }, new Color("376e6b"), 2, true);
        if (!ShowsCompass) return;
        // The compass in the top-right corner: a needle pointing to the room's north, an N at its tip.
        var centre = new Vector2(Size.X - 36, 38);
        var north = North;
        DrawLine(centre - north * 12, centre + north * 12, ink, 1.2f, true);
        DrawCircle(centre + north * 12, 2, ink);
        var letter = centre + north * 21 + new Vector2(-5, 6);
        DrawString(GetThemeDefaultFont(), letter, "N", fontSize: 15, modulate: ink);
    }
}

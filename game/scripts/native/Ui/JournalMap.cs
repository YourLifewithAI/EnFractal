using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using EnFractal.Native.Kernel;

namespace EnFractal.Native.Ui;

/// <summary>Draws only the team's saved discoveries; neither the room manifest nor the camera reveals unexplored ground.</summary>
public partial class JournalMap : Control
{
    private readonly List<(Rect2 Area, float Height)> _land = new();
    private readonly List<(Rect2 Area, string Name)> _objects = new();
    private Vector2 _player, _companion;
    private float _height;
    public int KnownObjectCount => _objects.Count;
    public int DiscoveredSpanCount => _land.Count;
    public override void _Ready() { MouseFilter = MouseFilterEnum.Ignore; Resized += QueueRedraw; }
    public void UpdateFrom(CommandHost host)
    {
        _land.Clear(); _objects.Clear();
        var data = host.ExportDiscovered();
        var cell = (float)data["cell_m"]!.GetValue<double>();
        foreach (var level in data["levels"]!.AsArray().OfType<JsonObject>())
        {
            var x = (float)level["min_xz_m"]![0]!.GetValue<double>(); var z = (float)level["min_xz_m"]![1]!.GetValue<double>();
            var columns = level["columns"]!.GetValue<int>(); var rows = level["rows"]!.GetValue<int>();
            var height = (float)level["height_m"]!.GetValue<double>();
            var bits = Convert.FromBase64String(level["cells"]!.GetValue<string>());
            bool Seen(int c, int r) { var index = r * columns + c; return (bits[index >> 3] & (1 << (index & 7))) != 0; }
            for (var r = 0; r < rows; r++) for (var c = 0; c < columns; c++)
            {
                if (!Seen(c, r)) continue;
                var start = c; while (c + 1 < columns && Seen(c + 1, r)) c++;
                _land.Add((new Rect2(x + start * cell, z + r * cell, (c - start + 1) * cell, cell), height));
            }
        }
        foreach (var known in data["entities"]!.AsObject())
        {
            var entity = known.Value!["entity"]!; var bounds = entity["bounds_m"]!;
            var low = KernelJson.ReadVector(System.Text.Json.JsonSerializer.SerializeToElement(bounds["min_m"]));
            var high = KernelJson.ReadVector(System.Text.Json.JsonSerializer.SerializeToElement(bounds["max_m"]));
            _objects.Add((new Rect2(low.X, low.Z, high.X - low.X, high.Z - low.Z), entity["display_name"]!.GetValue<string>()));
        }
        var player = host.Player?.GlobalPosition ?? Vector3.Zero; var companion = host.Companion?.GlobalPosition ?? player;
        _player = new Vector2(player.X, player.Z); _companion = new Vector2(companion.X, companion.Z); _height = player.Y;
        QueueRedraw();
    }
    public override void _Draw()
    {
        var box = new Rect2(_player - Vector2.One * 0.2f, Vector2.One * 0.4f).Expand(_companion);
        foreach (var land in _land) box = box.Merge(land.Area);
        foreach (var obj in _objects) box = box.Merge(obj.Area);
        box = box.Grow(0.15f);
        var scale = MathF.Min((Size.X - 30) / box.Size.X, (Size.Y - 40) / box.Size.Y);
        var margin = (Size - box.Size * scale) * 0.5f;
        Vector2 At(Vector2 p) => margin + (p - box.Position) * scale;
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.55f, 0.40f, 0.18f, 0.10f), false, 1);
        foreach (var land in _land)
            DrawRect(new Rect2(At(land.Area.Position), land.Area.Size * scale), new Color(0.35f, 0.42f, 0.26f, MathF.Abs(land.Height - _height) < 0.12f ? 0.45f : 0.16f));
        foreach (var obj in _objects)
            DrawRect(new Rect2(At(obj.Area.Position), obj.Area.Size * scale), new Color(0.4f, 0.28f, 0.15f, 0.65f), false, 1.2f);
        var ink = new Color("694533"); DrawCircle(At(_player), 5, ink);
        var g = At(_companion);
        DrawPolyline(new[] { g + Vector2.Up * 6, g + Vector2.Right * 6, g + Vector2.Down * 6, g + Vector2.Left * 6, g + Vector2.Up * 6 }, new Color("376e6b"), 2, true);
        DrawString(GetThemeDefaultFont(), new Vector2(Size.X - 30, 23), "N", fontSize: 17, modulate: ink);
        DrawLine(new Vector2(Size.X - 24, 30), new Vector2(Size.X - 24, 46), ink, 1);
    }
}

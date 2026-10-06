using Godot;
using System.Linq;

namespace EnFractal.Native;

public partial class NativeGameBoot : Node
{
    public override void _Ready()
    {
        var arguments = OS.GetCmdlineUserArgs();
        // Release templates reject command-line scene overrides. This bounded probe
        // uses the real exported entry point, without reading or writing user saves.
        if (arguments.Contains("--native-contract-probe"))
        {
            AddChild(new NativeContractProbe());
            return;
        }
        var world = GD.Load<PackedScene>("res://scenes/room.tscn").Instantiate<RoomWorld>();
        var room = arguments.FirstOrDefault(a => a.StartsWith("--room="))?["--room=".Length..];
        if (!string.IsNullOrWhiteSpace(room)) world.RoomDirectory = RoomWorld.ResolveRoom(room);
        DisplayServer.WindowSetTitle("EnFractal");
        AddChild(world);
    }
}

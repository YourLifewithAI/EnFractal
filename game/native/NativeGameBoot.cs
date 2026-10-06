using Godot;

namespace EnFractal.Native;

public partial class NativeGameBoot : Node
{
    public override void _Ready()
    {
        // Release templates reject command-line scene overrides. This bounded probe
        // uses the real exported entry point, without reading or writing user saves.
        if (OS.GetCmdlineUserArgs().Contains("--native-contract-probe"))
        {
            AddChild(new NativeContractProbe());
            return;
        }
        DisplayServer.WindowSetTitle("EnFractal - Test Room");
        AddChild(GD.Load<PackedScene>("res://scenes/room_test.tscn").Instantiate());
    }
}

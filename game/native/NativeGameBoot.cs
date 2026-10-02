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
        AddChild(GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate());
    }
}

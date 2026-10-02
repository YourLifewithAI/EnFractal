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
        var legacy = OS.GetCmdlineUserArgs().Contains("--legacy-barton");
        if (legacy && OS.HasFeature("template"))
        {
            GD.PushError("Barton is a retired development fixture and is not included in native exports.");
            GetTree().Quit(1);
            return;
        }
        DisplayServer.WindowSetTitle(legacy ? "EnFractal - retired Barton fixture" : "EnFractal - Pfluger District");
        var scene = legacy ? "res://scenes/main.tscn" : "res://scenes/pfluger_world.tscn";
        AddChild(GD.Load<PackedScene>(scene).Instantiate());
    }
}

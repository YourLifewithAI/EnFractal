using Godot;
using Godot.Collections;
using EnFractal.Native.Look;
using EnFractal.Native.Room;

namespace EnFractal.Native;

/// <summary>Runs unchanged in editor and exported release; never opens a user save.</summary>
public partial class NativeContractProbe : Node
{
    public override void _Ready()
    {
        try
        {
            using var contract = new NativeWorldContract();
            var profile = contract.GetDefaultProfile();
            if (!contract.ValidateProfile(profile) || profile["height_m"].AsDouble() != 0.10 ||
                profile["meters_per_world_unit"].AsDouble() != 1.0 || profile["schema_version"].AsInt32() != 1)
                throw new System.InvalidOperationException("Native metric profile failed");
            using var adapter = new LegacyCreationCompiler();
            var source = Json.ParseString(Godot.FileAccess.GetFileAsString("res://creation_templates/updraft_totem.json"));
            var accepted = adapter.Compile(source);
            var rejected = adapter.Compile(new Dictionary { ["schema"] = "untrusted" });
            using var original = (RefCounted)GD.Load<GDScript>("res://scripts/creation_compiler.gd").New().AsGodotObject();
            var direct = original.Call("compile", source).AsGodotDictionary();
            var directRejected = original.Call("compile", new Dictionary { ["schema"] = "untrusted" }).AsGodotDictionary();
            if (!accepted["ok"].AsBool() || rejected["ok"].AsBool() ||
                original.Call("canonical_json", accepted).AsString() != original.Call("canonical_json", direct).AsString() ||
                original.Call("canonical_json", rejected).AsString() != original.Call("canonical_json", directRejected).AsString())
                throw new System.InvalidOperationException("C# compiler adapter diverged from existing rules");
            // Shipped data must load with every pinned hash intact, exactly as the game will read it.
            var room = RoomData.Load(RoomWorld.DefaultRoom);
            var style = StylePreset.Resolve(RoomWorld.DefaultStyleId, RoomWorld.DefaultStyleVersion);
            if (room.Objects.Count == 0 || room.Shell.Count == 0 || style.PresetVersion < 1)
                throw new System.InvalidOperationException("Shipped room or style preset is empty");
            GD.Print($"Native release probe passed: compiled C#, 0.10 m profile, shared GDScript compiler, valid/invalid inputs, identical artifacts, room {room.RoomId} and style {style.PresetId}@{style.PresetVersion} hashes verified");
            GetTree().Quit(0);
        }
        catch (System.Exception error)
        {
            GD.PushError(error.Message);
            GetTree().Quit(1);
        }
    }
}

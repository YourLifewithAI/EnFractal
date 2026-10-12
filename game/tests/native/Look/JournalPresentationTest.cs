using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using EnFractal.Native;
using EnFractal.Native.Kernel;
using EnFractal.Native.Ui;

namespace EnFractal.Tests.Look;

/// <summary>Real island render and mouse-input coverage for the journal's reversible presentation.</summary>
public partial class JournalPresentationTest : Node
{
    private int _checks;
    private string? _output;
    private RoomHud _hud = null!;
    private JournalPresentation _book = null!;
    private async Task Frames(int count) { for(var i=0;i<count;i++) await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame); }
    private void Check(bool ok,string text) { if(!ok) throw new Exception(text); GD.Print("PASS: "+text); _checks++; }
    private void KeyPress(Key key) => _hud._UnhandledInput(new InputEventKey {PhysicalKeycode=key,Pressed=true});
    private async Task Click(Control control)
    {
        var point=control.GetGlobalRect().GetCenter();
        GetViewport().PushInput(new InputEventMouseMotion {Position=point,GlobalPosition=point});
        GetViewport().PushInput(new InputEventMouseButton {Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=true});
        GetViewport().PushInput(new InputEventMouseButton {Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=false});
        await Frames(1);
    }
    private async Task Capture(string name)
    {
        if(_output==null || DisplayServer.GetName()=="headless") return;
        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        System.IO.Directory.CreateDirectory(_output);
        using var image=GetViewport().GetTexture().GetImage();
        Check(image.SavePng(System.IO.Path.Combine(_output,name+".png"))==Error.Ok,"captured "+name);
    }
    public override async void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();
            _output=args.FirstOrDefault(a=>a.StartsWith("--capture-dir="))?[14..];
            var room=args.First(a=>a.StartsWith("--island="))[9..];
            CommandHost.SaveRoot="user://tests/journal_presentation/"+Guid.NewGuid().ToString("N");
            var world=GD.Load<PackedScene>("res://scenes/room.tscn").Instantiate<RoomWorld>();
            world.RoomDirectory=room; AddChild(world);
            for(var i=0;i<900&&!world.WorldReady&&world.LoadError.Length==0;i++) await Frames(1);
            Check(world.WorldReady,"island loads: "+world.LoadError);
            world.Player.ReadKeyboard=false;
            _hud=world.GetNode<RoomHud>("RoomHud"); _hud.SetViewMode(1);
            for(var i=0;i<3;i++) KeyPress(Key.T);
            var focus=new Vector3(1.2f,0.273f,-2.85f);
            var camera=new Camera3D {Near=0.005f,Fov=52,Position=focus+new Vector3(1.1f,0.9f,-1.6f)};
            AddChild(camera); camera.LookAt(focus); camera.MakeCurrent(); world.Look.FrameCamera(camera,focus);
            _book=(JournalPresentation)_hud.FindChild("JournalPresentation",true,false)!;
            await Frames(90);
            Check(_book.Dock.Visible&&!_hud.JournalOpen,"closed journal has a visible book button");
            var dock=_book.Dock.GetGlobalRect(); var screen=GetViewport().GetVisibleRect();
            Check(Mathf.Abs(screen.End.X-dock.End.X-18)<1&&Mathf.Abs(screen.End.Y-dock.End.Y-18)<1,"book stays 18 pixels from the lower-right edges");
            var footer=(Control)_hud.FindChild("HelpFooter",true,false)!;
            Check(footer.GetGlobalRect().End.X<dock.Position.X,"help footer leaves space for the book");
            await Capture("01-journal-dock");
            await Click(_book.Dock);
            Check(_book.WantsOpen&&!world.Player.InputEnabled,"a mouse click on the book starts opening and blocks movement");
            while(_book.Progress<0.28f) await Frames(1);
            await Capture("02-book-expands");
            while(_book.Progress<0.65f) await Frames(1);
            await Capture("03-cover-opens");
            while(_book.Progress<1) await Frames(1);
            Check(_book.Panel.Visible&&_book.Panel.Page=="Index"&&!_book.Dock.Visible,"opening finishes at the index with one visible book");
            await Capture("04-journal-index");
            var abilities=_book.Panel.FindChildren("*","Button",true,false).OfType<Button>().First(b=>b.Text.Contains("Abilities"));
            await Click(abilities);
            Check(_book.Panel.Page=="Abilities","an index entry opens its section through mouse input");
            await Frames(3); await Capture("05-abilities");
            _book.Panel.OpenPage("Character"); await Frames(3); await Capture("06-character");
            _book.Panel.OpenPage("Map"); await Frames(3); await Capture("07-map");
            var close=_book.Panel.FindChildren("*","Button",true,false).OfType<Button>().First(b=>b.Text.StartsWith("Close the journal"));
            await Click(close);
            Check(!_book.WantsOpen&&_hud.JournalOpen&&!world.Player.InputEnabled,"close button keeps gameplay blocked during the return animation");
            while(_book.Progress>0.65f) await Frames(1);
            await Capture("08-cover-closes");
            while(_book.Progress>0.22f) await Frames(1);
            await Capture("09-book-returns");
            while(_book.Progress>0) await Frames(1);
            Check(_book.Dock.Visible&&!_hud.JournalOpen&&world.Player.InputEnabled,"closing returns to the dock and restores movement");
            await Click(_book.Dock); await Frames(12); KeyPress(Key.Escape); await Frames(3); KeyPress(Key.J);
            await Frames(70);
            Check(_book.Progress==1&&_book.Panel.Page=="Index","rapid reversal reopens cleanly at the index");
            KeyPress(Key.J); await Frames(60);
            Check(_book.Progress==0&&_book.Dock.Visible,"J also closes the book completely");
            GD.Print($"JOURNAL_PRESENTATION: {_checks}/{_checks} checks passed"); GetTree().Quit();
        }
        catch(Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }
}

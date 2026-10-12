using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using EnFractal.Native;
using EnFractal.Native.Kernel;
using EnFractal.Native.Room;

namespace EnFractal.Tests.Look;

public partial class FireRestorationTest : Node
{
    private int _checks, _actions;
    private RoomWorld _world = null!;
    private CommandHost Host => CommandHost.Of(_world)!;
    private Camera3D _camera = null!;
    private string? _output;
    private async Task Frames(int n) { for(var i=0;i<n;i++) await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame); }
    private void Check(bool ok,string text) { if(!ok) throw new Exception(text); GD.Print("PASS: "+text); _checks++; }
    private JsonObject Command(string op,JsonObject args,string? action=null,string principal=CommandHost.PlayerPrincipal) => Host.HandleObject(new JsonObject {
        ["schema"]="enfractal.command",["version"]=1,["room_id"]=_world.Room.RoomId,["action_id"]=action ?? "restore-test-"+(++_actions),["op"]=op,["args"]=args }.ToJsonString(),principal);
    private async Task Walk(EcologyPart part)
    {
        _world.Player.ReadKeyboard=false; _world.Player.SetMovementFrame(0);
        for(var i=0;i<1600;i++)
        {
            var delta=part.Root-_world.Player.GlobalPosition;
            if(new Vector2(delta.X,delta.Z).Length()<0.38f) break;
            _world.Player.SetControlInput(new Vector2(delta.X,-delta.Z).Normalized(),sprint:true);
            await Frames(1);
        }
        _world.Player.SetControlInput(Vector2.Zero); await Frames(10);
        GD.Print("RESTORE_WALK "+part.Id+" player="+_world.Player.GlobalPosition+" root="+part.Root);
    }
    private async Task Capture(string name,EcologyPart p)
    {
        if(_output==null || DisplayServer.GetName()=="headless") return;
        var focus=p.Root+Vector3.Up*0.24f;
        _camera.GlobalPosition=focus+(p.Plant ? new Vector3(0.9f,0.52f,1.1f) : new Vector3(0.4f,0.25f,-0.75f)); _camera.LookAt(focus); _camera.MakeCurrent();
        _world.Look.FrameCamera(_camera,focus);
        await Frames(5); await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        System.IO.Directory.CreateDirectory(_output);
        using var img=GetViewport().GetTexture().GetImage();
        Check(img.SavePng(System.IO.Path.Combine(_output,name+".png"))==Error.Ok,"captured "+name);
    }
    private JsonObject Cast(Vector3 p) => Command("effect.start",new JsonObject {
        ["capability"]="wildfire",["duration_s"]=15,["params"]=new JsonObject(),
        ["area"]=new JsonObject {["center_m"]=new JsonArray(p.X,p.Y,p.Z),["radius_m"]=0.05} });
    public override async void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs(); var structuresOnly=args.Contains("--structures-only"); _output=args.FirstOrDefault(a=>a.StartsWith("--capture-dir="))?[14..];
            CommandHost.SaveRoot="user://tests/fire_restoration/"+Guid.NewGuid().ToString("N");
            _world=GD.Load<PackedScene>("res://scenes/room.tscn").Instantiate<RoomWorld>();
            _world.RoomDirectory=args.First(a=>a.StartsWith("--island="))[9..]; AddChild(_world);
            for(var i=0;i<900&&!_world.WorldReady&&_world.LoadError.Length==0;i++) await Frames(1);
            Check(_world.WorldReady,"island loads: "+_world.LoadError);
            Check(_world.Room.Ecology.Select(p=>p.Species).Distinct().Count()>=10,"all island plant types have distinct seed identities");
            _camera=new Camera3D { Near=0.005f,Fov=52 }; AddChild(_camera);
            var hud=_world.GetNode<RoomHud>("RoomHud"); hud.SetViewMode(1);
            for(var i=0;i<3;i++) hud._UnhandledInput(new InputEventKey { PhysicalKeycode=Key.T,Pressed=true });
            var tree=_world.Room.Ecology.Where(p=>p.Species=="broadleaf"&&p.Role=="foliage").OrderBy(p=>p.Root.DistanceTo(_world.Player.GlobalPosition)).First();
            if (!structuresOnly)
            {
            await Walk(tree);
            var seedArgs=new JsonObject {["target"]=tree.Target,["part_id"]="seed_"+tree.Id,["value"]=1};
            var before=Host.EcologySnapshot(); var revision=Host.Revision;
            Host.Authority.Set("persistence_sink",Callable.From((Godot.Collections.Dictionary _) => new Godot.Collections.Dictionary {["ok"]=false,["message"]="simulated full disk"}));
            var failed=Command("entity.set_part",(JsonObject)seedArgs.DeepClone(),"failed-seed");
            Host.Authority.Set("persistence_sink",new Callable());
            Check(failed["error"]?["code"]?.GetValue<string>()=="persistence_uncertain" && JsonNode.DeepEquals(before,Host.EcologySnapshot()) && Host.Revision==revision,"failed save leaves seeds and landscape unchanged");
            var seed=Command("entity.set_part",(JsonObject)seedArgs.DeepClone(),"seed-once"); Check(seed["ok"]!.GetValue<bool>(),"collect a broadleaf seed: "+seed.ToJsonString());
            Check(Host.SeedsOf(tree.Species)==1,"seed enters inventory");
            var replay=Command("entity.set_part",(JsonObject)seedArgs.DeepClone(),"seed-once");
            Check(replay["replayed"]!.GetValue<bool>()&&Host.SeedsOf(tree.Species)==1,"replayed harvest cannot duplicate seeds");
            Check(!Host.PlayerEcology("seed",tree.Id)["ok"]!.GetValue<bool>(),"unripe plant cannot be harvested repeatedly");
            var spoof=Command("entity.set_part",new JsonObject {["target"]=tree.Target,["part_id"]="ecology_step",["value"]=0.1});
            Check(!spoof["ok"]!.GetValue<bool>(),"external command cannot advance simulation");
            Check(Command("effect.stop",new JsonObject {["effect"]="all"},"eco-step-external-stop")["ok"]!.GetValue<bool>(),"dousing remains available even with a reserved action identifier");
            await Capture("01-living-island",tree);
            var bark=_world.Room.Ecology.First(p=>p.Group==tree.Group&&p.Role=="bark");
            var basePoint=new Vector3(tree.Root.X,bark.Bounds.Position.Y+0.12f,tree.Root.Z);
            var toward=(_world.Player.GlobalPosition-tree.Root); toward.Y=0; toward=toward.Normalized();
            var cast=Cast(basePoint+toward*0.025f); Check(cast["ok"]!.GetValue<bool>(),"ignite by command: "+cast.ToJsonString());
            await Frames(650);
            Check(Host.BurningPartCount>0&&Host.BurningPartCount<=24,"landscape sustains bounded natural combustion");
            await Capture("02-landscape-burning",tree);
            await Frames(650); await Capture("02b-tree-canopy-burning",tree);
            await Frames(950);
            var state=Host.EcologySnapshot()["parts"]![tree.Id];
            Check(state!=null&&state["fuel"]!.GetValue<float>()<=0,"tree canopy fuel is exhausted");
            Host.PlayerStopFire(); await Frames(5);
            Check(Host.BurningPartCount==0,"douse all also stops natural descendants");
            await Capture("03-burned-landscape",tree);
            var planting=Host.PlayerEcology("plant",tree.Id); Check(planting["ok"]!.GetValue<bool>(),"plant collected seed: "+planting.ToJsonString());
            Check(Host.SeedsOf(tree.Species)==0,"planting consumes exactly one seed");
            await Frames(420); await Capture("04-protected-sapling",tree);
            await Frames(1430);
            Check(Host.EcologySnapshot()["parts"]![tree.Id]!["growth"]!.GetValue<float>()==1,"sapling grows to mature plant");
            await Capture("05-regrown-tree",tree);
            var fireAgain=Cast(basePoint+toward*0.025f); Check(fireAgain["ok"]!.GetValue<bool>(),"cast near player-grown tree");
            await Frames(400);
            Check(Host.EcologySnapshot()["parts"]![tree.Id]!["scorch"]!.GetValue<float>()==0,"player-grown tree stays protected");
            Host.PlayerStopFire();
            }
            var stock=_world.Room.Ecology.Where(p=>!p.Plant&&p.Role=="wood").OrderBy(p=>p.Root.DistanceTo(tree.Root)).First();
            // Set up the second test station on the same island. This is fixture placement,
            // not a gameplay teleport or evidence for navigation between the two stations.
            var station=stock.Root+new Vector3(0,1,-0.45f);
            using(var ray=PhysicsRayQueryParameters3D.Create(station,station+Vector3.Down*3,RoomBuilder.WorldLayer))
            {
                var ground=_world.Player.GetWorld3D().DirectSpaceState.IntersectRay(ray);
                Check(ground.Count>0,"structure test station has solid ground");
                _world.Player.GlobalPosition=ground["position"].AsVector3()+Vector3.Up*0.01f;
                _world.Player.Velocity=Vector3.Zero; _world.Player.ResetPhysicsInterpolation();
            }
            await Frames(10);
            var near=_world.Player.EyeCamera.GlobalPosition.Clamp(stock.Bounds.Position,stock.Bounds.End);
            var direction=(_world.Player.EyeCamera.GlobalPosition-near).Normalized();
            using(var castRay=PhysicsRayQueryParameters3D.Create(_world.Player.EyeCamera.GlobalPosition,near,RoomBuilder.WorldLayer))
            {
                var hit=_world.Player.GetWorld3D().DirectSpaceState.IntersectRay(castRay);
                if(hit.Count>0) near=hit["position"].AsVector3()+hit["normal"].AsVector3()*0.003f;
            }
            GD.Print("STOCK_CAST "+near+" bounds "+stock.Bounds);
            var stockCast=Cast(near+direction*0.003f); Check(stockCast["ok"]!.GetValue<bool>(),"ignite stock timber: "+stockCast.ToJsonString());
            await Frames(structuresOnly ? 1700 : 750); Host.PlayerStopFire();
            Check(Host.EcologySnapshot()["parts"]![stock.Id]?["scorch"]?.GetValue<float>()>0,"stock part is scorched");
            await Capture("06-damaged-structure",stock);
            var repair=Host.PlayerEcology("repair",stock.Id); Check(repair["ok"]!.GetValue<bool>(),"repair stock part: "+repair.ToJsonString());
            Check(Host.EcologySnapshot()["parts"]![stock.Id]!["scorch"]!.GetValue<float>()==0,"repair restores original material and fuel");
            await Capture("07-repaired-structure",stock);
            var export=Host.ExportRoomState()!;
            if(_output!=null) System.IO.File.WriteAllText(System.IO.Path.Combine(_output,structuresOnly ? "structure-state.json" : "room-state.json"),export.ToJsonString());
            Check(export["extensions"]!["x_play_ecology"]!=null,"room state includes seeds, growth and damage");
            var bad=(JsonObject)Host.EcologySnapshot().DeepClone(); bad["seeds"]![tree.Species]=-1;
            var badTeam=Host.TeamBlock(); badTeam["ecology"]=bad;
            Check(Host.TeamProblem(badTeam,Host.Revision)!=null,"negative seed inventory is rejected on load");
            var savedRoom=_world.RoomDirectory;
            _world.QueueFree(); await Frames(4);
            _world=GD.Load<PackedScene>("res://scenes/room.tscn").Instantiate<RoomWorld>(); _world.RoomDirectory=savedRoom; AddChild(_world);
            for(var i=0;i<900&&!_world.WorldReady&&_world.LoadError.Length==0;i++) await Frames(1);
            Check(_world.WorldReady,"restored island reloads");
            if (!structuresOnly) Check(Host.SeedsOf(tree.Species)==0&&Host.EcologySnapshot()["parts"]![tree.Id]!["planted"]!.GetValue<bool>(),"seed spending and player-grown protection survive reload");
            Check(Host.EcologySnapshot()["parts"]![stock.Id]!["scorch"]!.GetValue<float>()==0,"structural repair survives reload");
            GD.Print($"FIRE_RESTORATION: {_checks}/{_checks} checks passed"); GetTree().Quit();
        }
        catch(Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }
}

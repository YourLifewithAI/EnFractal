using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using EnFractal.Native;
using EnFractal.Native.Kernel;
using EnFractal.Native.Room;

namespace EnFractal.Tests.Look;

public partial class IslandPlantingTest : Node
{
    private RoomWorld _world = null!;
    private CommandHost Host => CommandHost.Of(_world)!;
    private Camera3D _camera = null!;
    private int _checks,_actions;
    private string? _output;
    private Vector3 _focus;
    private async Task Frames(int n) { for(var i=0;i<n;i++) await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame); }
    private void Check(bool value,string text) { if(!value) throw new Exception(text); GD.Print("PASS: "+text); _checks++; }
    private JsonObject Command(string op,JsonObject args,string? action=null,bool preview=false) => Host.HandleObject(new JsonObject {
        ["schema"]="enfractal.command",["version"]=1,["room_id"]=_world.Room.RoomId,["action_id"]=action ?? "plant-test-"+(++_actions),["op"]=op,["args"]=args,["preview"]=preview }.ToJsonString(),CommandHost.PlayerPrincipal);
    private JsonObject SowArgs(string species)
    {
        var ahead=_world.Player.GlobalPosition-_world.Player.GlobalBasis.Z*0.28f;
        using var ray=PhysicsRayQueryParameters3D.Create(ahead+Vector3.Up*0.3f,ahead+Vector3.Down*0.45f,RoomBuilder.WorldLayer);
        var hit=_world.Player.GetWorld3D().DirectSpaceState.IntersectRay(ray);
        var target=hit.Count>0 ? (hit["collider"].AsGodotObject() as Node)!.GetMeta("entity_id").AsString() : "shell:terrain_meadow";
        return new() {["target"]=target,["part_id"]="sow_"+species,["value"]=1};
    }
    private Vector3 Ground(Vector3 p)
    {
        using var ray=PhysicsRayQueryParameters3D.Create(new Vector3(p.X,2.5f,p.Z),new Vector3(p.X,-0.6f,p.Z),RoomBuilder.WorldLayer);
        var hit=_world.Player.GetWorld3D().DirectSpaceState.IntersectRay(ray);
        if(hit.Count==0) throw new Exception("No fixture ground at "+p);
        return hit["position"].AsVector3();
    }
    // Fixture placement isolates planting from long-distance navigation. All seed/world edits use commands.
    private async Task Station(Vector3 root)
    {
        _world.Player.ReadKeyboard=false; _world.Player.SetControlInput(Vector2.Zero);
        _world.Player.GlobalPosition=Ground(root+Vector3.Back*0.28f)+Vector3.Up*0.005f;
        _world.Player.Rotation=Vector3.Zero; _world.Player.Velocity=Vector3.Zero; _world.Player.ResetPhysicsInterpolation();
        await Frames(3);
    }
    private async Task Capture(string name,bool flowers=false)
    {
        if(_output==null || DisplayServer.GetName()=="headless") return;
        var focus=flowers ? _focus+new Vector3(0,0.025f,-0.22f) : _focus+Vector3.Up*0.20f;
        _camera.GlobalPosition=focus+(flowers ? new Vector3(0.12f,0.20f,-0.26f) : new Vector3(1.10f,0.9f,-1.6f));
        _camera.LookAt(focus); _camera.MakeCurrent(); _world.Look.FrameCamera(_camera,focus);
        await Frames(5); await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        System.IO.Directory.CreateDirectory(_output);
        using var img=GetViewport().GetTexture().GetImage();
        Check(img.SavePng(System.IO.Path.Combine(_output,name+".png"))==Error.Ok,"captured "+name);
    }
    public override async void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs(); _output=args.FirstOrDefault(a=>a.StartsWith("--capture-dir="))?[14..];
            var room=args.First(a=>a.StartsWith("--island="))[9..];
            CommandHost.SaveRoot="user://tests/island_planting/"+Guid.NewGuid().ToString("N");
            await Load(room);
            Check(_world.Room.Ecology.Any(p=>p.Species=="daisies"&&p.Role=="snow")&&_world.Room.Ecology.Any(p=>p.Species=="buttercups"&&p.Role=="gravel"),"flower templates contain blossoms as well as stems");
            _camera=new Camera3D { Near=0.005f,Fov=52 }; AddChild(_camera);
            foreach(var (species,count) in new[]{("broadleaf",3),("conifer",1),("daisies",4),("buttercups",3)})
            {
                var collected=0;
                foreach(var source in _world.Room.Ecology.Where(p=>p.Species==species&&p.Role=="foliage"))
                {
                    await Station(source.Root);
                    var result=Host.PlayerEcology("seed",source.Id);
                    if(result["ok"]!.GetValue<bool>()) collected++;
                    if(collected==count) break;
                }
                Check(Host.SeedsOf(species)==count,"collect "+count+" "+species+" seeds through the command host");
            }
            Host.CycleSeed(); Check(Host.SelectedSeed.Length>0,"seed selector chooses an owned seed");
            // Survey a new coastal meadow using actual placement previews, with no terrain edits.
            var treeSites=new List<Vector3>();
            for(var z=-2.85f;z<=-2.1f&&treeSites.Count<3;z+=0.15f)
            for(var x=0.75f;x<=2.1f&&treeSites.Count<3;x+=0.15f)
            {
                var point=new Vector3(x,0,z);
                if(treeSites.Any(p=>new Vector2(p.X-x,p.Z-z).Length()<0.42f)) continue;
                await Station(point);
                if(Command("entity.set_part",SowArgs("broadleaf"),preview:true)["ok"]!.GetValue<bool>()) treeSites.Add(Ground(point));
            }
            Check(treeSites.Count==3,"a new meadow site can hold three trees");
            _focus=treeSites.Aggregate(Vector3.Zero,(a,b)=>a+b)/3;
            var flowerSites=new List<Vector3>();
            for(var z=_focus.Z-0.22f;z<=_focus.Z+0.3f&&flowerSites.Count<7;z+=0.09f)
            for(var x=_focus.X-0.3f;x<=_focus.X+0.3f&&flowerSites.Count<7;x+=0.10f)
            {
                var point=new Vector3(x,0,z);
                if(treeSites.Any(p=>new Vector2(p.X-x,p.Z-z).Length()<0.16f)) continue;
                await Station(point);
                if(Command("entity.set_part",SowArgs("daisies"),preview:true)["ok"]!.GetValue<bool>()) flowerSites.Add(Ground(point));
            }
            Check(flowerSites.Count==7,"the new meadow has seven clear flower sites");
            GD.Print("GARDEN_ROOT "+_focus+" trees="+string.Join(",",treeSites)+" flowers="+string.Join(",",flowerSites));
            Check(Host.EcologySnapshot()["plantings"]==null,"previews leave no planted instances");
            await Capture("01-new-garden-site");
            await Station(treeSites[0]);
            var before=Host.EcologySnapshot(); var revision=Host.Revision;
            Host.Authority.Set("persistence_sink",Callable.From((Godot.Collections.Dictionary _) => new Godot.Collections.Dictionary {["ok"]=false,["message"]="simulated full disk"}));
            var failed=Command("entity.set_part",SowArgs("broadleaf"));
            Host.Authority.Set("persistence_sink",new Callable());
            Check(failed["error"]?["code"]?.GetValue<string>()=="persistence_uncertain"&&JsonNode.DeepEquals(before,Host.EcologySnapshot())&&Host.Revision==revision,"failed planting save consumes no seed and creates no plant");
            var planted=Command("entity.set_part",SowArgs("broadleaf"),"plant-once");
            Check(planted["ok"]!.GetValue<bool>(),"plant first tree on new ground: "+planted.ToJsonString());
            var replay=Command("entity.set_part",SowArgs("broadleaf"),"plant-once");
            Check(replay["replayed"]?.GetValue<bool>()==true&&Host.SeedsOf("broadleaf")==2&&Host.EcologySnapshot()["plantings"]!.AsObject().Count==1,"replayed planting neither duplicates plant nor spends a second seed");
            var overlap=Host.PlayerPlantSeed("broadleaf");
            Check(overlap["error"]?["code"]?.GetValue<string>()=="occupied"&&Host.SeedsOf("broadleaf")==2,"overlapping trees are refused without spending seeds");
            for(var i=1;i<3;i++)
            {
                await Station(treeSites[i]);
                var result=Host.PlayerPlantSeed(i==1 ? "conifer" : "broadleaf");
                Check(result["ok"]!.GetValue<bool>(),"plant new tree "+i+": "+result.ToJsonString());
            }
            for(var i=0;i<7;i++)
            {
                await Station(flowerSites[i]);
                var result=Host.PlayerPlantSeed(i%2==0 ? "daisies" : "buttercups");
                Check(result["ok"]!.GetValue<bool>(),"plant new flower clump "+i+": "+result.ToJsonString());
            }
            Check(Host.EcologySnapshot()["plantings"]!.AsObject().Count==10,"three trees and seven flower clumps occupy new locations");
            Check(Host.SeedsOf("daisies")==0&&Host.SeedsOf("buttercups")==0&&Host.SeedsOf("conifer")==0,"new planting consumes matching seed inventory");
            Check(!Host.PlayerPlantSeed("daisies")["ok"]!.GetValue<bool>(),"an empty seed pouch cannot create a plant");
            await Frames(420); await Capture("02-young-trees-and-flowers");
            var records=Host.EcologySnapshot()["plantings"]!.AsObject();
            var first=records.First(); var savedPosition=first.Value!["position_m"]!.DeepClone();
            var bad=Host.TeamBlock(); bad["ecology"]!["plantings"]![first.Key]!["position_m"]![0]=5000;
            Check(Host.TeamProblem(bad,Host.Revision)!=null,"out-of-bounds planted save is rejected");
            bad=Host.TeamBlock(); bad["ecology"]!["plantings"]![first.Key]!["template"]="unknown_template";
            Check(Host.TeamProblem(bad,Host.Revision)!=null,"unknown saved plant template is rejected");
            bad=Host.TeamBlock(); bad["ecology"]!["parts"]![first.Key+"_0"]!["planted"]=false;
            Check(Host.TeamProblem(bad,Host.Revision)!=null,"new plants cannot lose their saved protection flag");
            await Frames(1500);
            Check(Host.EcologySnapshot()["parts"]!.AsObject().Where(p=>p.Key.StartsWith("grown_")).All(p=>p.Value!["growth"]!.GetValue<float>()==1),"all new plants mature");
            Check(_world.Built.FindChildren("Trunk_grown_*","StaticBody3D",true,false).Count==3,"new trees have derived trunk collision");
            await Capture("03-established-garden"); await Capture("04-new-flowers",true);
            var saved=Host.ExportRoomState()!;
            if(_output!=null) System.IO.File.WriteAllText(System.IO.Path.Combine(_output,"garden-state.json"),saved.ToJsonString());
            _world.QueueFree(); await Frames(4); await Load(room);
            Check(Host.EcologySnapshot()["plantings"]!.AsObject().Count==10&&JsonNode.DeepEquals(Host.EcologySnapshot()["plantings"]![first.Key]!["position_m"],savedPosition),"new locations and all ten plants survive reload");
            Check(_world.Built.FindChildren("Trunk_grown_*","StaticBody3D",true,false).Count==3,"reload recreates tree collision");
            var newFlower=Host.EcologySnapshot()["plantings"]!.AsObject().First(p=>p.Value!["template"]!.GetValue<string>()==_world.Room.Ecology.First(s=>s.Species=="daisies").Group);
            var a=newFlower.Value!["position_m"]!.AsArray(); await Station(new Vector3(a[0]!.GetValue<float>(),a[1]!.GetValue<float>(),a[2]!.GetValue<float>()));
            Check(Host.PlayerEcology("seed",newFlower.Key+"_0")["ok"]!.GetValue<bool>()&&Host.SeedsOf("daisies")==1,"newly planted flowers become collectible seed sources");
            await Capture("05-garden-after-reload");
            GD.Print($"ISLAND_PLANTING: {_checks}/{_checks} checks passed"); GetTree().Quit();
        }
        catch(Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }
    private async Task Load(string room)
    {
        _world=GD.Load<PackedScene>("res://scenes/room.tscn").Instantiate<RoomWorld>(); _world.RoomDirectory=room; AddChild(_world);
        for(var i=0;i<900&&!_world.WorldReady&&_world.LoadError.Length==0;i++) await Frames(1);
        Check(_world.WorldReady,"island loaded: "+_world.LoadError);
        var hud=_world.GetNode<RoomHud>("RoomHud"); hud.SetViewMode(1);
        for(var i=0;i<3;i++) hud._UnhandledInput(new InputEventKey {PhysicalKeycode=Key.T,Pressed=true});
    }
}

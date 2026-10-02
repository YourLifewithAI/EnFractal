using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using GDictionary = Godot.Collections.Dictionary;
using FileAccess = Godot.FileAccess;

namespace EnFractal.Native;

/// <summary>Bounded source-aligned walkthrough; structural heights and dressing are illustrative.</summary>
public partial class PflugerWorld : Node3D
{
    public const string PackagePath = "res://maps/pfluger_district";
    // Source ground reaches 8.39 m inside the route corridor. A separate flat
    // 9 m deck clears that ridge; this remains an inferred prototype elevation.
    public const float InferredDeckHeightM = 9.0f;
    public const float InferredLakeHeightM = 0.035f;
    public static readonly Rect2 PlayBounds = new(-128, -384, 512, 512);
    public SmallPlayerController Player { get; private set; } = null!;
    public CompanionAvatar Companion { get; private set; } = null!;
    public bool WorldReady { get; private set; }
    public string LoadError { get; private set; } = "";
    public Vector3 Spawn { get; private set; }
    public Vector3[] Route { get; private set; } = Array.Empty<Vector3>();
    public int TreeCount { get; private set; }
    public int BuildingCount { get; private set; }
    public int BridgeSegmentCount { get; private set; }
    public int WaterHoleCount { get; private set; }
    public int TrailSegmentCount { get; private set; }

    private GodotObject _map = null!;
    private Node3D _collision = null!;
    private readonly List<Vector2[][]> _water = new();
    private readonly List<Vector2[]> _trails = new();
    private readonly List<Vector2[]> _buildings = new();
    private Material _deck = null!;
    private readonly StandardMaterial3D _metal = Material("657774");
    private float _landingHeight;

    public override async void _Ready()
    {
        try
        {
            _map = GD.Load<GDScript>("res://scripts/map_runtime.gd").New().AsGodotObject();
            if (!_map.Call("load_package", PackagePath, "pfluger_district_v0").AsBool())
                throw new InvalidOperationException(_map.Get("last_error").AsString());
            var manifest = _map.Get("manifest").AsGodotDictionary();
            var routeFile = PackagePath + "/landing_route.json";
            if (FileAccess.GetSha256(routeFile) != manifest["landing_route_sha256"].AsString())
                throw new InvalidOperationException("Landing route checksum differs from map manifest.");
            var route = Json.ParseString(FileAccess.GetFileAsString(routeFile)).AsGodotDictionary();
            var routeXZ = Points(route["points_local_xz_m"]);
            _landingHeight = SurfaceHeightQuery(routeXZ[0].X, routeXZ[0].Y) + 0.06f;
            Route = routeXZ.Select(p => At(p, DeckHeight(p))).ToArray();
            Spawn = Route[0] + Vector3.Up * 0.008f;
            BuildLight();
            BuildTerrain();
            BuildWater();
            BuildStructures();
            BuildBuildings();
            BuildTrees();
            _collision = GD.Load<GDScript>("res://scripts/terrain_collision_streamer.gd").New().As<Node3D>();
            _collision.Name = "ValidatedSourceTerrainCollision";
            AddChild(_collision);
            _collision.Call("configure", _map, 4096, 2);
            _collision.Call("activate", Spawn);
            // Let PhysicsServer register every terrain/structure collider before actor placement.
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            for (var i = 0; i < Route.Length; i++)
            {
                var ray = PhysicsRayQueryParameters3D.Create(Route[i] + Vector3.Up * 0.5f, Route[i] + Vector3.Down * 0.7f, 1);
                var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
                var body = hit.Count > 0 ? hit["collider"].AsGodotObject() as Node : null;
                if (body == null || !body.HasMeta("surface_role") || body.GetMeta("surface_role").AsString() != "bridge_deck")
                    throw new InvalidOperationException($"Route point {i} lacks a clear structural deck collider.");
                // Keep source XZ; resolve actual piecewise-planar structural Y.
                Route[i] = new Vector3(Route[i].X, hit["position"].AsVector3().Y, Route[i].Z);
            }
            Spawn = Route[0] + Vector3.Up * 0.008f;
            Player = new SmallPlayerController { Name = "Player", Position = Spawn };
            AddChild(Player);
            Player.SetSpawnPoint(Spawn);
            var toward = Route[1] - Route[0];
            Player.Rotation = new Vector3(0, Mathf.Atan2(-toward.X, -toward.Z), 0);
            Companion = new CompanionAvatar { Name = "Companion", Position = Spawn + Player.Basis.X * 0.45f };
            Companion.ConfigureIdentity("local_companion");
            AddChild(Companion);
            Companion.SetSpawnPoint(Companion.Position);
            Companion.BindPlayer(Player);
            Companion.Follow();
            AddChild(new PflugerHud { Player = Player, Companion = Companion });
            SetMeta("source_scope", "USGS 3DEP terrain and OSM horizontal features; 512 m bounded prototype");
            SetMeta("structure_evidence", "Deck/ramp heights, 4.8 m width, 0.32 m slab and 1.10 m guards inferred; not surveyed");
            SetMeta("water_evidence", "OSM exterior/interior rings retained; flat water height inferred from local terrain plateau");
            SetMeta("art_scope", "Original illustrative painterly assets and proxy buildings; not final location or character art");
            WorldReady = true;
            GD.Print($"PFLUGER_WORLD_READY trees={TreeCount} buildings={BuildingCount} bridge_segments={BridgeSegmentCount} water_holes={WaterHoleCount} spawn={Spawn}");
        }
        catch (Exception exception)
        {
            LoadError = exception.Message;
            GD.PushError("Pfluger source scene failed: " + exception);
        }
    }

    public float SurfaceHeightQuery(float x, float z) => _map.Call("surface_height_at", x, z).AsSingle();
    public bool IsWater(Vector2 point) => _water.Any(p => Geometry2D.IsPointInPolygon(point, p[0]) && !p.Skip(1).Any(h => Geometry2D.IsPointInPolygon(point, h)));

    public override void _PhysicsProcess(double delta)
    {
        if (!WorldReady) return;
        var point = new Vector2(Player.GlobalPosition.X, Player.GlobalPosition.Z);
        if (!PlayBounds.Grow(-3).HasPoint(point) || (IsWater(point) && Player.GlobalPosition.Y < InferredLakeHeightM + 0.05f))
        {
            _collision.Call("update_centers", new Godot.Collections.Array<Vector3> { Spawn, Companion.GlobalPosition });
            Player.TryTeleportTo(Spawn);
        }
        _collision.Call("update_centers", new Godot.Collections.Array<Vector3> { Player.GlobalPosition, Companion.GlobalPosition });
        if (!PlayBounds.HasPoint(new Vector2(Companion.Position.X, Companion.Position.Z)) ||
            (IsWater(new Vector2(Companion.Position.X, Companion.Position.Z)) && Companion.Position.Y < InferredLakeHeightM + 0.05f))
            Companion.TryTeleportTo(Spawn + Vector3.Right * 0.5f);
    }

    private IEnumerable<GDictionary> Features(string kind, Rect2 bounds)
        => _map.Call("query_features", kind, bounds).AsGodotArray().Select(v => v.AsGodotDictionary());
    private static Vector2[] Points(Variant value) => value.AsGodotArray().Select(v =>
    {
        var p = v.AsGodotArray(); return new Vector2(p[0].AsSingle(), p[1].AsSingle());
    }).ToArray();
    private static Vector3 At(Vector2 p, float y) => new(p.X, y, p.Y);
    private static StandardMaterial3D Material(string color) => new() { AlbedoColor = new Color(color), Roughness = 0.92f };
    // Extrapolate the entry plane across its full width: clamping at the centre
    // of the first cross-section would lift one edge and bury the spawn beneath it.
    private float DeckHeight(Vector2 p) => Mathf.Lerp(_landingHeight, InferredDeckHeightM, Mathf.Min((p.Y + 178.07274f) / 21.0011f, 1));

    private void BuildLight()
    {
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color("668e9b"), SkyHorizonColor = new Color("e5dfb9"),
            GroundBottomColor = new Color("648177"), GroundHorizonColor = new Color("d1d5b0"),
            SkyCurve = 0.22f, SunAngleMax = 12
        };
        AddChild(new WorldEnvironment { Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky, Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color("adc6b6"),
            AmbientLightEnergy = 0.48f, ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Filmic, TonemapExposure = 0.8f, TonemapWhite = 2.0f, FogEnabled = true,
            FogLightColor = new Color("a0b8bd"), FogLightEnergy = 0.8f, FogSkyAffect = 0,
            FogDensity = 0.00045f
        }});
        AddChild(new DirectionalLight3D { Name = "WarmAfternoon", RotationDegrees = new Vector3(-38, -32, 0),
            LightColor = new Color("fff0ca"), LightEnergy = 0.55f, ShadowEnabled = true,
            DirectionalShadowMaxDistance = 220, ShadowBias = 0.03f });
    }

    private void BuildTerrain()
    {
        const int side = 257;
        var vertices = new Vector3[side * side];
        var normals = new Vector3[vertices.Length];
        var colors = new Color[vertices.Length];
        var uv = new Vector2[vertices.Length];
        for (var z = 0; z < side; z++) for (var x = 0; x < side; x++)
        {
            var p = PlayBounds.Position + new Vector2(x * 2, z * 2);
            var i = z * side + x;
            vertices[i] = At(p, SurfaceHeightQuery(p.X, p.Y));
            colors[i] = new Color(Mathf.Clamp(vertices[i].Y / 55, 0, 1), 0, 0);
            uv[i] = (p + Vector2.One * 2048) / 4096;
        }
        for (var z = 0; z < side; z++) for (var x = 0; x < side; x++)
        {
            var dx = vertices[z * side + Math.Min(x + 1, side - 1)].Y - vertices[z * side + Math.Max(x - 1, 0)].Y;
            var dz = vertices[Math.Min(z + 1, side - 1) * side + x].Y - vertices[Math.Max(z - 1, 0) * side + x].Y;
            normals[z * side + x] = new Vector3(-dx, 4, -dz).Normalized();
        }
        var indices = new List<int>(256 * 256 * 6);
        for (var z = 0; z < side - 1; z++) for (var x = 0; x < side - 1; x++)
        {
            var a = z * side + x; indices.AddRange(new[] { a, a + 1, a + side, a + 1, a + side + 1, a + side });
        }
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices; arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors; arrays[(int)Mesh.ArrayType.TexUV] = uv; arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        var ground = GD.Load<GDScript>("res://scripts/painterly_ground_kit.gd").Call("make_terrain_material", Vector2.Zero).As<ShaderMaterial>();
        ground.SetShaderParameter("patch_center", PlayBounds.GetCenter());
        ground.SetShaderParameter("patch_radius", 900.0f);
        AddChild(new MeshInstance3D { Name = "SourceTerrain2m", Mesh = mesh, MaterialOverride = ground });
    }

    private void BuildWater()
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/pfluger_water.gdshader") };
        foreach (var area in Features("water_areas", PlayBounds))
        {
            var rings = new List<Vector2[]> { Points(area["outline"]) };
            if (area.TryGetValue("holes", out var holes)) foreach (var hole in holes.AsGodotArray()) rings.Add(Points(hole));
            _water.Add(rings.ToArray()); WaterHoleCount += rings.Count - 1;
            var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles);
            foreach (var q in PolygonStrips(rings, PlayBounds)) Quad(surface, At(q[0], InferredLakeHeightM), At(q[1], InferredLakeHeightM), At(q[2], InferredLakeHeightM), At(q[3], InferredLakeHeightM), Vector3.Up);
            AddChild(new MeshInstance3D { Name = "MappedWaterWithHoles", Mesh = surface.Commit(), MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
    }

    // Exact scan strips split at all ring vertices and clipping-edge crossings.
    // Even-odd crossings retain island holes without treating a ring as a filled polygon.
    private static IEnumerable<Vector2[]> PolygonStrips(IEnumerable<Vector2[]> rings, Rect2 clip)
    {
        var edges = new List<(Vector2 A, Vector2 B)>(); var cuts = new SortedSet<float> { clip.Position.Y, clip.End.Y };
        foreach (var ring in rings) for (var i = 0; i < ring.Length; i++)
        {
            var a = ring[i]; var b = ring[(i + 1) % ring.Length];
            if (Mathf.Abs(a.Y - b.Y) < 0.000001f) continue;
            edges.Add((a, b));
            if (a.Y > clip.Position.Y && a.Y < clip.End.Y) cuts.Add(a.Y);
            foreach (var x in new[] { clip.Position.X, clip.End.X }) if ((a.X < x) != (b.X < x))
            {
                var z = Mathf.Lerp(a.Y, b.Y, (x - a.X) / (b.X - a.X));
                if (z > clip.Position.Y && z < clip.End.Y) cuts.Add(z);
            }
        }
        var levels = cuts.ToArray();
        static float X((Vector2 A, Vector2 B) e, float z) => Mathf.Lerp(e.A.X, e.B.X, (z - e.A.Y) / (e.B.Y - e.A.Y));
        for (var i = 1; i < levels.Length; i++)
        {
            var z0 = levels[i - 1]; var z1 = levels[i]; var middle = (z0 + z1) * 0.5f;
            var crosses = edges.Where(e => (e.A.Y < middle) != (e.B.Y < middle)).OrderBy(e => X(e, middle)).ToArray();
            for (var j = 0; j + 1 < crosses.Length; j += 2)
            {
                var l0 = Mathf.Clamp(X(crosses[j], z0), clip.Position.X, clip.End.X); var l1 = Mathf.Clamp(X(crosses[j], z1), clip.Position.X, clip.End.X);
                var r0 = Mathf.Clamp(X(crosses[j + 1], z0), clip.Position.X, clip.End.X); var r1 = Mathf.Clamp(X(crosses[j + 1], z1), clip.Position.X, clip.End.X);
                if (r0 - l0 + r1 - l1 > 0.0001f) yield return new[] { new Vector2(l0, z0), new Vector2(r0, z0), new Vector2(r1, z1), new Vector2(l1, z1) };
            }
        }
    }

    private void BuildStructures()
    {
        var concrete = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/pfluger_deck.gdshader") };
        concrete.SetShaderParameter("surface_paint", GD.Load<Texture2D>("res://assets/art/barton/textures/ground-paint-v1.png"));
        _deck = concrete;
        Ribbon("Candidate150mRoute_InferredStructure", Route, 4.8f, _deck, true);
        BridgeSegmentCount += Route.Length - 1;
        foreach (var p in new[] { new Vector2(140.54f, -64.48f), new Vector2(109.11f, 4.25f), new Vector2(81.13f, 41.92f) })
            Beam("IllustrativeConcretePier", At(p, SurfaceHeightQuery(p.X, p.Y)), At(p, InferredDeckHeightM - 0.32f), 1.1f, 1.1f, _deck, true);
        // Continue the source centreline from the candidate route to the south landing.
        foreach (var feature in Features("trails", PlayBounds))
        {
            var points = Points(feature["points"]); _trails.Add(points);
            var id = feature["osm_id"].AsInt64(); var tags = feature["tags"].AsGodotDictionary();
            if (id == 238575801)
            {
                var continuation = new[] { new Vector2(Route[^1].X, Route[^1].Z) }.Concat(points.Skip(2));
                var path = continuation.Select(p => At(p, InferredDeckHeightM)).ToArray();
                Ribbon("PflugerMainSpan_Inferred", path, 4.8f, _deck, true); BridgeSegmentCount += path.Length - 1;
                var fork = points.Take(3).Select(p => At(p, InferredDeckHeightM)).ToArray();
                Ribbon("PflugerLoopFork_Inferred", fork, 4.8f, _deck, true); BridgeSegmentCount += fork.Length - 1;
            }
            else if (id is 27401202 or 127249123)
            {
                Ribbon("PflugerFork_Inferred", points.Select(p => At(p, InferredDeckHeightM)).ToArray(), 4.8f, _deck, true);
                BridgeSegmentCount += points.Length - 1;
            }
            else if (feature["name"].AsString().Contains("Pfluger") || tags.ContainsKey("bridge") ||
                     id is 27401207 or 1513606533 || tags.TryGetValue("highway", out var highway) && highway.AsString() == "steps") continue;
            else
            {
                // Subdivide on the terrain grid; these remain explicitly inferred path surfaces.
                var path = new List<Vector3>();
                for (var i = 1; i < points.Length; i++)
                {
                    var count = Math.Max(1, Mathf.CeilToInt(points[i - 1].DistanceTo(points[i]) / 2));
                    for (var j = 0; j < count; j++)
                    {
                        var p = points[i - 1].Lerp(points[i], j / (float)count);
                        if (PlayBounds.Grow(-4).HasPoint(p) && !IsWater(p)) path.Add(At(p, SurfaceHeightQuery(p.X, p.Y) + 0.012f));
                        else if (path.Count > 1) { Ribbon("MappedTrail_InferredWidth", path.ToArray(), 2.4f, Material("655e48"), false); TrailSegmentCount += path.Count - 1; path.Clear(); }
                        else path.Clear();
                    }
                }
                if (path.Count > 1) { Ribbon("MappedTrail_InferredWidth", path.ToArray(), 2.4f, Material("655e48"), false); TrailSegmentCount += path.Count - 1; }
            }
        }
    }

    private void Ribbon(string name, Vector3[] points, float width, Material material, bool guards)
    {
        if (points.Length < 2) return;
        var left = new Vector3[points.Length]; var right = new Vector3[points.Length];
        for (var i = 0; i < points.Length; i++)
        {
            var direction = points[Math.Min(i + 1, points.Length - 1)] - points[Math.Max(0, i - 1)]; direction.Y = 0;
            var across = direction.Normalized().Cross(Vector3.Up) * width * 0.5f;
            left[i] = points[i] + across; right[i] = points[i] - across;
            if (guards)
            {
                // The approach is one inferred ramp plane. Sampling its elevation
                // at both edges prevents overlapping turn panels forming small
                // ledges that stop a 0.30 m capsule at the tight north loop.
                left[i].Y = DeckHeight(new Vector2(left[i].X, left[i].Z));
                right[i].Y = DeckHeight(new Vector2(right[i].X, right[i].Z));
            }
        }
        var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles);
        for (var i = 1; i < points.Length; i++)
        {
            Quad(surface, left[i - 1], right[i - 1], right[i], left[i], Vector3.Up);
            if (!guards) continue;
            var down = Vector3.Down * 0.32f;
            Quad(surface, right[i - 1] + down, left[i - 1] + down, left[i] + down, right[i] + down, Vector3.Down);
            foreach (var edge in new[] { left, right })
            {
                Quad(surface, edge[i - 1], edge[i - 1] + down, edge[i] + down, edge[i], (edge[i - 1] - points[i - 1]).Normalized());
                if (points[i].Z < -158) continue; // Open north approach and tight source loop.
                var n = Math.Max(1, Mathf.CeilToInt(edge[i - 1].DistanceTo(edge[i]) / 2));
                for (var j = 0; j < n; j++)
                {
                    var a = edge[i - 1].Lerp(edge[i], j / (float)n); var b = edge[i - 1].Lerp(edge[i], (j + 1) / (float)n);
                    var middle = new Vector2((a.X + b.X) * 0.5f, (a.Z + b.Z) * 0.5f);
                    // Connected source paths need open joins, not guards across
                    // the traversable route. These gaps are inferred junctions.
                    if (new[] { new Vector2(140.54f, -64.48f), new Vector2(156.25f, -55.7f), new Vector2(128.85f, -29.06f), new Vector2(81.13f, 41.92f) }.Any(p => p.DistanceTo(middle) < 5.0f)) continue;
                    Beam("GuardTop", a + Vector3.Up * 1.1f, b + Vector3.Up * 1.1f, 0.07f, 0.06f, _metal, false);
                    Beam("GuardLow", a + Vector3.Up * 0.10f, b + Vector3.Up * 0.10f, 0.06f, 0.10f, _metal, false);
                    Beam("InferredGuardCollision", a + Vector3.Up * 0.55f, b + Vector3.Up * 0.55f, 0.04f, 1.1f, null, true);
                    Beam("GuardPost", a, a + Vector3.Up * 1.1f, 0.045f, 0.045f, _metal, false);
                }
            }
        }
        var mesh = surface.Commit(); var body = new StaticBody3D { Name = name, CollisionLayer = 1, CollisionMask = 0 };
        body.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = material });
        body.AddChild(new CollisionShape3D { Shape = mesh.CreateTrimeshShape() });
        body.SetMeta("surface_role", guards ? "bridge_deck" : "trail_surface");
        body.SetMeta("evidence", "OSM horizontal alignment; width and vertical construction inferred for traversal"); AddChild(body);
    }

    private void BuildBuildings()
    {
        foreach (var building in Features("buildings", PlayBounds.Grow(350)))
        {
            var polygon = Points(building["footprint"]); _buildings.Add(polygon);
            var proxy = building["proxy"].AsGodotDictionary(); var x = proxy["x"].AsSingle(); var z = proxy["z"].AsSingle();
            var height = building["height_m"].AsSingle(); var ground = SurfaceHeightQuery(x, z);
            var size = new Vector3(proxy["width_m"].AsSingle(), height, proxy["length_m"].AsSingle());
            var body = new StaticBody3D { Name = "MappedBuildingProxy", Position = new Vector3(x, ground + height * 0.5f, z),
                Rotation = new Vector3(0, proxy["yaw_rad"].AsSingle(), 0), CollisionLayer = 1, CollisionMask = 0 };
            var tone = BuildingCount % 3; var mat = Material(tone == 0 ? "6b8583" : tone == 1 ? "928d77" : "536f7a");
            body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = mat });
            if (PlayBounds.HasPoint(new Vector2(x, z))) body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            body.SetMeta("feature_id", building["feature_id"]); body.SetMeta("height_evidence", building["height_evidence"]);
            body.SetMeta("geometry_scope", "OSM oriented bounding proxy; illustrative facade; not a surveyed building");
            AddChild(body); BuildingCount++;
        }
    }

    private void BuildTrees()
    {
        var vegetation = Features("vegetation_areas", PlayBounds).Select(a => Points(a["outline"])).ToArray();
        var rng = new RandomNumberGenerator { Seed = 41025 };
        var assets = GD.Load<GDScript>("res://scripts/painterly_assets.gd");
        for (var attempt = 0; attempt < 2400 && TreeCount < 85; attempt++)
        {
            var p = new Vector2(rng.RandfRange(-112, 368), rng.RandfRange(-368, 112));
            if (!vegetation.Any(v => Geometry2D.IsPointInPolygon(p, v)) || IsWater(p) ||
                _buildings.Any(v => Geometry2D.IsPointInPolygon(p, v)) || NearTrail(p, 4.5f)) continue;
            var tree = assets.Call("make_tree", "oak", true).As<Node3D>();
            tree.Name = "IllustrativeOak_" + TreeCount; tree.Position = At(p, SurfaceHeightQuery(p.X, p.Y));
            tree.Rotation = new Vector3(0, rng.RandfRange(0, Mathf.Tau), 0); tree.Scale = Vector3.One * rng.RandfRange(0.8f, 1.2f);
            tree.SetMeta("placement_evidence", "Seeded dressing in mapped vegetation area; not a surveyed individual"); AddChild(tree); TreeCount++;
        }
    }

    private bool NearTrail(Vector2 point, float clearance)
    {
        foreach (var line in _trails) for (var i = 1; i < line.Length; i++)
            if (Geometry2D.GetClosestPointToSegment(point, line[i - 1], line[i]).DistanceTo(point) < clearance) return true;
        return false;
    }

    private void Beam(string name, Vector3 a, Vector3 b, float width, float height, Material? material, bool collision)
    {
        var direction = b - a; if (direction.LengthSquared() < 0.00001f) return;
        var body = new StaticBody3D { Name = name, Position = (a + b) * 0.5f, CollisionLayer = collision ? 1u : 0, CollisionMask = 0 };
        var size = new Vector3(width, height, direction.Length());
        body.Basis = Basis.LookingAt(direction, Mathf.Abs(direction.Normalized().Dot(Vector3.Up)) > 0.99f ? Vector3.Right : Vector3.Up);
        if (material != null) body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material });
        if (collision) body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        AddChild(body);
    }

    private static void Quad(SurfaceTool surface, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
    {
        foreach (var vertex in new[] { a, b, d, b, c, d }) { surface.SetNormal(normal); surface.AddVertex(vertex); }
    }

    public override void _ExitTree() { if (GodotObject.IsInstanceValid(_map)) _map.Dispose(); }
}

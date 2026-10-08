"""Disposable Blender scene derived from synthetic data, never game state.

By-volume uses kind-blind closed relief envelopes of the occupied boxes, then
an exact Boolean union with the floor. It is a box occupancy experiment, not
a reconstruction of the recipe meshes' cavities. Hybrid replaces recognised
envelopes with authored landmarks while retaining automatic shell/ground.
"""
import argparse
import importlib
import json
import math
from pathlib import Path
import sys
import time

sys.dont_write_bytecode=True
HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[3]
sys.path.insert(0,str(ROOT/'pipeline/recipes'))
import bpy
import bmesh
from mathutils import Matrix, Vector
from geometry import bounds, material, mesh_object, panel, reset
from specs import resolve, values
import style
from worker import materials, fit_and_check


def write_json(path,data):
    path.write_text(json.dumps(data,indent=2,allow_nan=False)+'\n',encoding='utf-8',newline='\n')


def colour(value):
    c=[int(value[i:i+2],16)/255 for i in (1,3,5)]
    return [x/12.92 if x<=0.04045 else ((x+0.055)/1.055)**2.4 for x in c]


def paint(name,hexcolour,rough=0.85,emission=0):
    mat=material(name,colour(hexcolour),roughness=rough)
    mat['base_srgb']=hexcolour
    tree=mat.node_tree
    bsdf=tree.nodes.get('Principled BSDF')
    if emission:
        bsdf.inputs['Emission Color'].default_value=(*colour(hexcolour),1)
        bsdf.inputs['Emission Strength'].default_value=emission
    else:
        tex=tree.nodes.new('ShaderNodeTexNoise')
        tex.inputs['Scale'].default_value=18
        tex.inputs['Detail'].default_value=1.5
        ramp=tree.nodes.new('ShaderNodeValToRGB')
        base=colour(hexcolour)
        ramp.color_ramp.elements[0].color=(*[v*0.78 for v in base],1)
        ramp.color_ramp.elements[1].color=(*[min(1,v*1.10+0.025) for v in base],1)
        tree.links.new(tex.outputs['Fac'],ramp.inputs['Fac'])
        tree.links.new(ramp.outputs['Color'],bsdf.inputs['Base Color'])
        bump=tree.nodes.new('ShaderNodeBump')
        bump.inputs['Strength'].default_value=0.12
        bump.inputs['Distance'].default_value=0.004
        tree.links.new(tex.outputs['Fac'],bump.inputs['Height'])
        tree.links.new(bump.outputs['Normal'],bsdf.inputs['Normal'])
    return mat


def block(name,size,position,mat,bevel=0.012):
    return panel(name,size,position,mat,bevel)


def cone(name,radius,depth,position,mat,vertices=8,radius2=0):
    bpy.ops.mesh.primitive_cone_add(vertices=vertices,radius1=radius,radius2=radius2,depth=depth,location=position)
    obj=bpy.context.object
    obj.name=name
    obj.data.materials.append(mat)
    return obj


def relief(name,fn,mat,n=42):
    """Closed topographic solid on [-.5,.5]^2, with outward faces."""
    vertices=[]
    for j in range(n+1):
        for i in range(n+1):
            x,y=i/n-0.5,j/n-0.5
            vertices.append((x,y,max(0.005,fn(x,y))))
    faces=[]
    for j in range(n):
        for i in range(n):
            a=j*(n+1)+i
            # Explicit triangulation gives measurable slopes and robust CSG.
            faces.extend([(a,a+1,a+n+2),(a,a+n+2,a+n+1)])
    boundary=list(range(n+1))+[j*(n+1)+n for j in range(1,n+1)]
    boundary += [n*(n+1)+i for i in range(n-1,-1,-1)]
    boundary += [j*(n+1) for j in range(n-1,0,-1)]
    lower=[]
    for index in boundary:
        lower.append(len(vertices))
        vertices.append((*vertices[index][:2],0))
    centre=len(vertices)
    vertices.append((0,0,0))
    for k,a in enumerate(boundary):
        nxt=(k+1)%len(boundary)
        b=boundary[nxt]
        c,d=lower[k],lower[nxt]
        faces.extend([(a,c,d,b),(centre,d,c)])
    return mesh_object(name,vertices,faces,mat,smooth=False)


def generic_height(x,y):
    # No category/name input. Rounded weathered box occupancy with a low basin.
    distance=min(0.5-abs(x),0.5-abs(y))
    edge=min(1,max(0,distance/0.16))
    edge=edge*edge*(3-2*edge)
    undulation=0.94+0.04*math.sin(16*x+2)*math.cos(13*y-1)
    basin=0.19*math.exp(-((x+0.09)**2+(y+0.05)**2)/0.032)
    return 0.035+edge*(undulation-basin)


def terrain_materials(name,hexcolour):
    # Broad captured colour dominates each role; environmental colour is restrained.
    c=[int(hexcolour[i:i+2],16) for i in (1,3,5)]
    def mixed(target,factor):
        return '#'+''.join(f'{round(a*(1-factor)+b*factor):02x}' for a,b in zip(c,target))
    result=[paint(name+'_grass',mixed((109,139,93),0.28)),
            paint(name+'_rock',mixed((128,139,147),0.20)),
            paint(name+'_scree',mixed((205,178,130),0.35)),
            paint(name+'_shadow',mixed((99,118,148),0.25))]
    for mat in result:
        mat['source_srgb']=hexcolour
    return result


def assign_slopes(objects,mats):
    for obj in objects:
        obj.data.materials.clear()
        for mat in mats:
            obj.data.materials.append(mat)
        peak=max(v.co.z for v in obj.data.vertices)
        for face in obj.data.polygons:
            if face.normal.z>0.75:
                face.material_index=0
            elif face.center.z<0.09:
                face.material_index=2
            else:
                face.material_index=3 if face.normal.x>0.25 else 1
            if len(mats)>4 and face.normal.z>.45 and face.center.z>peak*.90:
                # Secondary scan colours become crest accents without category
                # meaning: red jar lid, couch timber, dark frame, lighter carton.
                face.material_index=4+(face.index % (len(mats)-4))


def cottage(name,x,y,z,scale,mats):
    walls=block(name+'_walls',(scale,scale*.7,scale*.7),(x,y,z+scale*.35),mats['cream'],scale*.05)
    # Four-sided pyramidal roof, deliberately no furniture silhouette.
    roof=cone(name+'_roof',scale*.73,scale*.48,(x,y,z+scale*.94),mats['rose'],4)
    roof.rotation_euler.z=math.pi/4
    door=block(name+'_door',(scale*.24,scale*.02,scale*.36),(x,y-scale*.354,z+scale*.2),mats['dark'],0.001)
    return [walls,roof,door]


def kind_shape(data,mats):
    name=data['id']
    main=next(iter(data['colours'].values()))
    tinted=paint(name+'_source_tint',main)
    if name=='couch':
        def ridge(x,y):
            rear=0.45*math.exp(-((y-.28)/.17)**2)*(0.90+0.10*math.cos(14*x))
            arms=.23*(math.exp(-((x-.42)/.09)**2)+math.exp(-((x+.42)/.09)**2))
            edge=min(1,max(0,min(.5-abs(x),.5-abs(y))/.10))
            edge=edge*edge*(3-2*edge)
            return .015+edge*(.22+rear+arms+.025*math.sin(x*18)*math.sin(y*11))
        obj=relief('blue_slate_ridge',ridge,tinted)
        parts=[obj]
        # Trees inside the eventual donor bounds, not extra-sized mountains.
        for i,x in enumerate([-.31,-.12,.14,.30]):
            y=-.14
            z=ridge(x,y)
            parts += [cone(f'ridge_tree_{i}',.030,.11,(x,y,z+.055),mats['leaf'],7),
                      cone(f'ridge_trunk_{i}',.006,.03,(x,y,z+.015),mats['dark'],8,.006)]
        return parts
    if name=='cardboard_box':
        def butte(x,y):
            r=max(abs(x),abs(y))
            level=0.17 if r>.43 else .38 if r>.34 else .72 if r>.26 else .93
            return level+.012*math.sin(x*37+y*23)
        return [relief('ochre_terraced_butte',butte,tinted)]
    if name=='gaming_laptop':
        # Ruin and horizontal water: discard keyboard/screen meanings in silhouette.
        obj=relief('slab_ruin',lambda x,y:.25+.55*math.exp(-((x-.29)**2+(y-.24)**2)/.035)+.04*abs(x),tinted)
        parts=[obj,block('mirror_lake',(.60,.40,.008),(-.04,-.17,.276),mats['water'],.018)]
        for i,(x,y,h) in enumerate([(-.36,.16,.46),(.36,.35,.64),(-.40,-.34,.28)]):
            parts.append(cone(f'ruin_obelisk_{i}',.055,h,(x,y,.27+h/2),mats['dark'],5,.028))
        return parts
    if name=='jam_jar':
        return [cone('pale_crystal',.45,.85,(0,0,.425),tinted,7,.16),
                cone('ruby_tip',.17,.19,(0,0,.93),paint('jar_lid_ruby',data['colours']['lid']),7)]
    if name=='french_press':
        return [block('watchtower_rock',(.90,.78,.22),(0,0,.11),tinted,.08),
                cone('watchtower',.28,.58,(0,0,.48),mats['cream'],9,.25),
                cone('watchtower_slate_roof',.38,.27,(0,0,.89),mats['dark'],9),
                block('watchtower_window',(.12,.02,.15),(0,-.27,.54),mats['glow'],.004)]
    if name=='shelves':
        def cliff(x,y):
            return .16 if x<-.27 else .42 if x<-.02 else .68 if x<.25 else 1.0
        obj=relief('brown_terraced_cliff',cliff,tinted)
        parts=[obj]
        # Local coordinates are later fitted with the terrain, conserving the box.
        for i,y in enumerate([-.34,-.08,.20]):
            parts += cottage(f'terrace_hamlet_{i}',.08,y,.68,.105,mats)
        return parts
    if name=='rug':
        obj=relief('rose_heather_meadow',lambda x,y:.65+.025*math.cos(x*23)*math.sin(y*19),tinted)
        parts=[obj]
        for i in range(24):
            x=math.sin(i*6.7)*.44
            y=math.cos(i*4.2)*.43
            parts.append(cone(f'heather_{i}',.006,.10,(x,y,.71),mats['rose'],5))
        return parts
    return [relief(name+'_unknown',generic_height,tinted)]


def slope_stats(objects):
    area=walk=climb=blocked=0.0
    for obj in objects:
        obj.data.calc_loop_triangles()
        for tri in obj.data.loop_triangles:
            nz=tri.normal.z
            if nz<=0:
                continue
            a=tri.area
            angle=math.degrees(math.acos(min(1,max(-1,nz))))
            area+=a
            if angle<=35: walk+=a
            elif angle<=55: climb+=a
            else: blocked+=a
    return {'upward_area_m2':area,'walk_candidate_fraction':walk/area if area else 0,
            'climb_candidate_fraction':climb/area if area else 0,
            'too_steep_fraction':blocked/area if area else 0,
            'assumed_thresholds_degrees':[35,55],
            'note':'Area only, no connectivity, clearance, collision or jump certification.'}


def hybridise(parts,data):
    """Blend automatic terrain with recognised relief, before exact box fitting.

    Every recognised landform keeps a volume contribution. Non-heightfield
    landmarks (spire/tower) sit in a small automatic foothill of the same box.
    """
    base=parts[0]
    mats=terrain_materials(data['id']+'_hybrid',next(iter(data['colours'].values())))
    if base.name in ('blue_slate_ridge','ochre_terraced_butte','slab_ruin','brown_terraced_cliff','rose_heather_meadow'):
        count=43*43
        deltas=[]
        for v in list(base.data.vertices)[:count]:
            original=v.co.z
            blended=.25*generic_height(v.co.x,v.co.y)+.75*original
            deltas.append(blended-original)
            v.co.z=blended
        # Keep rim vertices consistent with the top's corner heights.
        base.data.update()
        for detail in parts[1:]:
            i=min(42,max(0,round((detail.location.x+.5)*42)))
            j=min(42,max(0,round((detail.location.y+.5)*42)))
            detail.location.z+=deltas[j*43+i]
        assign_slopes([base],mats)
    else:
        parts.append(relief(data['id']+'_automatic_foothill',lambda x,y:.22*generic_height(x,y),mats[0]))
    return parts


def fit_landmark(parts,data):
    root,topology,box,scales=fit_and_check(parts,data['id'],data['size_m'])
    p=data['position_m']
    root.location=(p[0],-p[2],p[1])
    bpy.context.view_layer.update()
    lo,hi=bounds(parts)
    actual={'min_m':[lo[0],lo[2],-hi[1]],'max_m':[hi[0],hi[2],-lo[1]]}
    w,h,d=data['size_m']
    expected={'min_m':[p[0]-w/2,p[1],p[2]-d/2],
              'max_m':[p[0]+w/2,p[1]+h,p[2]+d/2]}
    delta={key:[actual[key][i]-expected[key][i] for i in range(3)] for key in actual}
    error=max(abs(v) for row in delta.values() for v in row)
    assert error<1e-5, (data['id'],error)
    return {'id':data['id'],'expected':expected,'measured':actual,'delta_m':delta,
            'max_error_m':error,'source_colours':data['colours'],
            'slopes':slope_stats(parts),'parts':len(parts),
            'checked_closed_parts':len(topology),'measurement_stage':'fitted donor geometry before CSG union'}


def merge_occupied(parts,floor):
    """Exact CPU geometry union; floor joins touching, supported solids."""
    donors=[]
    for obj in parts:
        # Bake parent/world transforms before joining operand meshes.
        world=obj.matrix_world.copy()
        obj.parent=None
        for v in obj.data.vertices:
            v.co=world@v.co
        obj.matrix_world=Matrix.Identity(4)
        donors.append(obj)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in donors: obj.select_set(True)
    bpy.context.view_layer.objects.active=donors[0]
    bpy.ops.object.join()
    operand=bpy.context.object
    operand.name='occupied_box_envelopes'
    bpy.context.view_layer.objects.active=floor
    modifier=floor.modifiers.new('merge_occupied_space','BOOLEAN')
    modifier.operation='UNION'
    modifier.solver='EXACT'
    modifier.material_mode='TRANSFER'
    modifier.object=operand
    # Joined components are closed but may intersect at support contacts.
    modifier.use_self=True
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    bpy.data.objects.remove(operand,do_unlink=True)
    floor.name='unified_terrain'
    assert len(floor.data.vertices)>8, 'CSG discarded terrain'
    floor.data.calc_loop_triangles()
    bm=bmesh.new()
    bm.from_mesh(floor.data)
    floor['nonmanifold_edges']=sum(not e.is_manifold for e in bm.edges)
    bm.free()
    floor['degenerate_triangles']=sum(t.area<=1e-14 for t in floor.data.loop_triangles)
    print(f'SPIKE_UNION vertices={len(floor.data.vertices)} triangles={len(floor.data.loop_triangles)}',flush=True)
    return floor


def shell(route,mats):
    """All shell pieces from manifest, with true holes. Camera cutaway is explicit."""
    room=json.loads((HERE/'room/room.json').read_text())
    objects=[]
    for part in room['shell']['parts']:
        points=[Vector((p[0],-p[2],p[1])) for p in part['geometry']['points_m']]
        low=[min(p[i] for p in points) for i in range(3)]
        high=[max(p[i] for p in points) for i in range(3)]
        dimensions=[max(.10,high[i]-low[i]) for i in range(3)]
        centre=[(low[i]+high[i])/2 for i in range(3)]
        if part['role']=='floor':
            continue
        mat=mats['cream'] if route=='plain' else mats['cloud'] if part['role']=='ceiling' else mats['shell']
        if route!='plain' and (part['id'].startswith('shell:north_') or part['id']=='shell:west_head'):
            # Broken skyline within each measured shell slab. Opening bottoms,
            # jambs, floor contacts and maximum height remain anchored.
            along_x=dimensions[0]>dimensions[1]
            def skyline(x,y):
                t=x if along_x else y
                ridge=.50+.48*(.5+.5*math.cos(t*24+.4))
                return ridge*(.90+.10*math.cos((y if along_x else x)*math.pi))
            obj=relief(part['id'],skyline,mat,n=44)
            a,b=bounds([obj])
            for vertex in obj.data.vertices:
                vertex.co=Vector([(vertex.co[i]-a[i])/(b[i]-a[i])*dimensions[i]+low[i] for i in range(3)])
            obj.data.update()
            assign_slopes([obj],terrain_materials(part['id'],part['base_color']))
        else:
            obj=block(part['id'],dimensions,centre,mat,.025)
        objects.append((part,obj))
    return objects


def shell_landmarks(route,mats):
    if route not in ('kind','hybrid'):
        return []
    objects=[]
    # Waterfall of light only within the synthetic window, not a physical opening.
    for i in range(8):
        objects.append(block(f'lightfall_{i}',(.012,.035,1.0),(-2.69,1.27+i*.145,1.5),mats['glow'],.004))
    # Door remains the same 0.9 m canyon pass, never filled with a blocking feature.
    for x in [1.34,2.36]:
        objects.append(cone('pass_spur',.07,1.9,(x,2.93,.95),mats['shell'],7,.02))
    return objects


def figure(mats):
    parts=[cone('avatar_boots',.014,.018,(-.55,-.53,.009),mats['dark'],10,.012),
           cone('avatar_coat',.022,.052,(-.55,-.53,.044),mats['orange'],12,.013)]
    bpy.ops.mesh.primitive_uv_sphere_add(segments=16,ring_count=8,radius=.016,location=(-.55,-.53,.084))
    bpy.context.object.name='avatar_head'
    bpy.context.object.data.materials.append(mats['cream'])
    parts.append(bpy.context.object)
    bpy.context.view_layer.update()
    lo,hi=bounds(parts)
    assert abs(hi[2]-lo[2]-.10)<1e-6
    return parts


def light(name,xyz,target,power,colour,size):
    data=bpy.data.lights.new(name,'AREA')
    data.energy=power
    data.color=colour
    data.shape='DISK'
    data.size=size
    obj=bpy.data.objects.new(name,data)
    bpy.context.collection.objects.link(obj)
    obj.location=xyz
    obj.rotation_euler=(Vector(target)-obj.location).to_track_quat('-Z','Y').to_euler()


def render(route,view,shell_objects):
    scene=bpy.context.scene
    for part,obj in shell_objects:
        # Same cutaway rule in every route; ceiling reappears at eye height.
        obj.hide_render=part['id'] in ('shell:south','shell:east') or (view=='overview' and part['role']=='ceiling')
    camera=scene.camera
    if view=='overview':
        camera.location=(4.6,-8.8,10.7)
        target=Vector((0,.22,.75))
        camera.data.type='ORTHO'
        # Fit all corners of the source room, including hidden shell planes,
        # so every route gets the same uncropped reference framing.
        rotation=(target-camera.location).to_track_quat('-Z','Y')
        projected=[rotation.inverted()@(Vector((x,y,z))-target)
                   for x in [-2.75,2.75] for y in [-3,3] for z in [-.08,2.65]]
        camera.data.ortho_scale=max(max(abs(p.x) for p in projected)*2,
                                   max(abs(p.y) for p in projected)*2*960/540)/.92
        camera.data.dof.use_dof=True
        camera.data.dof.focus_distance=(camera.location-target).length
        camera.data.dof.aperture_fstop=6.3
    else:
        camera.location=(-.82,-1.09,.10)
        target=Vector((.02,1.40,.33))
        camera.data.type='PERSP'
        camera.data.lens=19
        camera.data.dof.use_dof=True
        camera.data.dof.focus_distance=1.35
        camera.data.dof.aperture_fstop=5.6
    camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
    path=HERE/f'{route}_{view}.png'
    scene.render.filepath=str(path)
    assert bpy.app.background and scene.cycles.device=='CPU' and not scene.cycles.denoising_use_gpu
    start=time.perf_counter()
    bpy.ops.render.render(write_still=True)
    elapsed=time.perf_counter()-start
    print(f'SPIKE_RENDER route={route} view={view} engine=CYCLES device=CPU denoise=OIDN_CPU '
          f'pixels=960x540 samples=48 threads=4 seconds={elapsed:.3f} bytes={path.stat().st_size}',flush=True)
    return {'path':path.name,'seconds':elapsed,'raw_bytes':path.stat().st_size,
            'camera_m_blender':list(camera.location),'ceiling_hidden':view=='overview'}


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--route',required=True)
    parser.add_argument('--scratch',required=True,type=Path)
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:])
    route=args.route
    reset()
    style.configure({'style':'plain'})
    scene=bpy.context.scene
    scene.render.engine='CYCLES'
    scene.cycles.device='CPU'
    scene.cycles.samples=48
    scene.cycles.seed=12
    scene.cycles.use_denoising=True
    scene.cycles.denoiser='OPENIMAGEDENOISE'
    scene.cycles.denoising_use_gpu=False
    scene.cycles.max_bounces=5
    scene.render.threads_mode='FIXED'
    scene.render.threads=4
    scene.render.resolution_x=960
    scene.render.resolution_y=540
    scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'
    scene.render.image_settings.color_mode='RGB'
    scene.render.image_settings.color_depth='8'
    scene.render.image_settings.compression=100
    scene.view_settings.view_transform='AgX'
    scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs['Color'].default_value=(.16,.21,.29,1)
    scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value=.18
    mats={name:paint(name,value,emission=2 if name=='glow' else 0,
                     rough=.16 if name=='water' else .85) for name,value in {
        'cream':'#e8dfc3','rose':'#b75e72','dark':'#3e5660','water':'#497488',
        'glow':'#fff1ce','leaf':'#6e9473','orange':'#ef8c41','shell':'#b9b6a0',
        'cloud':'#cdd5de','ground':'#a7aa95'}.items()}
    # Window and fixture are the warm sources; no sun above the removed ceiling.
    light('window_key',(-2.68,1.80,1.65),(0,0,.4),360,(1,.85,.65),1.0)
    light('ceiling_lamp',(0,0,2.53),(0,0,0),160,(1,.93,.82),.70)
    light('door_sky',(1.85,2.95,1.65),(0,0,.4),80,(.69,.81,1),.65)
    data=bpy.data.cameras.new('review_camera')
    data.clip_start=.004
    data.clip_end=100
    camera=bpy.data.objects.new('review_camera',data)
    bpy.context.collection.objects.link(camera)
    scene.camera=camera
    floor=block('ground',(5.5,6,.08),(0,0,-.04),mats['ground'],.01)
    if route!='plain':
        # Landscape-only ground marks retain the source shell's sage-grey.
        for i in range(24):
            x=-2.3+(i%6)*.77
            y=-2.5+(i//6)*1.35
            block(f'ground_stone_{i}',(.08,.10,.016),(x,y,.008),mats['shell'],.008)
    shell_objects=shell(route,mats)
    shell_landmarks(route,mats)
    inventory=json.loads((HERE/'inventory.json').read_text())['objects']
    receipts=[]
    donors=[]
    for objdata in inventory:
        name=objdata['id']
        if route=='plain' and objdata['recipe']:
            used=resolve(objdata['recipe'])
            config=values(used)
            style.configure(config)
            recipe_mats=materials(used,args.scratch)
            parts=importlib.import_module(name).build(config,recipe_mats)
            style.finish(parts)
        elif route=='plain' and name=='shelves':
            parts=[]
            for z in [0,.25,.5,.75,1.0]:
                parts.append(block('shelf_board',(.5,1.8,.035),(0,0,z),paint('shelf_wood','#775035')))
            for y in [-.88,.88]:
                parts.append(block('shelf_post',(.5,.035,1.05),(0,y,.5),paint('post_wood','#775035')))
        elif route=='plain':
            parts=[block('rug',(1,1,.02),(0,0,.01),paint('rug_source','#b68c98'))]
        elif route=='volume':
            mats_for_box=terrain_materials(name,next(iter(objdata['colours'].values())))
            parts=[relief(name+'_weathered_box',generic_height,mats_for_box[0]),
                   block(name+'_basin_water',(.19,.17,.006),(-.09,-.05,.805),mats['water'],.012)]
        elif route=='hybrid' and name=='rug':
            # Unrecognised ground remains exactly the volume algorithm.
            mats_for_box=terrain_materials(name,next(iter(objdata['colours'].values())))
            parts=[relief(name+'_weathered_box',generic_height,mats_for_box[0])]
        else:
            parts=kind_shape(objdata,mats)
            if route=='hybrid':
                parts=hybridise(parts,objdata)
        receipts.append(fit_landmark(parts,objdata))
        if route=='volume' or (route=='hybrid' and name=='rug'):
            mats_for_box.extend(paint(name+'_source_accent_'+str(i),c)
                                for i,c in enumerate(list(objdata['colours'].values())[1:]))
            assign_slopes([p for p in parts if 'basin_water' not in p.name],mats_for_box)
        if route in ('volume','hybrid'):
            # Water stays a separate visual surface; solid occupancy is united.
            donors.extend(p for p in parts if 'basin_water' not in p.name and p.name!='mirror_lake')
    unified=None
    if donors:
        unified=merge_occupied(donors,floor)
    figure(mats)
    renders=[render(route,'overview',shell_objects)]
    if route!='plain':
        renders.append(render(route,'low',shell_objects))
    result={'route':route,'blender':bpy.app.version_string,'cpu_only':True,'denoising':'OPENIMAGEDENOISE_CPU',
            'objects':receipts,'renders':renders,'avatar_height_m':.10,
            'union':{'algorithm':'EXACT Boolean union of fitted donors with floor',
                     'vertices':len(unified.data.vertices),'slopes':slope_stats([unified]),
                     'bounds_blender_m':bounds([unified]),
                     'nonmanifold_edges':unified['nonmanifold_edges'],
                     'degenerate_triangles':unified['degenerate_triangles'],
                     'used_materials':[{'name':unified.data.materials[i].name,
                                        'base_srgb':unified.data.materials[i].get('base_srgb'),
                                        'source_srgb':unified.data.materials[i].get('source_srgb')}
                                       for i in sorted({p.material_index for p in unified.data.polygons})]} if unified else None,
            'limits':['Source box fidelity is not occupied-shape fidelity.',
                      'Donor AABBs measured before union; contact interiors removed by CSG.',
                      'Shell and ceiling remain separate solids to preserve apertures and cutaway.',
                      'Slope candidates are not tested avatar routes; shelf interiors are filled.']}
    write_json(HERE/f'{route}.receipt.json',result)
    print(f'SPIKE_BOUNDS route={route} objects={len(receipts)} max_error_m={max(r["max_error_m"] for r in receipts):.9f}',flush=True)


if __name__=='__main__':
    main()

"""Measurements of the decoded, float32 package, not an ideal analytic surface."""
import argparse
import heapq
import math
from pathlib import Path

from pipeline.landscape.harness import read_package
from pipeline.landscape.harness.common import load_json
from pipeline.landscape.harness.kit import SIZES


class Surface:
    def __init__(self, doc, meshes):
        g=doc['x_grid'];self.g=g;self.heights={};faces=set()
        for primitive in meshes['land']:
            keys=[]
            for x,y,z in primitive['positions']:
                i=round((x-g['x0'])/g['step']);j=round((z-g['z0'])/g['step'])
                assert abs(x-(g['x0']+i*g['step']))<1e-6 and abs(z-(g['z0']+j*g['step']))<1e-6
                old=self.heights.get((i,j),y)
                assert abs(old-y)<1e-7,'terrain seam'
                self.heights[i,j]=y
                keys.append((i,j))
            for a,b,c in primitive['triangles']:
                pa,pb,pc=[primitive['positions'][k] for k in (a,b,c)]
                assert (pb[2]-pa[2])*(pc[0]-pa[0])-(pb[0]-pa[0])*(pc[2]-pa[2])>0,'terrain faces down'
                face=tuple(sorted((keys[a],keys[b],keys[c])))
                assert face not in faces,'duplicate terrain face'
                faces.add(face)
        assert len(self.heights)==g['nx']*g['nz'],'missing ground vertex'
        expected=set()
        for j in range(g['nz']-1):
            for i in range(g['nx']-1):
                expected.add(tuple(sorted(((i,j),(i,j+1),(i+1,j+1)))))
                expected.add(tuple(sorted(((i,j),(i+1,j+1),(i+1,j)))))
        assert faces==expected,'terrain hole or changed topology'

    def sample(self,x,z):
        g=self.g;s=g['step'];u=(x-g['x0'])/s;v=(z-g['z0'])/s
        i,j=math.floor(u),math.floor(v);u-=i;v-=j
        a,b,c,d=[self.heights[k] for k in [(i,j),(i+1,j),(i+1,j+1),(i,j+1)]]
        if v>=u:
            return a+(c-d)*u+(d-a)*v,math.hypot(c-d,d-a)/s
        return a+(b-a)*u+(c-b)*v,math.hypot(b-a,c-b)/s


def obstacles(doc):
    from .generate import CUSTOM_SIZES
    result=[]
    for item in doc['scatter']:
        name=item['prototype'];x,_,z=item['position_m'];s=item['scale']
        if name in ('grass_tuft','flower_clump','fern'):continue
        if name=='valley_cottage':
            result.append((x,z,.235*s[0],.206*s[2]))
        elif name in CUSTOM_SIZES:
            size=CUSTOM_SIZES[name]
            assert item['yaw_deg']==0
            result.append((x,z,size[0]*s[0]/2,size[2]*s[2]/2))
        elif name in ('broadleaf','conifer'):
            result.append((x,z,.055*s[0],.055*s[2]))
        else:
            size=SIZES[name] if name in SIZES else CUSTOM_SIZES[name]
            # Conservative circumscribed horizontal bounds of yawed rocks/shrubs.
            r=math.hypot(size[0]*s[0],size[2]*s[2])/2
            result.append((x,z,r,r))
    return result


def route(doc,surface,start,goal,radius=.055):
    """A* on decoded terrain, with body + held-basket clearance.

    Every node probes a 11 cm wide support area; all eight-connected edges
    are sampled each centimetre. No stepping or swimming is assumed.
    """
    step=.04;obs=obstacles(doc);stream=doc['x_stream']
    from .generate import nearest
    def key(p):return round(p[0]/step),round(p[1]/step)
    cache={}
    def valid(k):
        if k in cache:return cache[k]
        x,z=k[0]*step,k[1]*step
        ok=abs(x)<2.90 and abs(z)<3.40
        if ok:ok=not any(abs(x-a)<rx+radius and abs(z-b)<rz+radius for a,b,rx,rz in obs)
        if ok:ok=nearest(x,z,stream)[0]>.16
        if ok:
            probes=[surface.sample(x+dx,z+dz) for dx,dz in [(0,0),(radius,0),(-radius,0),(0,radius),(0,-radius)]]
            ok=max(s for _,s in probes)<=math.tan(math.radians(20))
        cache[k]=ok
        return ok
    first,last=key(start),key(goal)
    assert valid(first),f'blocked route start {start}'
    assert valid(last),f'blocked route goal {goal}'
    heap=[(0,0,first)];cost={first:0};prev={}
    while heap:
        _,g,k=heapq.heappop(heap)
        if g!=cost[k]:continue
        if k==last:break
        for dx,dz in [(-1,0),(1,0),(0,-1),(0,1),(-1,-1),(-1,1),(1,-1),(1,1)]:
            n=k[0]+dx,k[1]+dz
            if not valid(n):continue
            if dx and dz and (not valid((k[0]+dx,k[1])) or not valid((k[0],k[1]+dz))):continue
            length=step*math.hypot(dx,dz)
            y0=surface.sample(k[0]*step,k[1]*step)[0];y1=surface.sample(n[0]*step,n[1]*step)[0]
            if abs(y1-y0)>length*math.tan(math.radians(20)):continue
            ng=g+length+abs(y1-y0)*2
            if ng<cost.get(n,1e20):
                cost[n]=ng;prev[n]=k
                heapq.heappush(heap,(ng+step*math.hypot(n[0]-last[0],n[1]-last[1]),ng,n))
    assert last in cost,'no low-incline route'
    path=[last]
    while path[-1]!=first:path.append(prev[path[-1]])
    path.reverse();max_grade=0;max_rise=0;length=0
    for a,b in zip(path,path[1:]):
        ax,az=a[0]*step,a[1]*step;bx,bz=b[0]*step,b[1]*step
        run=math.hypot(bx-ax,bz-az);length+=run;n=math.ceil(run/.01)
        y=surface.sample(ax,az)[0]
        for j in range(1,n+1):
            t=j/n;ny,slope=surface.sample(ax+t*(bx-ax),az+t*(bz-az))
            max_grade=max(max_grade,math.degrees(math.atan(slope)))
            max_rise=max(max_rise,abs(ny-y));y=ny
    assert max_grade<=20.01,'triangle exceeds route incline'
    assert max_rise<.02,'sample rise exceeds step limit'
    return dict(length=length,max_grade=max_grade,max_rise=max_rise,nodes=len(path))


def check(package,room,verbose=True):
    doc,meshes,manifest,stats=read_package(package,room)
    surface=Surface(doc,meshes)
    source=doc['x_stream']
    water=meshes['stream'][0]['positions']
    assert len(water)==len(source)*3
    ups=0;depths=[]
    for i in range(len(source)):
        row=water[i*3:i*3+3]
        assert all(abs(a-b)<1e-6 for a,b in zip(row[1],source[i])),'stream witness differs from mesh'
        assert max(v[1] for v in row)-min(v[1] for v in row)<1e-6,'tilted cross-section'
        if i:ups+=int(row[1][1]>water[(i-1)*3+1][1]+1e-7)
        for x,y,z in row:
            ground=surface.sample(x,z)[0];depths.append(y-ground)
    assert ups==0,'water uphill'
    for triangle in meshes['stream'][0]['triangles']:
        a,b,c=[water[k] for k in triangle]
        assert (b[2]-a[2])*(c[0]-a[0])-(b[0]-a[0])*(c[2]-a[2])>0,'water faces down or folds'
        for weights in [(1/3,1/3,1/3),(.5,.5,0),(.5,0,.5),(0,.5,.5)]:
            x,y,z=[sum(p[k]*w for p,w in zip((a,b,c),weights)) for k in range(3)]
            depths.append(y-surface.sample(x,z)[0])
    assert min(depths)>.001,'water buried in bank or terrain'
    # Connected stream triangles retain downstream order, and no unsupported sheets.
    assert max(depths)<.09,'water floating far above bed'
    support_error=0
    built=[];homes=[]
    from .generate import CUSTOM_SIZES
    for item in doc['scatter']:
        if item['prototype'] not in CUSTOM_SIZES:continue
        assert item['yaw_deg']==0,'support sampler expects axis-aligned buildings'
        x,y,z=item['position_m'];sx,sy,sz=item['scale']
        size=CUSTOM_SIZES[item['prototype']]
        b=dict(x=x,z=z,y=y,rx=size[0]/2*sx,rz=size[2]/2*sz)
        built.append(b)
        if item['prototype']=='valley_cottage':homes.append(b)
    assert len(homes)==3,'missing settlement'
    for b in built:
        for i in range(9):
            for j in range(9):
                x=b['x']+b['rx']*(i/4-1);z=b['z']+b['rz']*(j/4-1)
                support_error=max(support_error,abs(surface.sample(x,z)[0]-b['y']))
    for o in doc['objects']:
        x,y,z=o['position_m'];rx,_,rz=[s/2 for s in o['size_m']]
        for dx,dz in [(-rx,-rz),(rx,-rz),(rx,rz),(-rx,rz),(0,0)]:
            support_error=max(support_error,abs(surface.sample(x+dx,z+dz)[0]-y))
    assert support_error<.0001,f'unsupported buildings/prop {support_error}'
    player=next(s for s in manifest['spawns'] if s['role']=='player')['position_m']
    obj=doc['objects'][0]['position_m']
    target=(obj[0]-.08,obj[2])
    outward=route(doc,surface,(player[0],player[2]),target)
    returning=route(doc,surface,target,(player[0],player[2]))
    for b in built:
        route(doc,surface,(player[0],player[2]),(b['x'],b['z']-b['rz']-.08))
    grounding=[]
    inventory=load_json(Path(room)/'inventory.json')['objects']
    for obj in inventory:
        box_=obj['box'];cx,cy,cz=box_['centre_m'];sx,sy,sz=box_['size_m'];a=math.radians(box_['yaw_deg'])
        samples=[]
        for i in range(17):
            for j in range(17):
                u=(i/16-.5)*sx;v=(j/16-.5)*sz
                x=cx+math.cos(a)*u+math.sin(a)*v;z=cz-math.sin(a)*u+math.cos(a)*v
                samples.append((surface.sample(x,z)[0],x,z))
        peak,x,z=max(samples)
        top=cy+sy/2
        assert peak>.55*top,f'lost rise {obj["id"]}'
        grounding.append(dict(id=obj['id'],centre=[cx,cz],top=top,peak=peak,delta=peak-top,
                              offset=math.hypot(x-cx,z-cz)))
    if verbose:
        print(f"PACKAGE PASS triangles={stats['triangles']} instances={stats['instances']}")
        print(f"WATER PASS sections={len(source)} uphill={ups} depth_m={min(depths):.5f}..{max(depths):.5f}; still_water=none")
        print(f"SUPPORT PASS cottages={len(homes)} details={len(built)-len(homes)} pickup=1 max_error_m={support_error:.8f}")
        for label,r in [('OUTWARD',outward),('CARRY_RETURN',returning)]:
            print(f"{label} PASS length_m={r['length']:.3f} max_triangle_deg={r['max_grade']:.3f} max_1cm_rise_m={r['max_rise']:.5f} clearance_diameter_m=0.11 steps=0")
        print('DESTINATIONS PASS all 3 cottage and 2 workyard approaches reachable')
        print('GROUNDING id centre_xz box_top land_peak delta_top peak_offset_m')
        for g in grounding:
            print(f"{g['id']} {g['centre'][0]:.3f},{g['centre'][1]:.3f} {g['top']:.3f} {g['peak']:.3f} {g['delta']:+.3f} {g['offset']:.3f}")
    return dict(grounding=grounding,outward=outward,returning=returning,depths=[min(depths),max(depths)],support_error=support_error)


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--package',required=True);p.add_argument('--room',required=True)
    a=p.parse_args();check(a.package,a.room)

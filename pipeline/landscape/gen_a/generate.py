"""A sheltered summer valley, inferred from posed observation volumes.

Standard library only. Packages are disposable outputs, never source assets.
"""
import argparse
import json
import math
import random
from pathlib import Path

from pipeline.landscape.harness import write_package
from pipeline.landscape.harness.common import load_json
from pipeline.landscape.harness.kit import box, cone, ellipsoid, fit, SIZES

ROOT = Path(__file__).resolve().parents[3]
SEED = 170621
STEP = .025
CUSTOM_SIZES = {'valley_cottage':[.47,.396,.412], 'drying_line':[.42,.24,.04], 'firewood':[.18,.065,.09]}


def clamp(x, a=0., b=1.):
    return max(a, min(b, x))


def smooth(a, b, x):
    t = clamp((x-a)/(b-a))
    return t*t*(3-2*t)


def distance(a, b):
    return math.hypot(a[0]-b[0], a[1]-b[1])


def shell_distance(x,z):
    """Weather the wall alignment while retaining its approximate position."""
    wx=x+.16*math.sin(2.3*z)+.06*math.sin(5.4*z)
    wz=z+.17*math.sin(2*x)+.06*math.sin(4.7*x)
    return min(3-abs(wx),3.5-abs(wz))


def nearest(x, z, line):
    best = (1e9, 0., 0., 0)
    for i, (a, b) in enumerate(zip(line, line[1:])):
        dx, dz = b[0]-a[0], b[2]-a[2]
        t = clamp(((x-a[0])*dx+(z-a[2])*dz)/(dx*dx+dz*dz))
        d = math.hypot(x-a[0]-t*dx, z-a[2]-t*dz)
        if d < best[0]:
            best = d, a[1]+t*(b[1]-a[1]), (i+t)/(len(line)-1), i
    return best


def curve(points, spacing=.045):
    """Catmull-Rom horizontal bends, monotone linear vertical profile."""
    out = []
    for i in range(len(points)-1):
        p, a, b, q = points[max(0, i-1)], points[i], points[i+1], points[min(len(points)-1, i+2)]
        n = max(2, math.ceil(math.hypot(b[0]-a[0], b[2]-a[2])/spacing))
        for j in range(n):
            t = j/n
            v = [.5*((2*a[k])+(-p[k]+b[k])*t+(2*p[k]-5*a[k]+4*b[k]-q[k])*t*t+
                      (-p[k]+3*a[k]-3*b[k]+q[k])*t*t*t) for k in (0, 2)]
            out.append([v[0], a[1]+t*(b[1]-a[1]), v[1]])
    return out+[points[-1]]


class Valley:
    def __init__(self, room_folder):
        self.folder = Path(room_folder)
        self.room = load_json(self.folder/'room.json')
        self.inventory = load_json(self.folder/'inventory.json')['objects']
        self.lo = self.room['bounds']['min_m']
        self.hi = self.room['bounds']['max_m']
        if self.hi[0]-self.lo[0] != 6 or self.hi[2]-self.lo[2] != 7:
            raise ValueError('This first build supports the synthetic garage shell only')
        self.forms = []
        for obj in sorted(self.inventory, key=lambda o: o['id']):
            b = obj['box']; sx, h, sz = b['size_m']; x, cy, z = b['centre_m']
            supported = obj['placement']['support']['kind'] == 'object'
            confident = obj['confidence'] >= .65
            soft = confident and obj['kind'] in ('couch', 'bean bag')
            a = math.radians(b['yaw_deg'])
            colour = obj['colours'][0]['hex'].lstrip('#')
            rgb = [int(colour[i:i+2], 16)/255 for i in (0, 2, 4)]
            warmth = clamp((rgb[0]-rgb[2])*.8, -.12, .12)
            self.forms.append(dict(id=obj['id'], x=x, z=z, sx=sx, sz=sz, h=h,
                                   top=cy+h/2, support=supported, c=math.cos(a), s=math.sin(a),
                                   soft=soft, warmth=warmth,
                                   apron=.12 if supported else .20+.12*math.sqrt(sx*sz),
                                   kind=obj['kind'] if confident else 'proportion fallback'))
        # Source follows the broad northern volume, rather than its detector label.
        ridge = max((f for f in self.forms if f['z'] < -2 and not f['support']),
                    key=lambda f: f['sx']*f['sz']*f['h'])
        spring_x = ridge['x'] + ridge['sx']*.36
        self.stream = curve([[spring_x,.68*ridge['h']+.12,ridge['z']+.14],
                             [-.48,.40,-2.17],[-.31,.28,-1.45],[-.48,.205,-.68],
                             [-.10,.16,.10],[.25,.13,.78],[.12,.105,1.48],
                             [-.26,.08,2.26],[-.65,.055,2.96],[-.83,.035,3.65]])
        player=next(s['position_m'] for s in self.room['spawns'] if s['role']=='player')
        companion=next(s['position_m'] for s in self.room['spawns'] if s['role']=='companion')
        self.paths = [curve([[player[0],.23,player[2]],[.075,.225,-2.42],[.12,.22,-1.83],
                             [.12,.215,-1.05],[.28,.20,-.40],[.74,.185,.31],
                             [1.03,.17,1.01],[1.0,.17,1.72],[.90,.17,2.30]]),
                      curve([[companion[0],.23,companion[2]],[.58,.226,-2.68],[.075,.225,-2.42]])]
        self.homes = [dict(x=1.52,z=1.25,y=.17,yaw=0,scale=1.0),
                      dict(x=1.48,z=2.13,y=.17,yaw=0,scale=.83),
                      dict(x=.56,z=2.21,y=.17,yaw=0,scale=.90)]
        for h in self.homes:
            self.paths.append(curve([[1.0,.17,h['z']-.39],[h['x'],.17,h['z']-.30],
                                     [h['x'],.17,h['z']]]))
        self.pickup = [1.22,.17,1.63]
        self.paths.append(curve([[1.0,.17,1.5],self.pickup]))
        self.paths.append(curve([[1,.17,1.52],[1.22,.17,1.56],[1.55,.17,1.61]]))
        self.paths.append(curve([[1.52,.17,.95],[1.79,.17,.81]]))
        self.pads = [(h['x'],h['z'],.30*h['scale'],.27*h['scale'],h['y']) for h in self.homes]
        self.pads.append((self.pickup[0],self.pickup[2],.13,.13,.17))
        self.pads.extend([(1.55,1.72,.23,.08,.17),(1.79,.95,.12,.08,.17)])

    def contribution(self, f, x, z):
        dx, dz = x-f['x'], z-f['z']
        u, v = f['c']*dx-f['s']*dz, f['s']*dx+f['c']*dz
        rx, rz = f['sx']/2+f['apron'], f['sz']/2+f['apron']
        # Rounded volumes, softened toes and weathered crests; no recovered cavities.
        r = (abs(u/rx)**2.7+abs(v/rz)**2.7)**(1/2.7)
        if r > 2.5:
            return 0.
        power = 1.65 if f['h']>1.2 else 2.7 if f['soft'] else 3.1
        shape = math.exp(-1.5*r**power)
        weather = 1 + (.15 if f['h']>1.2 else .07)*math.sin(11*x+3*z)*math.sin(8*z-x)*smooth(.05,.9,r)
        return f['h']*(.93 if not f['support'] else .75)*shape*weather

    def natural(self, x, z):
        base = .105+.016*(3.5-z)+.017*math.sin(2*x+z)*math.cos(1.5*z-.6*x)
        large = [self.contribution(f,x,z) for f in self.forms if not f['support']]
        # An Lp union blends foothills without adding every box height together.
        uplift = sum(v**4 for v in large)**.25
        child = sum(self.contribution(f,x,z) for f in self.forms if f['support'])
        edge = shell_distance(x,z)
        boundary = (.14+.09*math.sin(2*x+1.7*z)**2+.12*math.sin(3*z-x)**2)*math.exp(-(edge/.34)**2)
        # Closed entry is a low, scrub-filled saddle; window becomes an east notch.
        boundary *= 1-.50*math.exp(-((x-2.9)**2+(z+1.75)**2)/.35)
        y = base+uplift+child+boundary
        outside = -edge
        if outside > 0:
            # Keep the apron 1 mm above the harness's -0.025 m far plane.
            # Coplanar overlapping sheets produce ray/shadow interference.
            y = (y+.024)*(1-smooth(.1,1.35,outside))-.024
        return y

    def channel(self, x, z):
        return nearest(x,z,self.stream)

    def path(self, x, z):
        return min((nearest(x,z,p) for p in self.paths),key=lambda a:a[0])

    def height(self, x, z):
        y = self.natural(x,z)
        d, wy, t, _ = self.channel(x,z)
        width = .061+.033*t+.012*math.sin(t*math.tau*3)**2
        # A U-shaped channel, holding water; bank rises above its edge.
        if d < width+.17:
            bed = wy-.033+.064*smooth(width*.45,width+.14,d)
            blend = 1-smooth(width+.035,width+.17,d)
            y = y*(1-blend)+bed*blend
        d, py, _, _ = self.path(x,z)
        path_outer=.34 if z<-0.55 else .26
        path_inner=.15 if z<-0.55 else .115
        if d < path_outer:
            blend = 1-smooth(path_inner,path_outer,d)
            y = y*(1-blend)+py*blend
        for px,pz,rx,rz,py in self.pads:
            q=max(abs(x-px)-rx,abs(z-pz)-rz)
            if q<.16:
                blend=1-smooth(.01,.16,q)
                y=y*(1-blend)+py*blend
        return y


def cottage():
    """Small plaster and timber cottage with a real, threshold-free doorway."""
    p = []
    w, depth, wall = .41,.34,.235
    p += [box([.025,wall,depth],[x,wall/2,0],'stone_masonry') for x in [-w/2+.0125,w/2-.0125]]
    p.append(box([w,.235,.025],[0,wall/2,depth/2-.0125],'stone_masonry'))
    # Even the smallest cottage has a doorway wider than the carrying envelope.
    for x in [-.1375,.1375]:
        p.append(box([.135,wall,.025],[x,wall/2,-depth/2+.0125],'stone_masonry'))
    p.append(box([.14,.075,.025],[0,.1975,-depth/2+.0125],'stone_masonry'))
    p.append(dict(role='roof',positions=[[-.235,.23,-.20],[.235,.23,-.20],[0,.365,-.20],
                                       [-.235,.23,.20],[.235,.23,.20],[0,.365,.20]],
                  triangles=[[0,2,1],[3,4,5],[0,3,5],[0,5,2],[1,2,5],[1,5,4],[0,1,4],[0,4,3]]))
    p += [box([.016,.235,.016],[x,.1175,-.182],'timber') for x in [-.198,-.078,.078,.198]]
    p += [box([.40,.014,.018],[0,y,-.183],'timber') for y in [.012,.225]]
    for x in [-.151,.151]:
        p.append(box([.062,.062,.01],[x,.15,-.185],'wood'))
        p.append(box([.047,.046,.012],[x,.15,-.192],'cloth'))
        p.append(box([.006,.046,.014],[x,.15,-.194],'timber'))
        p.append(box([.047,.006,.014],[x,.15,-.194],'timber'))
        p.append(box([.082,.025,.046],[x,.106,-.193],'wood'))
        p.append(ellipsoid([.085,.035,.046],[x,.128,-.195],'foliage'))
    p.append(box([.05,.14,.048],[.126,.315,.065],'stone_masonry'))
    p.append(box([.064,.022,.059],[.126,.385,.065],'stone_masonry'))
    for part in p:
        if part['role']=='stone_masonry':
            part['tints']=[[1,.91,.72,1]]*len(part['positions'])
        elif part['role']=='roof':
            part['tints']=[[1,.76,.63,1]]*len(part['positions'])
    return fit(p,[.47,.396,.412])


def drying_line():
    p=[box([.012,.24,.014],[x,.12,0],'timber') for x in [-.204,.204]]
    p.append(box([.414,.004,.004],[0,.23,0],'wood'))
    for n,cx in enumerate([-.126,0,.126]):
        vs=[];ts=[]
        for j in range(5):
            for i in range(5):
                x=cx+(i/4-.5)*.09;y=.225-j/4*(.095 if n!=1 else .12)
                z=.012*math.sin(i*1.6+n)*j/4
                vs.append([x,y,z])
                if i and j:
                    a=j*5+i;ts.extend([[a-6,a-1,a],[a-6,a,a-5]])
        tint=[1,.91,.66,1] if n!=1 else [.66,.85,1,1]
        p.append(dict(role='cloth',positions=vs,triangles=ts,tints=[tint]*len(vs)))
    return fit(p,CUSTOM_SIZES['drying_line'])


def firewood():
    p=[ellipsoid([.18,.036,.037],[0,.018,z],'wood',4,10) for z in [-.024,.024]]
    p.append(ellipsoid([.17,.036,.037],[0,.047,0],'wood',4,10))
    return fit(p,CUSTOM_SIZES['firewood'])


def terrain_mesh(v):
    x0,z0=-4.6,-5.1
    nx,nz=369,409
    vs=[]; attrs=[]
    for j in range(nz):
        z=z0+j*STEP
        for i in range(nx):
            x=x0+i*STEP; y=v.height(x,z)
            vs.append([x,y,z])
    for j in range(nz):
        for i in range(nx):
            x,y,z=vs[j*nx+i]
            # Coarse derivatives express rock faces; paths blend softly into meadow.
            il,ir=max(0,i-1),min(nx-1,i+1);jb,jt=max(0,j-1),min(nz-1,j+1)
            grad=math.hypot((vs[j*nx+ir][1]-vs[j*nx+il][1])/((ir-il)*STEP),
                            (vs[jt*nx+i][1]-vs[jb*nx+i][1])/((jt-jb)*STEP))
            pd=v.path(x,z)[0]; wd,wy,t,_=v.channel(x,z)
            stone=smooth(.45,1.25,grad)*.96
            if y>1.25: stone=max(stone,.45)
            tint=[.90+.08*math.sin(x*1.5+z)**2,.94,.78+.11*math.sin(z+1)**2,1]
            # Confident observed hues influence local geology, in a restrained palette.
            f=max(v.forms,key=lambda f:v.contribution(f,x,z))
            tint[0]=clamp(tint[0]+f['warmth']); tint[2]=clamp(tint[2]-f['warmth'])
            fade=smooth(.1,1.35,-shell_distance(x,z))
            tint=[t*(1-fade)+fade for t in tint]
            if pd<.18 and grad<.55:
                attrs.append(('path',1-smooth(.09,.18,pd),tint,stone))
            elif wd<.24 and abs(y-wy)<.12:
                attrs.append(('bank',1-smooth(.08,.24,wd),tint,stone))
            else:
                attrs.append(('land',stone,tint,stone))
    groups={}
    for j in range(nz-1):
        for i in range(nx-1):
            a=j*nx+i
            for tri in ([a,a+nx,a+nx+1],[a,a+nx+1,a+1]):
                tag=attrs[tri[0]][0]
                if any(attrs[k][0]=='path' for k in tri):tag='path'
                elif any(attrs[k][0]=='bank' for k in tri):tag='bank'
                p,lookup=groups.setdefault(tag, (dict(role='meadow',blend_role={'land':'cliff','bank':'gravel','path':'worn_path'}[tag],
                                                     positions=[],triangles=[],tints=[],blend_weights=[]),{}))
                face=[]
                for k in tri:
                    if k not in lookup:
                        lookup[k]=len(p['positions']);p['positions'].append(vs[k]);p['tints'].append(attrs[k][2])
                        p['blend_weights'].append(attrs[k][3] if tag=='land' else attrs[k][1] if attrs[k][0]==tag else 0.)
                    face.append(lookup[k])
                p['triangles'].append(face)
    return [p for p,_ in groups.values()],dict(x0=x0,z0=z0,step=STEP,nx=nx,nz=nz)


def water_mesh(v):
    ps=[];ts=[]
    for i,a in enumerate(v.stream):
        b=v.stream[min(i+1,len(v.stream)-1)]; c=v.stream[max(0,i-1)]
        dx,dz=b[0]-c[0],b[2]-c[2];length=math.hypot(dx,dz)
        t=i/(len(v.stream)-1); width=.061+.033*t+.012*math.sin(t*math.tau*3)**2
        for side in [-1,0,1]:
            ps.append([a[0]-dz/length*width*side,a[1],a[2]+dx/length*width*side])
        if i:
            for k in range(2):
                n=i*3+k;ts.extend([[n-3,n+1,n],[n-3,n-2,n+1]])
    return [dict(role='flowing_water',positions=ps,triangles=ts)]


def horizon():
    vs=[];ts=[];tints=[];weights=[]
    n=360
    for ring in range(51):
        r=10+ring*.60
        for i in range(n):
            a=math.tau*i/n
            peak=.35+1.4*(.5+.5*math.sin(a*3+.8))**3+3.2*(.5+.5*math.sin(a*7))**6+1.6*(.5+.5*math.sin(a*11+1.6))**8
            feather=smooth(10,13,r)*(1-smooth(36,40,r))
            h=peak*math.exp(-((r-23-1.4*math.sin(a*5))/5)**2)*feather-.024
            rr=r+.8*math.sin(a*5)+.45*math.sin(a*11)
            vs.append([rr*math.cos(a),h,rr*math.sin(a)])
            tints.append([.64,.76,.87,1]);weights.append(.3+.45*smooth(.1,1,h))
            if ring:
                k=ring*n+i; nxt=ring*n+(i+1)%n
                ts.extend([[k-n,nxt,k],[k-n,nxt-n,nxt]])
    return [dict(role='meadow',blend_role='rock',positions=vs,triangles=ts,tints=tints,blend_weights=weights)]


def populate(v):
    rng=random.Random(SEED)
    scatter=[];clear=[];built=[]
    def add(name,x,z,scale=1,yaw=None,tint=None):
        scales=[scale]*3 if isinstance(scale,(int,float)) else scale
        size=SIZES[name] if name in SIZES else CUSTOM_SIZES[name]
        extent=max(size[0]*scales[0],size[2]*scales[2])*.72
        y=v.height(x,z)
        if abs(x)+extent>2.97 or abs(z)+extent>3.47 or y+size[1]*scales[1]>2.67:
            return False
        item=dict(prototype=name,position_m=[x,y,z],yaw_deg=rng.uniform(-180,180) if yaw is None else yaw,scale=scales)
        if tint:item['tint']=tint
        scatter.append(item)
        return True
    for h in v.homes:
        add('valley_cottage',h['x'],h['z'],h['scale'],0)
        built.append(dict(x=h['x'],z=h['z'],y=h['y'],rx=.235*h['scale'],rz=.206*h['scale']))
        clear.append((h['x'],h['z'],.40))
    for name,x,z in [('drying_line',1.55,1.72),('firewood',1.79,.95)]:
        add(name,x,z,1,0)
        size=CUSTOM_SIZES[name]
        built.append(dict(x=x,z=z,y=.17,rx=size[0]/2,rz=size[2]/2))
        clear.append((x,z,.22))
    # Moist toe slopes support groves. Exposed dry crests remain largely bare.
    for _ in range(430):
        x,z=rng.uniform(-2.82,2.82),rng.uniform(-3.24,3.24)
        y=v.height(x,z);pd=v.path(x,z)[0];wd=v.channel(x,z)[0]
        grad=math.hypot(v.height(x+.03,z)-v.height(x-.03,z),v.height(x,z+.03)-v.height(x,z-.03))/.06
        if pd<.30 or wd<.18 or grad>.90 or y>1.45 or any(math.hypot(x-a,z-b)<r+.18 for a,b,r in clear):continue
        grove=.45+.30*math.sin(2*x+1.5*z)+.20*math.cos(4*z-x)
        moist=wd<.63 or x<-1.4 or x>1.9
        if not moist or grove<.25:continue
        if any(math.hypot(x-a,z-b)<r for a,b,r in clear):continue
        size=rng.uniform(.42,.77)
        name='conifer' if y>.65 and rng.random()<.52 else 'broadleaf'
        if add(name,x,z,[size*rng.uniform(.85,1.12),size,size],tint=[rng.uniform(.77,.96),rng.uniform(.88,1),rng.uniform(.67,.9)]):
            clear.append((x,z,.23))
    for _ in range(1100):
        x,z=rng.uniform(-2.90,2.90),rng.uniform(-3.38,3.38)
        y=v.height(x,z);pd=v.path(x,z)[0];wd=v.channel(x,z)[0]
        grad=math.hypot(v.height(x+.02,z)-v.height(x-.02,z),v.height(x,z+.02)-v.height(x,z-.02))/.04
        if pd<.18 or wd<.13 or any(math.hypot(x-h['x'],z-h['z'])<.33 for h in v.homes):continue
        if math.hypot(x,z)<.32 or math.hypot(x-.40,z+.07)<.20:continue
        if any(math.hypot(x-s['position_m'][0],z-s['position_m'][2])<.11 for s in scatter if s['prototype'] in ('broadleaf','conifer')):continue
        if grad>1.0:continue
        patch=(math.sin(6*x+2*z)+math.sin(7*z-x))*.5
        if patch<-.3:continue
        if wd<.4:name='fern';s=rng.uniform(.26,.45)
        elif y>.7:name='shrub';s=rng.uniform(.2,.42)
        elif rng.random()<.38:name='flower_clump';s=rng.uniform(.22,.42)
        elif rng.random()<.30:name='shrub';s=rng.uniform(.24,.46)
        else:name='grass_tuft';s=rng.uniform(.24,.42)
        add(name,x,z,s,tint=[rng.uniform(.84,1),rng.uniform(.88,1),rng.uniform(.72,1)])
    # Clusters of talus at steep faces, never on the walking surface.
    for f in v.forms:
        if f['support'] or f['h']<.65:continue
        for _ in range(9):
            a=rng.uniform(0,math.tau);x=f['x']+math.cos(a)*(f['sx']/2+.40);z=f['z']+math.sin(a)*(f['sz']/2+.40)
            grad=math.hypot(v.height(x+.02,z)-v.height(x-.02,z),v.height(x,z+.02)-v.height(x,z-.02))/.04
            if grad<.8 and v.path(x,z)[0]>.27 and v.channel(x,z)[0]>.18:
                add('rock',x,z,rng.uniform(.45,.95),tint=[.90,.9,.86])
    objects=[dict(id='orchard_basket',kind='supply',prototype='crate',position_m=v.pickup,
                  yaw_deg=0,size_m=[.045,.039375,.039375],mass_kg=.015,carriable=True,tint=[1,.83,.60])]
    return scatter,objects,built


def build(room,out):
    v=Valley(room)
    ground,grid=terrain_mesh(v)
    scatter,objects,built=populate(v)
    meshes=dict(land=ground,stream=water_mesh(v),horizon=horizon(),cottage=cottage(),drying_line=drying_line(),firewood=firewood())
    grounding=[]
    for f in v.forms:
        # Measured on the same surface function; output checker repeats on float32 triangles.
        grounding.append(dict(id=f['id'],centre_xz=[f['x'],f['z']],box_top=f['top'],
                              kind_rule=f['kind'],support=f['support']))
    doc=write_package(out,room,meshes=meshes,setup=load_json(ROOT/'pipeline/landscape/harness/ab-setup.json'),
                      generator=dict(name='summer-valley',version='1'),terrain=[dict(mesh='land')],
                      water=[dict(mesh='stream',kind='flowing')],scenery=[dict(mesh='horizon',reachable=False)],
                      prototypes={name:dict(mesh='cottage' if name=='valley_cottage' else name,size_m=size) for name,size in CUSTOM_SIZES.items()},
                      scatter=scatter,objects=objects,
                      extensions=dict(x_seed=SEED,x_grid=grid,x_paths=v.paths,x_stream=v.stream,
                                      x_buildings=built,x_grounding=grounding,
                                      x_boundary='Irregular scrub ridges at the shell, scenery beyond is unreachable.',
                                      x_inventions=['spring seam','incised stream and outlet','alluvial settlement terrace']))
    print(f"GENERATED {v.room['room_id']} seed={SEED} forms={len(v.forms)} instances={len(scatter)+len(objects)}")
    return doc


def main():
    p=argparse.ArgumentParser();p.add_argument('--room',required=True,type=Path);p.add_argument('--out',required=True,type=Path)
    a=p.parse_args();build(a.room,a.out)


if __name__=='__main__':main()

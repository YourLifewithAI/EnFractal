"""Small deterministic geometry kit, in metres, bottom-centre pivots.

All helpers return the same plain-list primitive records as generator meshes.
"""
import math

SIZES = {
    'broadleaf': [.65, .95, .65], 'conifer': [.48, 1.1, .48],
    'shrub': [.32, .24, .32], 'grass_tuft': [.12, .13, .12],
    'flower_clump': [.15, .18, .15], 'fern': [.22, .16, .22],
    'rock': [.20, .13, .18], 'boulder': [.55, .42, .48],
    'cottage': [.58, .58, .48], 'tower': [.36, .82, .36],
    'fence': [.60, .24, .08], 'crate': [.16, .14, .14],
    'lantern': [.08, .15, .08],
}


def box(size, centre, role):
    x, y, z = size
    cx, cy, cz = centre
    vertices = [[cx+a*x/2, cy+b*y/2, cz+c*z/2]
                for a,b,c in [(-1,-1,-1),(1,-1,-1),(1,-1,1),(-1,-1,1),
                              (-1,1,-1),(1,1,-1),(1,1,1),(-1,1,1)]]
    faces = [[0,1,2],[0,2,3],[4,7,6],[4,6,5],[0,4,5],[0,5,1],
             [1,5,6],[1,6,2],[2,6,7],[2,7,3],[3,7,4],[3,4,0]]
    return {'positions': vertices, 'triangles': faces, 'role': role}


def ellipsoid(size, centre, role, rings=5, segments=12):
    sx, sy, sz = size
    cx, cy, cz = centre
    vertices = [[cx, cy-sy/2, cz]]
    for j in range(1, rings):
        angle = -math.pi/2 + math.pi*j/rings
        for i in range(segments):
            a = math.tau*i/segments
            vertices.append([cx+sx/2*math.cos(angle)*math.cos(a),
                             cy+sy/2*math.sin(angle),
                             cz+sz/2*math.cos(angle)*math.sin(a)])
    top = len(vertices)
    vertices.append([cx, cy+sy/2, cz])
    triangles = []
    for i in range(segments):
        k = (i+1)%segments
        triangles += [[0,1+i,1+k], [top,1+(rings-2)*segments+k,1+(rings-2)*segments+i]]
    for j in range(rings-2):
        for i in range(segments):
            a=1+j*segments+i; b=1+j*segments+(i+1)%segments
            triangles += [[a,a+segments,b+segments],[a,b+segments,b]]
    return {'positions': vertices, 'triangles': triangles, 'role': role}


def cone(radius, height, y, role, top_radius=0, segments=12):
    # Truncated cones when top_radius>0; avoid coincident apex triangles.
    vs = [[radius*math.cos(i*math.tau/segments), y,
           radius*math.sin(i*math.tau/segments)] for i in range(segments)]
    vs.append([0,y,0]); bottom=segments
    ts = [[bottom,i,(i+1)%segments] for i in range(segments)]
    if top_radius:
        base=len(vs)
        vs += [[top_radius*math.cos(i*math.tau/segments),y+height,
                top_radius*math.sin(i*math.tau/segments)] for i in range(segments)]
        top=len(vs); vs.append([0,y+height,0])
        for i in range(segments):
            k=(i+1)%segments
            ts += [[i,base+i,base+k],[i,base+k,k],[top,base+k,base+i]]
    else:
        top=len(vs); vs.append([0,y+height,0])
        ts += [[i,top,(i+1)%segments] for i in range(segments)]
    return {'positions':vs,'triangles':ts,'role':role}


def fit(primitives, size):
    vertices=[v for p in primitives for v in p['positions']]
    lo=[min(v[i] for v in vertices) for i in range(3)]
    hi=[max(v[i] for v in vertices) for i in range(3)]
    for p in primitives:
        p['positions'] = [[(v[i]-(lo[i] if i==1 else (lo[i]+hi[i])/2))*
                           size[i]/(hi[i]-lo[i]) for i in range(3)] for v in p['positions']]
    return primitives


def prototype(name):
    if name=='broadleaf':
        parts=[cone(.055,.53,0,'bark',.035)]
        parts += [ellipsoid([.42,.42,.42],[x,y,z],'foliage') for x,y,z in
                  [(-.17,.62,-.03),(.16,.65,.02),(0,.82,0),(0,.61,-.17),(0,.61,.16)]]
    elif name=='conifer':
        parts=[cone(.035,.38,0,'bark',.025)]
        parts += [cone(.25-i*.055,.50,.22+i*.20,'foliage') for i in range(3)]
    elif name=='shrub':
        parts=[ellipsoid([.32,.24,.32],[0,.12,0],'foliage')]
    elif name in ('grass_tuft','fern','flower_clump'):
        parts=[]
        for i in range(7):
            a=i*math.tau/7; x=.045*math.cos(a); z=.045*math.sin(a)
            leaf=ellipsoid([.03,.13,.035],[x,.065,z],'foliage')
            if name=='fern':
                for v in leaf['positions']:
                    v[0] += v[1]*math.cos(a)*.5; v[2] += v[1]*math.sin(a)*.5
            parts.append(leaf)
            if name=='flower_clump':
                parts.append(ellipsoid([.045,.025,.045],[x,.15,z],'cloth'))
    elif name in ('rock','boulder'):
        parts=[ellipsoid([1,.75,.86],[0,.375,0],'rock',4,10)]
    elif name=='cottage':
        parts=[box([.54,.32,.44],[0,.16,0],'timber')]
        roof=dict(role='roof',positions=[[-.305,.32,-.245],[.305,.32,-.245],[0,.52,-.245],
                                         [-.305,.32,.245],[.305,.32,.245],[0,.52,.245]],
                  triangles=[[0,2,1],[3,4,5],[0,3,5],[0,5,2],[1,2,5],[1,5,4],[0,1,4],[0,4,3]])
        parts += [roof,box([.10,.15,.02],[0,.075,-.23],'wood'),
                  box([.09,.18,.09],[.17,.44,.10],'stone_masonry')]
        parts += [box([.025,.32,.025],[x,.16,-.23],'wood') for x in [-.25,.25]]
        parts += [box([.105,.09,.018],[x,.20,-.235],'cloth') for x in [-.16,.16]]
    elif name=='tower':
        parts=[cone(.17,.62,0,'stone_masonry',.15),cone(.22,.22,.62,'roof')]
    elif name=='fence':
        parts=[box([.035,.24,.07],[x,.12,0],'timber') for x in [-.27,0,.27]]
        parts += [box([.60,.03,.035],[0,y,0],'timber') for y in [.08,.18]]
    elif name=='crate':
        parts=[box([.16,.14,.14],[0,.07,0],'wood')]
        parts += [box([.17,.02,.15],[0,y,0],'timber') for y in [.015,.125]]
    elif name=='lantern':
        parts=[box([.06,.09,.06],[0,.065,0],'cloth')]
        parts += [box([.08,.02,.08],[0,y,0],'metal') for y in [.01,.12]]
        parts += [cone(.018,.025,.13,'metal',.012)]
    else:
        raise ValueError('unknown prototype: '+name)
    return fit(parts,SIZES[name])


def roles(name):
    return {p['role'] for p in prototype(name)}

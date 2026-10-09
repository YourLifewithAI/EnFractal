"""Solar-time lighting and manifest-only view layout (standard library)."""
import math


def sun_position(latitude_deg,day_of_year,solar_time_h,bearing_deg=0):
    # NOAA fractional-year declination series; time is apparent solar time,
    # so no longitude, equation-of-time or civil-time correction is needed.
    gamma=2*math.pi/365*(day_of_year-1+(solar_time_h-12)/24)
    decl=(.006918-.399912*math.cos(gamma)+.070257*math.sin(gamma)
          -.006758*math.cos(2*gamma)+.000907*math.sin(2*gamma)
          -.002697*math.cos(3*gamma)+.00148*math.sin(3*gamma))
    lat=math.radians(latitude_deg); hour=math.radians(15*(solar_time_h-12))
    altitude=math.asin(max(-1,min(1,math.sin(lat)*math.sin(decl)+math.cos(lat)*math.cos(decl)*math.cos(hour))))
    azimuth=(math.degrees(math.atan2(math.sin(hour),math.cos(hour)*math.sin(lat)-math.tan(decl)*math.cos(lat)))+180)%360
    relative=math.radians(azimuth-bearing_deg)
    direction=[math.cos(altitude)*math.sin(relative),math.sin(altitude),-math.cos(altitude)*math.cos(relative)]
    return dict(altitude_deg=math.degrees(altitude),azimuth_deg=azimuth,direction_room=direction,
                declination_deg=math.degrees(decl),model='NOAA approximate, geometric, apparent solar time')


def ground_height(meshes,terrain,x,z):
    """Vertical ray from above all terrain. Handles arbitrary triangle meshes."""
    hits=[]
    for record in terrain:
        for p in meshes[record['mesh']]:
            vs=p['positions']
            for indices in p['triangles']:
                a,b,c=[vs[i] for i in indices]
                det=(b[2]-c[2])*(a[0]-c[0])+(c[0]-b[0])*(a[2]-c[2])
                if abs(det)<1e-12: continue
                u=((b[2]-c[2])*(x-c[0])+(c[0]-b[0])*(z-c[2]))/det
                v=((c[2]-a[2])*(x-c[0])+(a[0]-c[0])*(z-c[2]))/det
                w=1-u-v
                if min(u,v,w)>=-1e-7:
                    hits.append(u*a[1]+v*b[1]+w*c[1])
    return max(hits) if hits else None


def project_bounds(view, bounds, lens_mm=40., aspect=16/9):
    """Project all eight manifest corners to normalized frame coordinates."""
    def dot(a,b): return sum(x*y for x,y in zip(a,b))
    def cross(a,b): return [a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]]
    def unit(a): return [v/math.sqrt(dot(a,a)) for v in a]
    camera=view['position_m']; forward=unit([t-c for t,c in zip(view['target_m'],camera)])
    right=unit(cross(forward,[0,1,0])); up=cross(right,forward)
    points=[]
    for x in [bounds['min_m'][0],bounds['max_m'][0]]:
        for y in [bounds['min_m'][1],bounds['max_m'][1]]:
            for z in [bounds['min_m'][2],bounds['max_m'][2]]:
                rel=[x-camera[0],y-camera[1],z-camera[2]]; depth=dot(rel,forward)
                if depth<=0: return []
                points.append((.5+dot(rel,right)/depth*lens_mm/36-view.get('shift_x',0),
                               .5+dot(rel,up)/depth*lens_mm/36*aspect-view.get('shift_y',0)*aspect))
    return points


def overview_camera(room, name, sx, sz):
    """Fit at 87% width; a tall, narrow room that cannot fit at any pitch is
    framed a little looser (82%, 77%, ...) rather than failing."""
    for fraction in (.87, .82, .77, .72, .67, .62, .57):
        try:
            return _overview_camera(room, name, sx, sz, fraction)
        except ValueError:
            continue
    raise ValueError('manifest full-height bounds cannot fit overview')


def _overview_camera(room, name, sx, sz, fraction):
    """Fit manifest floor AND roof bounds, with a fixed 87% horizontal span.

    Select the highest pitch that fits the full-height envelope in 16:9.
    Perspective and the three corner directions remain fixed; package geometry
    has no influence. Lens shifts centre the projected envelope without cropping.
    """
    bounds=room['bounds']; lo,hi=bounds['min_m'],bounds['max_m']
    target=[(a+b)/2 for a,b in zip(lo,hi)]; span=max(hi[i]-lo[i] for i in (0,2))
    for pitch in range(60,4,-1):
        def at(distance):
            horizontal=distance*math.cos(math.radians(pitch))/math.sqrt(2)
            return dict(name=name,kind='overview',position_m=[target[0]+sx*horizontal,
                        target[1]+distance*math.sin(math.radians(pitch)),target[2]+sz*horizontal],
                        target_m=target,projection='perspective',ground_height_m=None,ground_missing=False)
        near,far=span*.6,span*20
        for _ in range(55):
            distance=(near+far)/2; points=project_bounds(at(distance),bounds)
            width=max(p[0] for p in points)-min(p[0] for p in points) if points else math.inf
            if width>fraction: near=distance
            else: far=distance
        view=at(far); points=project_bounds(view,bounds)
        ys=[p[1] for p in points]
        if max(ys)-min(ys)<=.92:
            xs=[p[0] for p in points]
            view.update(shift_x=(min(xs)+max(xs))/2-.5,
                        shift_y=(min(ys)-40/720)/(16/9),pitch_deg=pitch)
            if fraction!=.87: view['width_fraction']=fraction
            return view
    raise ValueError('manifest full-height bounds cannot fit overview')


def camera_plan(room,meshes,terrain):
    lo,hi=room['bounds']['min_m'],room['bounds']['max_m']
    cx,cz=(lo[0]+hi[0])/2,(lo[2]+hi[2])/2
    span=max(hi[0]-lo[0],hi[2]-lo[2])
    views=[]
    for name,sx,sz in [('overview_ne',1,-1),('overview_sw',-1,1),('overview_se',1,1)]:
        views.append(overview_camera(room,name,sx,sz))
    for role in ['player','companion']:
        spawn=next((s for s in room['spawns'] if s['role']==role),None)
        if spawn is None: spawn=next(s for s in room['spawns'] if s['role']=='any')
        x,_,z=spawn['position_m']
        ground=ground_height(meshes,terrain,x,z)
        y=(ground if ground is not None else lo[1])+.087
        aim_x,aim_z=cx,cz
        if math.hypot(cx-x,cz-z)<1.5:
            # Farthest wall midpoint from this spawn; tie order is fixed.
            aim_x,aim_z=max([(cx,lo[2]),(hi[0],cz),(cx,hi[2]),(lo[0],cz)],
                           key=lambda p:math.hypot(p[0]-x,p[1]-z))
        views.append(dict(name='eye_'+role,kind='eye',position_m=[x,y,z],
                          target_m=[aim_x,y,aim_z],
                          ground_height_m=ground,ground_missing=ground is None,spawn_id=spawn['id']))
    ground=ground_height(meshes,terrain,cx,cz)
    y=(ground if ground is not None else lo[1])+.087
    windows=[o for o in room['shell'].get('openings',[]) if o['kind']=='window']
    window=sorted(windows,key=lambda w:w['id'])[0]['center_m'] if windows else [cx,y,lo[2]]
    dx,dz=window[0]-cx,window[2]-cz
    length=math.hypot(dx,dz) or 1
    views.append(dict(name='eye_window',kind='eye',position_m=[cx,y,cz],
                      target_m=[cx+dx/length,y,cz+dz/length],ground_height_m=ground,
                      ground_missing=ground is None))
    return views

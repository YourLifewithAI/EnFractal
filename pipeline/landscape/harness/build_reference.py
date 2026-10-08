"""Build the deliberately simple synthetic format fixture; never edits renders."""
import argparse
from pathlib import Path
from .common import SETUP
from .kit import SIZES, box
from .package import write_package

ROOM=Path(__file__).resolve().parents[1]/'corpus/rooms/garage_nominal'
REFERENCE=Path(__file__).resolve().parent/'reference/package'


def quad(x0,x1,z0,z1,y,role):
    return dict(role=role,positions=[[x0,y,z0],[x0,y,z1],[x1,y,z1],[x1,y,z0]],
                triangles=[[0,1,2],[0,2,3]])


def build(out,room=ROOM):
    meadow=quad(-3,0,-3.5,3.5,0,'meadow')
    meadow.update(blend_role='soil',blend_weights=[0,0,1,1],
                  tints=[[.85,1,.9,1],[1,.9,.8,1],[1,1,1,1],[.9,1,1,1]])
    path=quad(0,3,-3.5,3.5,0,'worn_path')
    ridge=dict(role='rock',positions=[[-3,0,3.5],[3,0,3.5],[0,.45,3.15]],triangles=[[0,2,1]])
    mesh={'land':[meadow,path,ridge],
          'pool':[quad(-1.8,-.9,-.5,.3,.012,'still_water')],
          'stream':[quad(-.1,.1,.4,2,.016,'flowing_water')],
          'fall':[dict(role='flowing_water',positions=[[-.1,.02,2],[.1,.02,2],[.1,.25,2],[-.1,.25,2]],triangles=[[0,1,2],[0,2,3]])],
          'horizon':[dict(role='cliff',positions=[[-10,0,9],[10,0,9],[6,1.5,10],[0,2.5,11],[-6,1,10]],triangles=[[0,1,2],[0,2,3],[0,3,4]])],
          'custom':[box([.12,.10,.12],[0,.05,0],'stone')]}
    scatter=[]
    for i,name in enumerate(SIZES):
        scatter.append(dict(prototype=name,position_m=[-2.3+(i%5)*1.05,0,-1.7+(i//5)*1.2],
                            yaw_deg=(i%3)*15,scale=[1,1,1],tint=[1,.9,.8]))
    scatter.append(dict(prototype='marker',position_m=[2.2,0,2],yaw_deg=25,scale=[1,1,1]))
    objects=[dict(id='findable_crate',kind='container',prototype='crate',position_m=[.4,0,-2.8],
                  yaw_deg=0,size_m=[.12,.105,.105],mass_kg=.05,carriable=True,tint=[.8,.9,1])]
    return write_package(out,room,meshes=mesh,setup=dict(SETUP),generator=dict(name='format-fixture',version='1'),
                         terrain=[dict(mesh='land')],water=[dict(mesh='pool',kind='still'),dict(mesh='stream',kind='flowing'),dict(mesh='fall',kind='falling')],
                         scenery=[dict(mesh='horizon',reachable=False)],
                         prototypes={'marker':dict(mesh='custom',size_m=[.12,.1,.12])},scatter=scatter,objects=objects,
                         extensions={'x_note':'Synthetic format fixture, not a landscape design.','x_paths':[[0,0,0],[0,0,1]]})


if __name__=='__main__':
    p=argparse.ArgumentParser(); p.add_argument('--out',type=Path,default=REFERENCE); args=p.parse_args()
    build(args.out)
    print('REFERENCE_BUILT',args.out)

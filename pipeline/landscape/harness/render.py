"""Validate and launch the pinned, CPU-only background Blender worker."""
import argparse
import math
import os
from pathlib import Path
import shutil
import time
import subprocess
import uuid
from .common import canonical, load_json, harness_revision, sha
from .package import read_package, validate_setup

FINAL=dict(width=1280,height=720,samples=48,threads=4,seed=16,max_bounces=5,
           denoiser='OPENIMAGEDENOISE',denoising_use_gpu=False,device='CPU',
           overview_fstop=1.4,overview_lens_mm=40.,eye_lens_mm=22.,
           eye_fstop=32.,eye_focus_m=.40,haze_start_m=.8,haze_density=.008,
           far_haze_start_m=7.,far_haze_density=.10,
           sun_energy=4.5,sun_angle_deg=.65,sky_strength=.65,exposure=-.7,
           view_transform='Standard',view_look='Medium High Contrast')
DRAFT=dict(FINAL,width=480,height=270,samples=12)


def find_blender():
    configured=os.environ.get('BLENDER')
    if configured:
        path=Path(configured).expanduser()
        if not path.is_file(): raise ValueError('BLENDER must name an existing executable')
        return path.resolve()
    root=Path(r'C:\Users\blues\AppData\Local\Programs\Blender')
    matches=sorted(root.rglob('blender.exe')) if root.is_dir() else []
    if len(matches)!=1: raise ValueError('Blender absent or ambiguous; set BLENDER')
    return matches[0].resolve()


def render(package,room,label,out,draft=False,settings=None,expect_setup=None,kit_picture=False,views=None,eye=None):
    start=time.perf_counter()
    package=Path(package).resolve(); room=Path(room).resolve(); out=Path(out).resolve()
    doc=read_package(package,room)[0]  # Reject before creating outputs or starting Blender.
    expected=load_json(expect_setup) if expect_setup else None
    if expected is not None:
        validate_setup(expected)
        if doc['setup']!=expected: raise ValueError('package setup differs from expected setup')
    if out==package or out.is_relative_to(package): raise ValueError('renders must be outside the hashed package')
    if eye is not None and (len(eye)!=4 or not all(math.isfinite(v) for v in eye) or eye[:2]==eye[2:]):
        raise ValueError('eye is x z aim_x aim_z in metres, aiming away from itself')
    if not 0<len(label)<=80 or any(c.upper() not in 'ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 _-.:/' for c in label):
        raise ValueError('label must be 1-80 bitmap-font characters: letters, digits, space, _-.:/')
    blender=find_blender()
    selected=settings or (DRAFT if draft else FINAL)
    previous=None
    if views is not None:
        names={'overview_ne','overview_sw','overview_se','eye_player','eye_companion','eye_window','eye_custom','slope'}
        if not views or len(set(views))!=len(views) or not set(views)<=names:
            raise ValueError('views must be unique known view names')
        previous=load_json(out/'receipt.json')
        if (previous['settings']!=selected or previous['setup']!=doc['setup'] or
            previous['package_sha256']!=sha((package/'package.json').read_bytes()) or
            previous['room_sha256']!=doc['source']['room_sha256']):
            raise ValueError('partial rerender inputs/settings differ from the existing receipt')
        for entry in previous['views']+[previous['diagnostic']]:
            if sha((out/entry['path']).read_bytes())!=entry['sha256']:
                raise ValueError('existing view bytes differ from receipt')
    # os.makedirs rather than tempfile, required by the Windows sandbox ACLs.
    scratch=Path(os.environ.get('TEMP',os.environ.get('TMPDIR','/tmp'))).resolve()/('enfractal-harness-'+uuid.uuid4().hex)
    scratch.mkdir(parents=True)
    out.mkdir(parents=True,exist_ok=True)
    config={'package':str(package),'room':str(room),'out':str(out),'label':label,
            'settings':selected,'repo':str(Path(__file__).resolve().parents[3]),
            'expected_setup':expected,'harness_revision':harness_revision(),'kit_picture':kit_picture,
            'views':views,'previous_receipt':previous,'eye':None if eye is None else [float(v) for v in eye]}
    (scratch/'config.json').write_bytes(canonical(config))
    env=dict(os.environ,BLENDER_USER_CONFIG=str(scratch/'config'),BLENDER_USER_SCRIPTS=str(scratch/'scripts'),
             PYTHONDONTWRITEBYTECODE='1',OMP_NUM_THREADS='4',OIDN_NUM_THREADS='4')
    command=[str(blender),'--background','--factory-startup','--threads','4','--python-exit-code','1',
             '--python',str(Path(__file__).with_name('worker.py')),'--',str(scratch/'config.json')]
    try:
        with (out/'blender.log').open('wb') as log:
            result=subprocess.run(command,env=env,stdout=log,stderr=subprocess.STDOUT,check=False)
        print('BLENDER_EXIT',result.returncode)
        if result.returncode:
            tail='\n'.join((out/'blender.log').read_text(encoding='utf-8',errors='replace').splitlines()[-25:])
            raise RuntimeError('Blender failed; see '+str(out/'blender.log')+'\n'+tail)
    finally:
        shutil.rmtree(scratch)
    receipt=load_json(out/'receipt.json')
    receipt['total_wall_seconds']=round(time.perf_counter()-start,6)
    (out/'receipt.json').write_bytes(canonical(receipt))
    return out


def main():
    parser=argparse.ArgumentParser()
    for flag in ['package','room','label','out']: parser.add_argument('--'+flag,required=True)
    parser.add_argument('--draft',action='store_true')
    parser.add_argument('--expect-setup',type=Path)
    parser.add_argument('--kit-picture',action='store_true',help='also render a labelled kit catalogue')
    parser.add_argument('--views',nargs='+',help='iteration only: replace named views in a verified existing set')
    parser.add_argument('--eye',nargs=4,type=float,metavar=('X','Z','AIM_X','AIM_Z'),
                        help='review only: a 10 cm eye here, looking level toward the aim, replaces eye_window as eye_custom')
    args=parser.parse_args()
    render(args.package,args.room,args.label,args.out,args.draft,
           expect_setup=args.expect_setup,kit_picture=args.kit_picture,views=args.views,eye=args.eye)
    print('RENDER_COMPLETE',args.out)


if __name__=='__main__': main()

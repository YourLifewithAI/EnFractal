"""Trusted local runner: python pipeline/recipes/build.py input.json --out folder."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
from specs import resolve, strict_loads
from contract_check import check_glb


def find_blender():
    configured = os.environ.get('BLENDER')
    if configured:
        path = Path(configured).expanduser()
        if not path.is_file():
            raise ValueError('BLENDER must name an existing Blender executable')
        return path.resolve()
    root = Path(r'C:\Users\blues\AppData\Local\Programs\Blender')
    matches = sorted(root.rglob('blender.exe')) if root.is_dir() else []
    if not matches:
        raise ValueError('Blender not found; set BLENDER to its executable path')
    if len(matches) != 1:
        raise ValueError('Multiple Blender installs found; select one with BLENDER')
    return matches[0].resolve()


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2, allow_nan=False) + '\n',
                    encoding='utf-8', newline='\n')


def build(document, out):
    started = time.perf_counter()
    used = resolve(document)  # Refuse bad input before creating output or launching Blender.
    blender = find_blender()
    out = Path(out).resolve()
    out.mkdir(parents=True, exist_ok=True)
    name = used['recipe']['value']
    targets = [out / (name + suffix) for suffix in ('.glb', '.receipt.json', '.png', '.log')]
    if any(path.exists() for path in targets):
        raise ValueError(f'output already exists for {name}; use a fresh output folder')
    # Default-mode directories remain writable under Windows restricted tokens.
    # Refuse a pre-existing work folder, so cleanup only removes our own files.
    work = out / '.blender-work'
    os.makedirs(work)
    try:
        input_path = work / 'resolved.json'
        write_json(input_path, used)
        env = os.environ.copy()
        for key in ('BLENDER_USER_RESOURCES', 'BLENDER_USER_CONFIG', 'BLENDER_USER_DATAFILES',
                    'BLENDER_USER_SCRIPTS', 'TEMP', 'TMP', 'TMPDIR'):
            directory = work / key.lower()
            os.makedirs(directory)
            env[key] = str(directory)
        env['PYTHONDONTWRITEBYTECODE'] = '1'
        script = Path(__file__).resolve().with_name('worker.py')
        command = [str(blender), '--background', '--factory-startup', '--threads', '4',
                   '--python-exit-code', '1', '--python', str(script), '--',
                   '--input', str(input_path), '--out', str(work)]
        process = subprocess.run(command, env=env, capture_output=True, text=True,
                                 encoding='utf-8', errors='replace', timeout=600)
        log = process.stdout + process.stderr
        if process.returncode:
            targets[3].write_text(log, encoding='utf-8', newline='\n')
            raise RuntimeError(f'Blender exited {process.returncode}; see {targets[3]}')
        glb = work / (name + '.glb')
        problems = check_glb(glb.read_bytes(), name)
        if problems:
            raise RuntimeError('\n'.join(problems))
        receipt_path = work / (name + '.receipt.json')
        receipt = strict_loads(receipt_path.read_text(encoding='utf-8'))
        receipt['checks']['contract_check_glb'] = {'passed': True, 'problems': problems}
        receipt['runtime_seconds']['process'] = time.perf_counter() - started
        write_json(receipt_path, receipt)
        # Publish only after Blender, decoded roundtrip, CPU preview and contract check pass.
        for suffix in ('.png', '.receipt.json', '.glb'):
            (out / (name + suffix)).write_bytes((work / (name + suffix)).read_bytes())
        targets[3].write_text(log, encoding='utf-8', newline='\n')
    finally:
        shutil.rmtree(work)
    print(f"PASS {name}: triangles={receipt['triangle_count']} "
          f"bounds={receipt['dimensions_m']} bytes={receipt['glb']['bytes']} "
          f"seconds={receipt['runtime_seconds']['process']:.3f}", flush=True)
    return receipt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('input', type=Path)
    parser.add_argument('--out', required=True, type=Path)
    args = parser.parse_args()
    try:
        if args.input.stat().st_size > 65536:
            raise ValueError('input exceeds 65536 bytes')
        document = strict_loads(args.input.read_text(encoding='utf-8'))
        build(document, args.out)
    except (ValueError, OSError, RuntimeError, subprocess.TimeoutExpired) as error:
        print(f'ERROR: {error}', file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())

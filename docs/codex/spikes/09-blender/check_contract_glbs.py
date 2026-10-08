"""Use the existing contract validator's binary check without inventing asset metadata."""
import argparse
import sys
from pathlib import Path

sys.dont_write_bytecode = True
repo = Path(__file__).resolve().parents[4]
sys.path.insert(0, str(repo / 'contracts'))
from validate import check_glb

parser = argparse.ArgumentParser()
parser.add_argument('artifact_directory', type=Path)
args = parser.parse_args()
problems = []
for name in ['cardboard_box.glb', 'bonne_maman_jar.glb']:
    errors = check_glb((args.artifact_directory / name).read_bytes(), name)
    problems.extend(errors)
    print(('FAIL ' if errors else 'PASS ') + name + ': contracts/validate.py check_glb')
    for error in errors:
        print(error)
print(f'CONTRACT_GLBS: 2 checked, {len(problems)} problems')
sys.exit(1 if problems else 0)

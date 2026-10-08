"""Read-only audit of this uncommitted delivery; the integrator checks the eventual commit."""
import fnmatch
import hashlib
import re
import subprocess
from pathlib import Path

repo = Path(__file__).resolve().parents[4]
brief = (repo / 'docs/codex/briefs/09-blender-spike.md').read_text(encoding='utf-8')
scope = re.search(r'```scope\n(.*?)```', brief.replace('\r\n', '\n'), re.S).group(1).split()
changed = set()
for command in [('diff', '--name-only', 'HEAD'), ('ls-files', '--others', '--exclude-standard')]:
    changed.update(subprocess.check_output(['git', *command], cwd=repo, text=True).splitlines())
outside = [name for name in changed if not any(fnmatch.fnmatch(name, pattern) for pattern in scope)]
assert not outside, f'Outside brief scope: {outside}'
for name in sorted(changed):
    data = (repo / name).read_bytes()
    if name.endswith(('.glb', '.png')):
        assert len(data) < 1_000_000, f'Binary exceeds 1 MB: {name}'
    else:
        assert not data.startswith(b'\xef\xbb\xbf'), f'UTF-8 BOM: {name}'
        assert b'\r\n' not in data, f'CRLF: {name}'
    if name.endswith('.glb'):
        print(f'SHA256 {Path(name).name} {hashlib.sha256(data).hexdigest()}')
print(f'DELIVERY_AUDIT: PASS {len(changed)} files within scope; binaries <1000000 bytes; text UTF-8/LF')

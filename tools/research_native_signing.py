"""Retain original native signing bytes and disassembly, without running them."""
from pathlib import Path
import hashlib
import json
import shutil
import struct
import subprocess

output = Path('artifacts/native-signing')
output.mkdir(parents=True, exist_ok=True)
sources = sorted({path for root in ('firmware', 'firmware-signing') for path in Path(root).rglob('libsign-lib.so')})
if not sources:
    raise RuntimeError('Original sign-lib was not found in supplied firmware')
manifest = []
for index, source in enumerate(sources):
    payload = source.read_bytes()
    if payload[:4] != b'\x7fELF':
        raise RuntimeError(f'Signing library is not ELF: {source}')
    machine = struct.unpack('<H' if payload[5] == 1 else '>H', payload[18:20])[0]
    directory = output / str(index)
    directory.mkdir(exist_ok=True)
    shutil.copy2(source, directory / source.name)
    for name, args in (
        ('elf.txt', ['readelf', '-h', '-d', '-Ws', str(source)]),
        ('strings.txt', ['strings', '-a', '-t', 'x', str(source)]),
        ('disassembly.txt', [{40: 'arm-linux-gnueabi-objdump', 183: 'aarch64-linux-gnu-objdump'}.get(machine, 'objdump'), '-dr', str(source)]),
    ):
        result = subprocess.run(args, capture_output=True, text=True)
        (directory / name).write_text(result.stdout + result.stderr)
        if result.returncode:
            raise RuntimeError(f'Native signing inspection failed: {args[0]}')
    manifest.append({'source': source.as_posix(), 'sha256': hashlib.sha256(payload).hexdigest(),
                     'bytes': len(payload), 'elfMachine': machine, 'evidenceDirectory': directory.name})
(output / 'provenance.json').write_text(json.dumps(manifest, indent=2))
print(f'Retained {len(manifest)} original signing libraries and inspection records')

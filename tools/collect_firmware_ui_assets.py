"""Retain supplied Android fonts and standalone branding candidates with hashes."""
import hashlib
import json
import shutil
import sys
from pathlib import Path

source, output = map(Path, sys.argv[1:])
records = []
for partition in ('system', 'vendor', 'rootfs'):
    for path in sorted((source / partition).rglob('*')):
        if not path.is_file() or path.is_symlink():
            continue
        name = path.name.lower()
        font = name.startswith('roboto') and path.suffix.lower() in ('.ttf', '.otf')
        configuration = name in ('fonts.xml', 'system_fonts.xml', 'fallback_fonts.xml')
        branding = path.suffix.lower() in ('.png', '.jpg', '.jpeg', '.bmp') and (
            name in ('touch.png', '4k.png', '1080p.png', '720p.png', '480p.png') or 'logo' in name or 'vietk' in name)
        if not (font or configuration or branding):
            continue
        relative = path.relative_to(source)
        destination = output / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, destination)
        records.append({'path': relative.as_posix(), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
                        'bytes': path.stat().st_size, 'kind': 'font' if font else 'configuration' if configuration else 'branding-candidate'})
output.mkdir(parents=True, exist_ok=True)
(output / 'provenance.json').write_text(json.dumps(records, indent=2))
print(f'Retained {len(records)} original UI assets; candidates are not assumed to be active product branding')

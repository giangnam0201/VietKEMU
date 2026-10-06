"""Package decoded vendor code and protocol bytecode for a lightweight review."""
import shutil
from pathlib import Path

source = Path('artifacts/current-firmware')
destination = Path('artifacts/current-music-evidence')
bytecode_names = {
    'DCDomain', 'DCCloudMusicLibCommu', 'CloudMusicUnlockManager',
    'BaseDataCenterCommu', 'DataCenterConfigure', 'DCUnlockCloudLibraryCommu',
    'DCCheckCodeCommu', 'BoardInfo', 'HttpFile', 'DataCenterMessage',
}
for path in source.rglob('*'):
    if not path.is_file():
        continue
    relative = path.relative_to(source)
    keep = (len(relative.parts) == 1 or
            path.name in ('port-index.json', 'original-entries.json', 'AndroidManifest.xml') or
            (path.suffix == '.java' and 'com/evideo/' in relative.as_posix()) or
            (path.suffix == '.smali' and path.stem.split('$')[0] in bytecode_names) or
            (path.suffix == '.xml' and any(p.startswith('values') for p in relative.parts)))
    if keep:
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(path, target)

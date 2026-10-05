"""Read a supplied OTA and reconstruct its filesystems on a Linux CI runner."""
import argparse
import hashlib
import json
import re
import struct
import subprocess
import zipfile
from pathlib import Path


def reconstruct(archive, output):
    lines = archive.read('system.transfer.list').decode().splitlines()
    if int(lines[0]) not in (2, 3, 4):
        raise ValueError('Unsupported Android transfer list version')
    commands = [line.split() for line in lines[4:] if line.strip()]
    end = 0
    for command in commands:
        if command[0] not in ('new', 'zero', 'erase'):
            raise ValueError('Incremental OTAs need the original system image')
        ranges = [int(n) for n in command[1].split(',')]
        if ranges[0] != len(ranges) - 1 or ranges[0] % 2:
            raise ValueError('Invalid block ranges')
        end = max(end, *ranges[2::2])
    with output.open('wb') as image, archive.open('system.new.dat') as data:
        image.truncate(end * 4096)
        for command in commands:
            if command[0] != 'new':
                continue
            ranges = [int(n) for n in command[1].split(',')][1:]
            for start, stop in zip(ranges[::2], ranges[1::2]):
                image.seek(start * 4096)
                remaining = (stop - start) * 4096
                while remaining:
                    chunk = data.read(min(remaining, 1024 * 1024))
                    if not chunk:
                        raise ValueError('Truncated system.new.dat')
                    image.write(chunk)
                    remaining -= len(chunk)
        if data.read(1):
            raise ValueError('Unconsumed system.new.dat blocks')


def extract_ext4(image, destination):
    destination.mkdir(parents=True, exist_ok=True)
    subprocess.run(['debugfs', '-R', f'rdump / {destination}', str(image)], check=True)
    if not any(destination.iterdir()):
        raise RuntimeError(f'No files extracted from {image}')


def analyze(source, work):
    work.mkdir(parents=True, exist_ok=True)
    report = {'source': source.name, 'sha256': hashlib.file_digest(source.open('rb'), 'sha256').hexdigest()}
    with zipfile.ZipFile(source) as archive:
        report['entries'] = [{'path': e.filename, 'size': e.file_size} for e in archive.infolist()]
        report['metadata'] = archive.read('META-INF/com/android/metadata').decode()
        report['config'] = archive.read('config.txt').decode()
        tree = archive.read('android.emmc.dtb')
        report['device_tree_strings'] = [s.decode() for s in re.findall(rb'[ -~]{6,}', tree)]
        reconstruct(archive, work / 'system.img')
        with archive.open('vendor.img') as incoming, (work / 'vendor.img').open('wb') as outgoing:
            import shutil
            shutil.copyfileobj(incoming, outgoing)
        (work / 'device.dtb').write_bytes(tree)
    for partition in ('system', 'vendor'):
        image = work / f'{partition}.img'
        if image.read_bytes()[:4] == bytes.fromhex('3aff26ed'):
            raw = work / f'{partition}.raw.img'
            subprocess.run(['simg2img', str(image), str(raw)], check=True)
            image = raw
        extract_ext4(image, work / partition)
    props = list(work.glob('*/build.prop')) + list(work.glob('*/**/build.prop'))
    report['properties'] = {str(p.relative_to(work)): p.read_text(errors='replace') for p in set(props)}
    apks = []
    for apk in work.glob('*/**/*.apk'):
        result = subprocess.run(['aapt', 'dump', 'badging', str(apk)], capture_output=True, text=True)
        with zipfile.ZipFile(apk) as bundle:
            apks.append({'path': str(apk.relative_to(work)), 'size': apk.stat().st_size,
                         'badging': result.stdout, 'native_libraries': [n for n in bundle.namelist() if n.endswith('.so')],
                         'assets': [n for n in bundle.namelist() if n.startswith('assets/')]})
    report['apks'] = apks
    report['native_files'] = [str(p.relative_to(work)) for p in work.glob('*/**/*.so')]
    report['databases'] = [str(p.relative_to(work)) for p in work.glob('*/**/*') if p.is_file() and p.suffix in ('.db', '.sqlite', '.sqlite3')]
    (work / 'report.json').write_text(json.dumps(report, indent=2, ensure_ascii=False))
    inventory = '\n'.join(str(p.relative_to(work)) for part in ('system', 'vendor') for p in (work / part).rglob('*') if p.is_file())
    (work / 'inventory.txt').write_text(inventory)
    print(json.dumps({'apks': [{'path': a['path'], 'badging': a['badging'][:1500]} for a in apks], 'databases': report['databases']}, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('source', type=Path)
    parser.add_argument('--work', type=Path, default=Path('firmware'))
    args = parser.parse_args()
    analyze(args.source, args.work)

"""Inspect the official public KTV-Plus OTA on CI, without executing its apps."""
import hashlib
import json
import shutil
import subprocess
import zipfile
import sys
from pathlib import Path

from analyze_firmware import reconstruct
from decode_native_port import decode


def main():
    source = Path('incoming/KTV-Plus_ota_package.zip')
    output = Path('artifacts/current-firmware')
    output.mkdir(parents=True, exist_ok=True)
    with source.open('rb') as stream:
        checksum = hashlib.file_digest(stream, 'sha256').hexdigest()
    report = {
        'source_page': 'https://vietk.vn/cap-nhat-phan-mem/',
        'public_folder': '17Dq3g5VOi_zHa8p1lYwvXvMwGN5QPwn9',
        'public_file': '1-l9rE_5GLyvkgYMGya14mucQGPaLaE2A',
        'sha256': checksum,
        'bytes': source.stat().st_size,
    }
    work = Path('current-firmware')
    work.mkdir(exist_ok=True)
    with zipfile.ZipFile(source) as archive:
        report['entries'] = [{'path': e.filename, 'bytes': e.file_size}
                             for e in archive.infolist()]
        names = set(archive.namelist())
        for name in ('META-INF/com/android/metadata', 'config.txt'):
            if name in names:
                report[name] = archive.read(name).decode(errors='replace')
        (output / 'inventory.json').write_text(json.dumps(report, indent=2))
        partitions = []
        for partition in ('system', 'vendor'):
            image = work / f'{partition}.img'
            if f'{partition}.img' in names:
                with archive.open(f'{partition}.img') as incoming, image.open('wb') as outgoing:
                    shutil.copyfileobj(incoming, outgoing)
            elif partition == 'system' and {'system.transfer.list', 'system.new.dat'} <= names:
                reconstruct(archive, image)
            else:
                continue
            partitions.append(partition)
            with image.open('rb') as stream:
                sparse = stream.read(4) == bytes.fromhex('3aff26ed')
            if sparse:
                raw = work / f'{partition}.raw.img'
                subprocess.run(['simg2img', str(image), str(raw)], check=True)
                image = raw
            # debugfs emits ownership warnings when run without root. Keep its
            # diagnostics in the artifact rather than flooding the Actions log.
            destination = work / partition
            destination.mkdir(parents=True, exist_ok=True)
            with (output / f'{partition}-extraction.log').open('w') as log:
                subprocess.run(['debugfs', '-R', f'rdump / {destination}', str(image)],
                               stdout=log, stderr=subprocess.STDOUT, check=True)
            if not any(destination.iterdir()):
                raise RuntimeError(f'No files extracted from {image}')
    if not partitions:
        raise RuntimeError('Unknown OTA format; inspect inventory before adapting extraction')
    properties = {p.relative_to(work).as_posix(): p.read_text(errors='replace')
                  for partition in partitions for p in (work / partition).rglob('build.prop')}
    (output / 'properties.json').write_text(json.dumps(properties, indent=2))
    if '--properties-only' in sys.argv:
        return
    applications = []
    selected = []
    for partition in partitions:
        for apk in sorted((work / partition).rglob('*.apk')):
            result = subprocess.run(['aapt', 'dump', 'badging', str(apk)],
                                    capture_output=True, text=True)
            applications.append({'path': apk.relative_to(work).as_posix(),
                                 'bytes': apk.stat().st_size, 'badging': result.stdout,
                                 'badging_exit': result.returncode, 'badging_error': result.stderr})
            if apk.parent.name.lower() in ('dcservice', 'dualkmbox') or any(
                    term in result.stdout.lower() for term in ('kmdatacenter', 'dualkmbox')):
                selected.append(apk)
    (output / 'applications.json').write_text(json.dumps(applications, indent=2))
    if not selected:
        raise RuntimeError('Music applications not recognized; inspect application inventory')
    results = []
    for apk in selected:
        print('Decoding ' + str(apk), flush=True)
        results.append(decode(apk, output / 'decoded', Path('jadx/bin/jadx')))
        (output / 'decode-summary.json').write_text(json.dumps(results, indent=2))
    if any(result['apktool_exit'] != 0 for result in results):
        raise RuntimeError('Incomplete bytecode decode; inspect logs')


if __name__ == '__main__':
    main()

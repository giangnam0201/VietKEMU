"""Decode original APKs for a manual Windows port; never rewrite the inputs.

Smali is the fallback when JADX cannot reconstruct a Java method. Native ELF
files require a separate implementation; their symbols are evidence, not C code.
"""
import argparse
import hashlib
import json
import re
import subprocess
import zipfile
from pathlib import Path


def run(command, log, timeout=900):
    with log.open('w', encoding='utf-8') as stream:
        try:
            return subprocess.run(command, stdout=stream, stderr=subprocess.STDOUT,
                                  timeout=timeout).returncode
        except subprocess.TimeoutExpired:
            stream.write('\nDECODE TIMEOUT: output may be incomplete\n')
            return 124


def decode(apk, output, jadx):
    destination = output / apk.parent.name
    destination.mkdir(parents=True, exist_ok=True)
    apktool_exit = run(['apktool', 'd', '-f', '-o', str(destination / 'apktool'),
                       str(apk)], destination / 'apktool.log')
    jadx_exit = run([str(jadx), '--no-res', '--show-bad-code', '-j', '2',
                     '-d', str(destination / 'java'), str(apk)], destination / 'jadx.log')
    decoded = destination / 'apktool'
    java = destination / 'java/sources'
    resources = []
    for layout in sorted((decoded / 'res').glob('layout*/*.xml')):
        resources.append({
            'file': str(layout.relative_to(destination)),
            'ids': sorted(set(re.findall(r'@\+?id/([\w]+)', layout.read_text(errors='replace')))),
        })
    candidates, unresolved = [], []
    for source in sorted(java.rglob('*.java')):
        text = source.read_text(errors='replace')
        relative = str(source.relative_to(destination))
        if any(marker in text for marker in ('Method not decompiled:', 'JADX ERROR')):
            unresolved.append(relative)
        if any(marker in text for marker in ('setContentView(', 'inflate(', 'addJavascriptInterface(',
                                             'extends Activity', 'extends Fragment', 'extends Service')):
            candidates.append(relative)
    # Record every original entry so copied resources can be traced to the APK.
    entries, dex, embedded = [], [], []
    with zipfile.ZipFile(apk) as archive:
        for entry in archive.infolist():
            if entry.is_dir():
                continue
            payload = archive.read(entry)
            entries.append({'path': entry.filename, 'bytes': entry.file_size,
                            'sha256': hashlib.sha256(payload).hexdigest()})
            if re.fullmatch(r'classes\d*\.dex', entry.filename):
                dex.append(entry.filename)
            if payload.startswith(b'PK\x03\x04') and entry.filename.startswith('assets/'):
                embedded.append(entry.filename)
    native = []
    for library in sorted(decoded.rglob('*.so')):
        log = library.relative_to(decoded).as_posix().replace('/', '__') + '.txt'
        result = run(['readelf', '-h', '-d', '-Ws', str(library)], destination / log, timeout=60)
        native.append({'file': str(library.relative_to(destination)), 'inspection_exit': result,
                       'windows_implementation': 'pending'})
    manifest = decoded / 'AndroidManifest.xml'
    package = None
    if manifest.exists():
        match = re.search(r'\bpackage="([^"]+)"', manifest.read_text(errors='replace'))
        package = match[1] if match else None
    result = {
        'original_apk': apk.as_posix(), 'original_sha256': hashlib.sha256(apk.read_bytes()).hexdigest(),
        'package': package, 'apktool_exit': apktool_exit, 'jadx_exit': jadx_exit,
        'original_dex_files': dex,
        'smali_files': sum(1 for _ in decoded.rglob('*.smali')),
        'java_files': sum(1 for _ in java.rglob('*.java')),
        'java_files_with_unresolved_methods': unresolved,
        'screen_and_service_sources': candidates, 'layouts': resources,
        'embedded_archives': embedded, 'native_libraries': native,
        'windows_port_status': 'not implemented',
        'decode_complete': apktool_exit == 0 and jadx_exit == 0 and not unresolved,
    }
    (destination / 'original-entries.json').write_text(json.dumps(entries, indent=2))
    (destination / 'port-index.json').write_text(json.dumps(result, indent=2))
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--firmware', type=Path, default=Path('firmware'))
    parser.add_argument('--output', type=Path, default=Path('artifacts/native-decode'))
    parser.add_argument('--jadx', type=Path, default=Path('jadx/bin/jadx'))
    args = parser.parse_args()
    apks = sorted((args.firmware / 'vendor/app').rglob('*.apk'))
    if not apks:
        raise RuntimeError('Original vendor APKs are missing')
    priority = ['dualkmbox', 'daulkmboxosdtv', 'hdplayer', 'dcservice', 'cbb']
    apks.sort(key=lambda apk: (priority.index(apk.parent.name) if apk.parent.name in priority else 99,
                              apk.as_posix()))
    args.output.mkdir(parents=True, exist_ok=True)
    results = []
    for apk in apks:
        print('Decoding original app: ' + str(apk), flush=True)
        results.append(decode(apk, args.output, args.jadx))
        (args.output / 'decode-summary.json').write_text(json.dumps(results, indent=2))
    if any(item['apktool_exit'] != 0 for item in results):
        raise RuntimeError('Resource/smali decode incomplete; inspect per-APK logs')


if __name__ == '__main__':
    main()

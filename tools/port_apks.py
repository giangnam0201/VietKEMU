"""Build private compatibility copies; preserve original UI resources and DEX code.

Only the vendor-library manifest dependency, bundled VDK/native adapter and test
signing change. Originals remain untouched. This is a port, not a fidelity claim.
"""
import argparse
import base64
import hashlib
import json
import re
import struct
import subprocess
import zipfile
from pathlib import Path


def string_pool(chunk):
    count, _, flags, start, _ = struct.unpack_from('<5I', chunk, 8)
    header_size = struct.unpack_from('<H', chunk, 2)[0]
    strings = []
    def length8(offset):
        first = chunk[offset]
        return (((first & 127) << 8) | chunk[offset + 1], offset + 2) if first & 128 else (first, offset + 1)
    def length16(offset):
        first = struct.unpack_from('<H', chunk, offset)[0]
        return (((first & 32767) << 16) | struct.unpack_from('<H', chunk, offset + 2)[0], offset + 4) if first & 32768 else (first, offset + 2)
    for i in range(count):
        offset = start + struct.unpack_from('<I', chunk, header_size + i * 4)[0]
        if flags & 256:
            _, offset = length8(offset)
            size, offset = length8(offset)
            strings.append(chunk[offset:offset + size].decode('utf-8'))
        else:
            size, offset = length16(offset)
            strings.append(chunk[offset:offset + size * 2].decode('utf-16le'))
    return strings


def patch_manifest(data, remove_system_uid=False):
    kind, header_size, total = struct.unpack_from('<HHI', data)
    if kind != 3 or total != len(data):
        raise ValueError('Expected a complete binary Android XML document')
    strings = []
    chunks = []
    offset = header_size
    skip_depth = 0
    removed = []
    while offset < len(data):
        kind, size_header, size = struct.unpack_from('<HHI', data, offset)
        if size < size_header or offset + size > len(data):
            raise ValueError('Invalid Android XML chunk')
        chunk = data[offset:offset + size]
        offset += size
        if kind == 1:
            strings = string_pool(chunk)
        if skip_depth:
            if kind == 0x102:
                skip_depth += 1
            elif kind == 0x103:
                skip_depth -= 1
            continue
        if kind == 0x102:
            name = strings[struct.unpack_from('<I', chunk, 20)[0]]
            attr_start, attr_size, count = struct.unpack_from('<3H', chunk, 24)
            begin = 16 + attr_start
            attributes = []
            for i in range(count):
                record = chunk[begin + i * attr_size:begin + (i + 1) * attr_size]
                key = strings[struct.unpack_from('<I', record, 4)[0]]
                value_type, value = struct.unpack_from('<BI', record, 15)
                value = strings[value] if value_type == 3 else value
                attributes.append((key, value, record))
            if name == 'uses-library' and any(k == 'name' and v == 'com.evideostb.vdk' for k, v, _ in attributes):
                skip_depth = 1
                removed.append('uses-library:com.evideostb.vdk (bundled instead)')
                continue
            if name == 'manifest' and remove_system_uid:
                kept = [(i + 1, record) for i, (key, _, record) in enumerate(attributes) if key != 'sharedUserId']
                if len(kept) != count:
                    mutable = bytearray(chunk[:begin])
                    struct.pack_into('<H', mutable, 28, len(kept))
                    indices = {old: new + 1 for new, (old, _) in enumerate(kept)}
                    for field in (30, 32, 34):
                        old = struct.unpack_from('<H', mutable, field)[0]
                        struct.pack_into('<H', mutable, field, indices.get(old, 0))
                    mutable.extend(b''.join(record for _, record in kept))
                    mutable.extend(chunk[begin + count * attr_size:])
                    struct.pack_into('<I', mutable, 4, len(mutable))
                    chunk = bytes(mutable)
                    removed.append('sharedUserId (isolated ordinary-app experiment)')
        chunks.append(chunk)
    if skip_depth:
        raise ValueError('Unclosed removed library element')
    output = bytearray(data[:header_size] + b''.join(chunks))
    struct.pack_into('<I', output, 4, len(output))
    return bytes(output), removed


def run(*args):
    subprocess.run([str(arg) for arg in args], check=True)


def port(root, vdk, native, output, key, certificate, signer, aligner, ordinary=False, serial=None):
    output.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(vdk) as framework:
        framework_dex = [(n, framework.read(n)) for n in framework.namelist() if re.fullmatch(r'classes\d*\.dex', n)]
    if not framework_dex:
        raise ValueError('Original VDK has no DEX code')
    report = []
    for apk in sorted((root / 'vendor/app').glob('*/*.apk')):
        if apk.parent.name not in ('dualkmbox', 'cbb', 'dcservice', 'daulkmboxosdtv', 'hdplayer', 'evsdkserver', 'KmBoxPermission', 'VietkService', 'KmDataCenterService', 'KmHttpdService', 'KmAudioRecordService'):
            continue
        unsigned = output / f'{apk.stem}.unsigned.apk'
        changed = []
        retained = {}
        with zipfile.ZipFile(apk) as original, zipfile.ZipFile(unsigned, 'w', zipfile.ZIP_DEFLATED) as patched:
            dex_numbers = [int(m[1] or 1) for n in original.namelist() if (m := re.fullmatch(r'classes(\d*)\.dex', n))]
            next_dex = max(dex_numbers, default=0) + 1
            for entry in original.infolist():
                if entry.filename.upper().startswith('META-INF/'):
                    continue
                data = original.read(entry.filename)
                if entry.filename == 'AndroidManifest.xml':
                    data, changed = patch_manifest(data, ordinary)
                elif re.fullmatch(r'classes\d*\.dex', entry.filename) or entry.filename == 'resources.arsc':
                    retained[entry.filename] = hashlib.sha256(data).hexdigest()
                patched.writestr(entry, data)
            if any('uses-library:' in change for change in changed):
                for name, data in framework_dex:
                    patched.writestr(f'classes{next_dex}.dex', data)
                    next_dex += 1
                patched.write(native, 'lib/armeabi-v7a/libvdk.so')
            if serial and apk.parent.name == 'dualkmbox':
                name = 'lib/armeabi-v7a/libfactoryuart.so'
                if name in original.namelist():
                    raise ValueError('Refusing to overwrite an original UART library')
                patched.write(serial, name)
        aligned = output / f'{apk.stem}.aligned.apk'
        signed = output / f'{apk.stem}.apk'
        run(aligner, '-f', '-p', '4', unsigned, aligned)
        run(signer, 'sign', '--key', key, '--cert', certificate, '--out', signed, aligned)
        run(signer, 'verify', '--verbose', '--print-certs', signed)
        with zipfile.ZipFile(signed) as verified:
            for name, digest in retained.items():
                if hashlib.sha256(verified.read(name)).hexdigest() != digest:
                    raise RuntimeError(f'Original UI/DEX changed unexpectedly: {name}')
        report.append({'original': str(apk.relative_to(root)), 'ported': signed.name,
                       'manifest_changes': changed, 'original_payload_hashes': retained,
                       'status': 'built; runtime operation unverified'})
        unsigned.unlink()
        aligned.unlink()
    (output / 'port-manifest.json').write_text(json.dumps(report, indent=2), encoding='utf-8')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    for field in ('root', 'vdk', 'native', 'output', 'key', 'certificate', 'signer', 'aligner'):
        parser.add_argument('--' + field, type=Path, required=True)
    parser.add_argument('--ordinary', action='store_true')
    parser.add_argument('--serial', type=Path)
    port(**vars(parser.parse_args()))

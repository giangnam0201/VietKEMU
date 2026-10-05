"""Prepare an AOSP development guest to run the original ARM application stack.

Runs only on the disposable Linux build runner. Copies SDK native-bridge files,
preserving their xattrs, and configures ARM translation. No karaoke code or UI is
modified. An SDK source image and destination image must be explicitly supplied.
"""
import argparse
import json
import os
import shutil
import struct
import subprocess
from pathlib import Path


def run(*args):
    subprocess.run([str(a) for a in args], check=True)


def raw_image(path):
    subprocess.run(['file', str(path)], check=False)
    subprocess.run(['fdisk', '-l', str(path)], check=False)
    with path.open('rb') as source:
        sparse = source.read(4) == struct.pack('<I', 0xed26ff3a)
    if sparse:
        raw = path.with_suffix('.raw')
        run('simg2img', path, raw)
        os.replace(raw, path)


def copy_bridge(image, work):
    raw_image(image)
    layout = subprocess.run(['sfdisk', '--json', str(image)], capture_output=True, text=True)
    if layout.returncode == 0:
        table = json.loads(layout.stdout)['partitiontable']
        (work / 'sdk-disk-layout.json').write_text(json.dumps(table, indent=2))
        partition = max(table['partitions'], key=lambda p: p['size'])
        sector = table.get('sectorsize', 512)
        loop = subprocess.run(['losetup','--find','--show','--read-only','--offset',str(partition['start'] * sector),
                               '--sizelimit',str(partition['size'] * sector),str(image)],check=True,capture_output=True,text=True).stdout.strip()
        try:
            run('python3', work / 'lpunpack.py', '-p', 'system,system_a', loop, work / 'logical')
        finally:
            run('losetup', '-d', loop)
        choices = list((work / 'logical').glob('system*.img'))
        if len(choices) != 1:
            raise RuntimeError(f'Expected one SDK system logical partition, found {choices}')
        image = choices[0]
    mount = (work / 'source-mount').resolve()
    mount.mkdir(parents=True, exist_ok=True)
    run('mount', '-o', 'loop,ro', image, mount)
    found = []
    try:
        # SDK system images may mount with or without a system/ prefix.
        root = mount / 'system' if (mount / 'system/lib').exists() else mount
        for p in root.rglob('*'):
            relative = p.relative_to(root)
            name = relative.as_posix()
            bridge_file = (
                name.startswith(('bin/arm/', 'bin/arm64/', 'lib/arm/', 'lib64/arm64/', 'etc/binfmt_misc/'))
                or name.startswith('bin/ndk_translation')
                or name in ('etc/ld.config.arm.txt', 'etc/ld.config.arm64.txt')
                or (name.startswith('etc/init/ndk_translation') and name.endswith('.rc'))
                or (relative.parts[0] in ('lib', 'lib64') and p.name.startswith('libndk') and p.suffix == '.so')
            )
            if bridge_file:
                if p.is_file():
                    target = work / 'bridge' / relative
                    target.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copy2(p, target, follow_symlinks=False)
                    found.append(str(relative))
        if not any(Path(name).name == 'libndk_translation.so' for name in found):
            raise RuntimeError('SDK source has no native bridge; runtime cannot be prepared')
        if 'etc/ld.config.arm.txt' not in found:
            raise RuntimeError('SDK ARM linker namespace configuration is missing; ARM processes cannot start')
    finally:
        run('umount', mount)
    (work / 'bridge-files.json').write_text(json.dumps(found, indent=2))
    if image.parent == work / 'logical':
        image.unlink()


def provision(image, work):
    raw_image(image)
    # Allocate room for the added ARM runtime before mounting the SDK ext4 image.
    with image.open('r+b') as target:
        target.truncate(image.stat().st_size + 512 * 1024 * 1024)
    result = subprocess.run(['e2fsck', '-f', '-p', str(image)])
    if result.returncode not in (0, 1):
        raise RuntimeError('Guest filesystem check failed')
    run('resize2fs', image)
    mount = (work / 'destination-mount').resolve()
    mount.mkdir(parents=True, exist_ok=True)
    run('mount', '-o', 'loop,rw', image, mount)
    try:
        root = mount / 'system' if (mount / 'system/lib').exists() else mount
        shutil.copytree(work / 'bridge', root, dirs_exist_ok=True, copy_function=shutil.copy2, symlinks=True)
        properties = {
            'ro.dalvik.vm.native.bridge': 'libndk_translation.so',
            'ro.dalvik.vm.isa.arm': 'x86', 'ro.dalvik.vm.isa.arm64': 'x86_64',
            'ro.enable.native.bridge.exec': '1',
            'ro.product.cpu.abilist': 'x86_64,x86,arm64-v8a,armeabi-v7a,armeabi',
            'ro.product.cpu.abilist32': 'x86,armeabi-v7a,armeabi',
            'ro.product.cpu.abilist64': 'x86_64,arm64-v8a',
        }
        propfile = root / 'build.prop'
        lines = propfile.read_text().splitlines()
        lines = [line for line in lines if line.split('=', 1)[0] not in properties]
        propfile.write_text('\n'.join(lines) + '\n' + '\n'.join(f'{k}={v}' for k,v in properties.items()) + '\n')
        run('sync')
    finally:
        run('umount', mount)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('mode', choices=['extract', 'provision'])
    parser.add_argument('--image', type=Path, required=True)
    parser.add_argument('--work', type=Path, required=True)
    args = parser.parse_args()
    args.work.mkdir(parents=True, exist_ok=True)
    (copy_bridge if args.mode == 'extract' else provision)(args.image.resolve(), args.work.resolve())

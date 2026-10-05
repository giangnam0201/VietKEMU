"""Provision ARM translation in a disposable AOSP development guest.

The writable guest owns these platform settings; original karaoke app code,
resources, device activation and server authentication remain untouched.
"""
import argparse
import json
import re
import subprocess
import time
import xml.etree.ElementTree as ET
from pathlib import Path

OUTPUT = Path('artifacts/port-runtime')


def adb(*args, timeout=60, required=True):
    command = ['adb', *map(str,args)]
    print('Provision: ' + ' '.join(command), flush=True)
    try:
        result = subprocess.run(command,capture_output=True,text=True,timeout=timeout)
    except subprocess.TimeoutExpired as error:
        stdout = error.stdout or ''
        if isinstance(stdout, bytes):
            stdout = stdout.decode('utf-8', errors='replace')
        result = subprocess.CompletedProcess(command, 124, stdout, f'ADB timed out after {timeout}s')
    print(result.stdout + result.stderr, flush=True)
    OUTPUT.mkdir(parents=True, exist_ok=True)
    with (OUTPUT / 'provision.log').open('a', encoding='utf-8') as log:
        log.write(' '.join(command) + '\n' + result.stdout + result.stderr + '\n')
    if required and result.returncode:
        raise RuntimeError('Guest provisioning command failed: ' + ' '.join(map(str,args)))
    return result


def boot():
    limit = time.monotonic() + 300
    time.sleep(3)
    while time.monotonic() < limit:
        ready = adb('shell','getprop','sys.boot_completed',required=False,timeout=15)
        if ready.returncode == 0 and ready.stdout.strip() == '1':
            service = adb('shell','pm','path','android',required=False)
            if 'package:' in service.stdout:
                return
        if 'device offline' in ready.stderr:
            # Emulator reboot can leave the old ADB transport offline even when
            # the guest has restarted. Reconnect it rather than polling forever.
            adb('reconnect', 'offline', required=False, timeout=15)
        time.sleep(3)
    raise RuntimeError('Adapted guest did not finish booting')


def original_ui_warning_defaults():
    # Android 6 did not show Android 11's deprecated-target-SDK dialogs. Use
    # AppWarnings' actual persisted flag, leaving original APK target SDK intact.
    # Format/flag: AOSP android-11.0.0_r48 AppWarnings.java.
    existing = adb('shell', 'cat', '/data/system/packages-warnings.xml', required=False)
    tree = ET.fromstring(existing.stdout) if existing.returncode == 0 else ET.Element('packages')
    by_name = {entry.get('name'): entry for entry in tree.findall('package')}
    originals = json.loads(Path('firmware/report.json').read_text(encoding='utf-8'))
    built = {entry['original'] for entry in json.loads(Path('artifacts/port/apks/port-manifest.json').read_text())}
    for app in originals['apks']:
        if app['path'] not in built:
            continue
        match = re.search(r"^package: name='([A-Za-z0-9_.]+)'", app['badging'], re.M)
        if not match:
            raise RuntimeError('Cannot identify original app for guest UI settings')
        entry = by_name.get(match[1])
        if entry is None:
            entry = ET.SubElement(tree, 'package', name=match[1])
        entry.set('flags', str(int(entry.get('flags', '0')) | 4))
    local = OUTPUT / 'original-ui-warnings.xml'
    ET.ElementTree(tree).write(local, encoding='utf-8', xml_declaration=True)
    adb('push', local, '/data/system/packages-warnings.xml')
    adb('shell', 'chown', '1000:1000', '/data/system/packages-warnings.xml')
    adb('shell', 'chmod', '600', '/data/system/packages-warnings.xml')
    adb('shell', 'restorecon', '/data/system/packages-warnings.xml')


def provision(source, permissive_development=False):
    adb('root'); adb('wait-for-device')
    # This is the disposable AOSP guest, not the supplied VietK firmware.
    # disable-verity only changes hashtree flags; after a warm emulator reboot,
    # init rejects the changed vbmeta against the original kernel digest.
    # The development guest must also disable verification before that reboot.
    adb('shell', 'avbctl', 'disable-verification')
    verity = adb('disable-verity')
    if 'reboot' in verity.stdout.lower():
        adb('reboot'); boot(); adb('root'); adb('wait-for-device')
    adb('remount')
    for category in source.iterdir():
        if not category.is_dir():
            continue
        for entry in category.iterdir():
            adb('push',entry,f'/system/{category.name}/',timeout=180)
            adb('shell','restorecon','-RF',f'/system/{category.name}/{entry.name}')
    properties = {
        'ro.dalvik.vm.native.bridge':'libndk_translation.so',
        'ro.dalvik.vm.isa.arm':'x86', 'ro.dalvik.vm.isa.arm64':'x86_64',
        'ro.enable.native.bridge.exec':'1',
        'ro.product.cpu.abilist':'x86_64,x86,arm64-v8a,armeabi-v7a,armeabi',
        'ro.product.cpu.abilist32':'x86,armeabi-v7a,armeabi',
        'ro.product.cpu.abilist64':'x86_64,arm64-v8a',
        # Original system/build.prop defaults affecting the application UI.
        'ro.product.locale': 'vi-VN',
    }
    lines = adb('shell','cat','/system/build.prop').stdout.splitlines()
    lines = [line for line in lines if line.split('=',1)[0] not in properties]
    propfile = source.parent / 'runtime.build.prop'
    propfile.write_text('\n'.join(lines) + '\n' + '\n'.join(f'{k}={v}' for k,v in properties.items()) + '\n')
    adb('push',propfile,'/system/build.prop')
    adb('shell','chmod','644','/system/build.prop')
    adb('shell','restorecon','/system/build.prop')
    adb('shell','setprop','persist.sys.locale','vi-VN')
    adb('shell','setprop','persist.sys.timezone','Asia/Taipei')
    original_ui_warning_defaults()
    adb('reboot'); boot(); adb('root'); adb('wait-for-device')
    if permissive_development:
        # Development only: observe the original system app's vendor operations
        # before implementing the corresponding guest platform policy/backend.
        # This does not modify the original application or licensing checks.
        adb('shell', 'setenforce', '0')
        if adb('shell', 'getenforce').stdout.strip() != 'Permissive':
            raise RuntimeError('Development guest could not enter the requested permissive mode')
    loaded = adb('shell','getprop','ro.dalvik.vm.native.bridge').stdout.strip()
    if loaded != 'libndk_translation.so':
        raise RuntimeError('Guest did not load the configured ARM bridge property')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('source',type=Path)
    parser.add_argument('--permissive-development', action='store_true')
    args = parser.parse_args()
    try:
        provision(args.source, args.permissive_development)
    finally:
        # Keep evidence even when provisioning fails before application probing.
        for name, command in (
            ('provision-logcat.txt', ('logcat','-d','-b','all')),
            ('provision-properties.txt', ('shell','getprop')),
            ('provision-devices.txt', ('devices','-l')),
        ):
            result = adb(*command, timeout=15, required=False)
            (OUTPUT / name).write_text(result.stdout + result.stderr, encoding='utf-8')

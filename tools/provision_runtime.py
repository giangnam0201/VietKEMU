"""Provision ARM translation in a disposable AOSP development guest.

The writable guest owns these platform settings; original karaoke app code,
resources, device activation and server authentication remain untouched.
"""
import argparse
import subprocess
import time
from pathlib import Path


def adb(*args, timeout=60, required=True):
    result = subprocess.run(['adb', *map(str,args)],capture_output=True,text=True,timeout=timeout)
    print(result.stdout + result.stderr, flush=True)
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
        time.sleep(3)
    raise RuntimeError('Adapted guest did not finish booting')


def provision(source):
    adb('root'); adb('wait-for-device')
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
    }
    lines = adb('shell','cat','/system/build.prop').stdout.splitlines()
    lines = [line for line in lines if line.split('=',1)[0] not in properties]
    propfile = source.parent / 'runtime.build.prop'
    propfile.write_text('\n'.join(lines) + '\n' + '\n'.join(f'{k}={v}' for k,v in properties.items()) + '\n')
    adb('push',propfile,'/system/build.prop')
    adb('shell','chmod','644','/system/build.prop')
    adb('shell','restorecon','/system/build.prop')
    adb('reboot'); boot(); adb('root'); adb('wait-for-device')
    loaded = adb('shell','getprop','ro.dalvik.vm.native.bridge').stdout.strip()
    if loaded != 'libndk_translation.so':
        raise RuntimeError('Guest did not load the configured ARM bridge property')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('source',type=Path)
    provision(parser.parse_args().source)

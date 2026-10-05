"""Install the original VDK into an isolated writable Android research guest.

No application manifests, signatures or original firmware bytes are rewritten.
This tests userspace portability; it does not emulate the Realtek hardware.
"""
import argparse
import subprocess
import time
from pathlib import Path


def command(*args):
    result = subprocess.run(['adb', *args], capture_output=True, text=True,
                            encoding='utf-8', errors='replace', timeout=120)
    print(result.stdout + result.stderr, flush=True)
    if result.returncode:
        raise RuntimeError(f'ADB operation failed: {args}')
    return result.stdout + result.stderr


def boot():
    command('wait-for-device')
    for attempt in range(90):
        if command('shell', 'getprop', 'sys.boot_completed').strip() == '1':
            return
        time.sleep(2)
    raise RuntimeError('Guest did not complete reboot')


def provision(source):
    jar = next(source.rglob('vdk.jar'))
    library = next(source.rglob('libvdk.so'))
    permissions = next(source.rglob('com.evideostb.vdk.xml'))
    command('root')
    command('wait-for-device')
    command('disable-verity')
    command('reboot')
    boot()
    command('root')
    command('wait-for-device')
    command('remount')
    command('push', str(jar), '/system/framework/vdk.jar')
    command('push', str(permissions), '/system/etc/permissions/com.evideostb.vdk.xml')
    command('push', str(library), '/system/lib/libvdk.so')
    command('shell', 'chmod', '644', '/system/framework/vdk.jar',
            '/system/etc/permissions/com.evideostb.vdk.xml', '/system/lib/libvdk.so')
    command('shell', 'restorecon', '/system/framework/vdk.jar',
            '/system/etc/permissions/com.evideostb.vdk.xml', '/system/lib/libvdk.so')
    command('reboot')
    boot()
    libraries = command('shell', 'pm', 'list', 'libraries')
    if 'com.evideostb.vdk' not in libraries:
        raise RuntimeError('Original vendor shared library was not registered')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('source', type=Path)
    provision(parser.parse_args().source)

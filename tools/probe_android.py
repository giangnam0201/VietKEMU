"""Attempt original firmware applications on a stock Android guest, recording evidence.

This is a compatibility experiment, not a claim of successful firmware emulation.
"""
import argparse
import json
import re
import subprocess
import time
from pathlib import Path


def adb(*args, binary=False, timeout=30):
    command = ['adb', *args]
    try:
        return subprocess.run(command, capture_output=True, text=not binary, timeout=timeout)
    except subprocess.TimeoutExpired as error:
        stdout = error.stdout or b''
        if not binary and isinstance(stdout, bytes):
            stdout = stdout.decode('utf-8', errors='replace')
        message = f'ADB command timed out after {timeout}s'
        return subprocess.CompletedProcess(command, 124, stdout, message.encode() if binary else message)


def probe(root, report_path, output):
    output.mkdir(parents=True, exist_ok=True)
    report = json.loads(report_path.read_text(encoding='utf-8'))
    packages = []
    for app in report['apks']:
        badging = app['badging']
        package = re.search(r"^package: name='([^']+)'", badging, re.M)
        activity = re.search(r"^launchable-activity: name='([^']+)'", badging, re.M)
        if package and any(s in package[1].lower() for s in ('evideo', 'ktv', 'vietk', 'duochang', 'kmbox')):
            packages.append((app, package[1], activity[1] if activity else None))
    guest = adb('shell', 'getprop').stdout
    (output / 'guest-properties.txt').write_text(guest)
    (output / 'displays.txt').write_text(adb('shell', 'dumpsys', 'display').stdout)
    # Establish stock install compatibility before changing the guest display.
    # A changing display configuration can restart system_server during boot.
    packages.sort(key=lambda item: (item[1] != 'com.evideo.kmbox', item[1]))
    results = []
    def save():
        (output / 'compatibility.json').write_text(json.dumps(results, indent=2), encoding='utf-8')
    def package_service_ready():
        for attempt in range(12):
            response = adb('shell', 'pm', 'path', 'android')
            if response.returncode == 0 and 'package:' in response.stdout:
                return True
            time.sleep(2)
        return False
    for app, package, activity in packages:
        if not package_service_ready():
            (output / 'guest-failure.log').write_text(adb('logcat', '-d').stdout, encoding='utf-8')
            save()
            raise RuntimeError('Guest package service unavailable; no app compatibility conclusion is possible')
        installation = adb('install', '-r', '-g', str(root / app['path']), timeout=90)
        if any(error in installation.stdout + installation.stderr for error in ('Broken pipe', "Can't find service", 'device offline')):
            (output / 'guest-failure.log').write_text(adb('logcat', '-d').stdout, encoding='utf-8')
            if package_service_ready():
                installation = adb('install', '-r', '-g', str(root / app['path']), timeout=90)
        results.append({'package': package, 'activity': activity, 'path': app['path'],
                        'install': installation.stdout + installation.stderr,
                        'install_exit': installation.returncode})
        save()
        print(package, installation.stdout + installation.stderr, flush=True)
    (output / 'guest-install.log').write_text(adb('logcat', '-d').stdout, encoding='utf-8')
    for result in results:
        if not result['activity'] or result['install_exit']:
            continue
        package = result['package']
        adb('logcat', '-c')
        launch = adb('shell', 'am', 'start', '-W', '-n', package + '/' + result['activity'])
        result['launch'] = launch.stdout + launch.stderr
        result['launch_exit'] = launch.returncode
        save()
        time.sleep(12)
        result['pid'] = adb('shell', 'pidof', package).stdout.strip()
        log = adb('logcat', '-d', '-v', 'threadtime').stdout
        (output / f'{package}.log').write_text(log)
        activities = adb('shell', 'dumpsys', 'activity', 'activities').stdout
        (output / f'{package}.activities.txt').write_text(activities)
        shot = adb('exec-out', 'screencap', '-p', binary=True)
        if shot.returncode == 0 and shot.stdout.startswith(b'\x89PNG'):
            (output / f'{package}.png').write_bytes(shot.stdout)
        result['errors'] = [line for line in log.splitlines() if any(term in line for term in
                            ('FATAL EXCEPTION', 'UnsatisfiedLinkError', 'NoClassDefFoundError',
                             'ClassNotFoundException', 'SecurityException', 'Fatal signal',
                             'dlopen failed', 'Unable to start', 'not found'))][-100:]
        adb('shell', 'am', 'force-stop', package)
        save()
    save()
    print(json.dumps(results, indent=2))
    if not packages:
        raise RuntimeError('No karaoke packages identified; inspect firmware-report manually')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--root', type=Path, default=Path('firmware'))
    parser.add_argument('--report', type=Path, default=Path('firmware/report.json'))
    parser.add_argument('--output', type=Path, default=Path('artifacts/android-probe'))
    args = parser.parse_args()
    probe(args.root, args.report, args.output)

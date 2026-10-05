"""Install the compatibility copies and observe their real startup behavior."""
import json
import os
import re
import subprocess
import time
from pathlib import Path
from probe_android import adb, probe

root = Path('firmware')
report = json.loads((root / 'report.json').read_text(encoding='utf-8'))
ported = json.loads(Path('artifacts/port/apks/port-manifest.json').read_text())
mapping = {p['original']: p['ported'] for p in ported}
report['apks'] = [dict(app, path=mapping[app['path']]) for app in report['apks'] if app['path'] in mapping]
output = Path('artifacts/port-runtime')
output.mkdir(parents=True, exist_ok=True)
framework = output / 'guest-framework.apk'
pulled = adb('pull', '/system/framework/framework-res.apk', str(framework), timeout=60)
if pulled.returncode == 0:
    signer = Path(os.environ['ANDROID_HOME']) / 'build-tools/35.0.0/apksigner'
    certificate = subprocess.run([str(signer), 'verify', '--print-certs', str(framework)],
                                 capture_output=True, text=True)
    (output / 'guest-certificate.txt').write_text(certificate.stdout + certificate.stderr)
    digest = re.search(r'certificate SHA-256 digest: ([0-9a-f]+)', certificate.stdout)
    matches = []
    for cert in Path('keys').glob('*.x509.pem'):
        pem = subprocess.run(['openssl','x509','-in',str(cert),'-outform','DER'],capture_output=True,check=True)
        import hashlib
        if digest and hashlib.sha256(pem.stdout).hexdigest() == digest[1]:
            matches.append(cert.name)
    (output / 'guest-key-matches.json').write_text(json.dumps(matches))
    framework.unlink()
    if not matches:
        raise RuntimeError('Guest certificate has no matching public development key; original system identity cannot be retained')
    selected = matches[0].removesuffix('.x509.pem')
    for apk in Path('artifacts/port/apks').glob('*.apk'):
        signed = apk.with_suffix('.resigned')
        subprocess.run([str(signer),'sign','--key',f'keys/{selected}.pk8','--cert',f'keys/{selected}.x509.pem','--out',str(signed),str(apk)],check=True)
        os.replace(signed,apk)
        subprocess.run([str(signer),'verify',str(apk)],check=True)
else:
    raise RuntimeError('Cannot identify the guest platform certificate')
(output / 'report.json').write_text(json.dumps(report), encoding='utf-8')
adb('shell', 'settings', 'put', 'global', 'hidden_api_policy', '1')
adb('shell', 'settings', 'put', 'global', 'hidden_api_policy_pre_p_apps', '1')
adb('shell', 'settings', 'put', 'global', 'hidden_api_policy_p_apps', '1')
adb('shell', 'settings', 'put', 'system', 'screen_off_timeout', '1800000')
# Install original interfaces and input methods, then let MicroServiceActivity
# orchestrate its own services. Launching calibration/lock/settings activities
# as independent tests can change state before the original startup sequence.
excluded = ('com.android.packageinstaller',)
(output / 'system-integration-pending.json').write_text(json.dumps({
    'com.android.packageinstaller': 'Original OS installer is built but cannot replace the guest OS package as a normal app update'
}, indent=2))
probe(Path('artifacts/port/apks'), output / 'report.json', output,
      include_all=True, launch_packages=set(), excluded_packages=excluded)
methods = adb('shell', 'ime', 'list', '-s').stdout.splitlines()
original_keyboard = [line.strip() for line in methods if line.startswith('bkav.android.inputmethod.gtv/')]
(output / 'input-methods.txt').write_text('\n'.join(methods), encoding='utf-8')
if original_keyboard:
    adb('shell', 'ime', 'enable', original_keyboard[0])
    adb('shell', 'ime', 'set', original_keyboard[0])
adb('logcat', '-c')
adb('shell', 'am', 'start', '-W', '-n', 'com.evideo.kmbox/com.evideo.kmbox.activity.MicroServiceActivity')
time.sleep(30)
(output / 'main-startup.log').write_text(adb('logcat', '-d', '-v', 'threadtime').stdout, encoding='utf-8')
(output / 'main-activities.txt').write_text(adb('shell', 'dumpsys', 'activity', 'activities').stdout, encoding='utf-8')
shot = adb('exec-out', 'screencap', '-p', binary=True)
if shot.stdout.startswith(b'\x89PNG'):
    (output / 'main-panel.png').write_bytes(shot.stdout)

# A completed experiment must not publish install failures as adapted apps.
results = json.loads((output / 'compatibility.json').read_text())
failed = [item['package'] for item in results if item['install_exit'] != 0]
main_pid = adb('shell', 'pidof', 'com.evideo.kmbox').stdout.strip()
tv_services = adb('shell', 'dumpsys', 'activity', 'services', 'com.evideo.daulkmbox_osdtv').stdout
(output / 'tv-services.txt').write_text(tv_services, encoding='utf-8')
status = {
    'installation_failures': failed,
    'main_process_alive': bool(main_pid),
    'main_pid': main_pid,
    'original_tv_service_observed': 'com.evideo.kmboxosdtv.OsdTvShowService' in tv_services,
    'ui_and_feature_fidelity': 'unverified',
    'playback_and_server_downloads': 'unverified',
}
(output / 'runtime-status.json').write_text(json.dumps(status, indent=2))
if failed or not main_pid:
    raise RuntimeError('Original stack startup incomplete; inspect runtime-status.json and saved application logs')

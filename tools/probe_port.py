"""Install the compatibility copies and observe their real startup behavior."""
import json
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
(output / 'report.json').write_text(json.dumps(report), encoding='utf-8')
adb('shell', 'settings', 'put', 'global', 'hidden_api_policy', '1')
adb('shell', 'settings', 'put', 'global', 'hidden_api_policy_pre_p_apps', '1')
adb('shell', 'settings', 'put', 'global', 'hidden_api_policy_p_apps', '1')
adb('shell', 'settings', 'put', 'system', 'screen_off_timeout', '1800000')
probe(Path('artifacts/port/apks'), output / 'report.json', output)
adb('logcat', '-c')
adb('shell', 'am', 'start', '-W', '-n', 'com.evideo.kmbox/com.evideo.kmbox.activity.MicroServiceActivity')
time.sleep(30)
(output / 'main-startup.log').write_text(adb('logcat', '-d', '-v', 'threadtime').stdout, encoding='utf-8')
(output / 'main-activities.txt').write_text(adb('shell', 'dumpsys', 'activity', 'activities').stdout, encoding='utf-8')
shot = adb('exec-out', 'screencap', '-p', binary=True)
if shot.stdout.startswith(b'\x89PNG'):
    (output / 'main-panel.png').write_bytes(shot.stdout)

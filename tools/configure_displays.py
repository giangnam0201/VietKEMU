"""Configure two actual Android guest displays for the original app's TV service."""
from pathlib import Path
import argparse

parser = argparse.ArgumentParser()
parser.add_argument('--avd', type=Path, default=Path.home() / '.android/avd/test.avd/config.ini')
args = parser.parse_args()
values = {
    'hw.lcd.width': '1280', 'hw.lcd.height': '800', 'hw.lcd.density': '160',
    'hw.display1.width': '1920', 'hw.display1.height': '1080',
    'hw.display1.density': '160', 'hw.display1.xOffset': '-1',
    'hw.display1.yOffset': '-1', 'hw.display1.flag': '0',
    'hw.multi_display_window': 'yes',
}
lines = args.avd.read_text().splitlines()
lines = [line for line in lines if line.split('=',1)[0].strip() not in values]
args.avd.write_text('\n'.join(lines) + '\n' + '\n'.join(f'{k}={v}' for k,v in values.items()) + '\n')

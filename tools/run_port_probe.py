"""Run the cloud experiment and release SDK crash reporter subprocesses.

The runner action otherwise waits indefinitely for inherited output pipes when
the emulator exits but its crash reporter stays alive (upstream issue #385).
"""
import os
import argparse
import signal
import subprocess
import sys
from pathlib import Path


def release_crash_reporters():
    sdk = Path(os.environ['ANDROID_HOME']).resolve()
    for process in Path('/proc').iterdir():
        if not process.name.isdigit():
            continue
        try:
            executable = (process / 'exe').resolve(strict=True)
            if executable.name == 'crashpad_handler' and sdk in executable.parents:
                print(f'Releasing emulator crash reporter {process.name}', flush=True)
                os.kill(int(process.name), signal.SIGTERM)
        except (OSError, ProcessLookupError):
            pass


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--permissive-development', action='store_true')
    args = parser.parse_args()
    command = [sys.executable, 'tools/provision_runtime.py', 'artifacts/runtime/bridge']
    if args.permissive_development:
        command.append('--permissive-development')
    try:
        subprocess.run(command, check=True)
        subprocess.run([sys.executable, 'tools/probe_port.py'], check=True)
    finally:
        release_crash_reporters()

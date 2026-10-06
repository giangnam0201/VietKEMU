"""Small decoder test signals, never shipped as karaoke content."""
from pathlib import Path
import subprocess
import sys

output = Path(sys.argv[1])
output.mkdir(parents=True, exist_ok=True)
video = ['-f', 'lavfi', '-i', 'testsrc2=size=320x180:rate=25:duration=20']
subprocess.run(['ffmpeg', '-v', 'error', '-y', *video,
    '-f', 'lavfi', '-i', 'aevalsrc=0.2*sin(2*PI*440*t)|0.2*sin(2*PI*880*t):s=48000:d=20',
    '-map', '0:v', '-map', '1:a', '-c:v', 'libx264', '-preset', 'veryfast', '-crf', '24',
    '-c:a', 'pcm_s16le', str(output / 'stereo.mkv')], check=True)
subprocess.run(['ffmpeg', '-v', 'error', '-y', *video,
    '-f', 'lavfi', '-i', 'sine=frequency=480:sample_rate=48000:duration=20',
    '-f', 'lavfi', '-i', 'sine=frequency=1200:sample_rate=48000:duration=20',
    '-map', '0:v', '-map', '1:a', '-map', '2:a', '-c:v', 'mpeg2video', '-q:v', '5',
    '-c:a', 'mp2', '-b:a', '128k', str(output / 'multiple.ts')], check=True)

"""Cloud-only real yt-dlp/FFmpeg test with one audio HTTP disconnect at 45%."""
import http.server
import json
import socket
import shutil
import subprocess
import sys
import threading
from pathlib import Path

tools, fixtures, output = map(lambda item: Path(item).resolve(), sys.argv[1:4])
output.mkdir(parents=True, exist_ok=True)
for name, options in [('video.mp4', ['-an', '-c:v', 'copy']),
                      ('audio.m4a', ['-vn', '-c:a', 'aac', '-b:a', '128k'])]:
    subprocess.run([str(tools/'ffmpeg.exe'), '-v', 'error', '-y', '-i', str(fixtures/'stereo.mkv'),
                    *options, '-movflags', '+faststart', str(output/name)], check=True)
cut = False
audio_ranges = []

class Handler(http.server.BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def do_GET(self):
        global cut
        name = self.path.lstrip('/')
        if name not in ('audio.m4a', 'video.mp4'):
            self.send_error(404)
            return
        data = (output/name).read_bytes()
        request_range = self.headers.get('Range', '')
        start, end = 0, len(data)-1
        if request_range.startswith('bytes='):
            first, last = request_range[6:].split('-', 1)
            start = int(first or '0')
            if last:
                end = min(int(last), end)
        if name == 'audio.m4a':
            audio_ranges.append(start)
        self.send_response(206 if request_range else 200)
        self.send_header('Accept-Ranges', 'bytes')
        self.send_header('Content-Type', 'audio/mp4' if name == 'audio.m4a' else 'video/mp4')
        self.send_header('Content-Length', str(end-start+1))
        if request_range:
            self.send_header('Content-Range', f'bytes {start}-{end}/{len(data)}')
        self.end_headers()
        if name == 'audio.m4a' and not cut and start == 0:
            cut = True
            self.wfile.write(data[:int(len(data)*.45)])
            self.wfile.flush()
            self.connection.shutdown(socket.SHUT_RDWR)
            self.close_connection = True
        else:
            self.wfile.write(data[start:end+1])

server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), Handler)
threading.Thread(target=server.serve_forever, daemon=True).start()
base = f'http://127.0.0.1:{server.server_port}'
info = {'id': 'fixture0001', 'title': 'HTTP audio reconnect fixture', 'duration': 20,
        'extractor': 'generic', 'webpage_url': base,
        'formats': [{'format_id': '137', 'url': base+'/video.mp4', 'ext': 'mp4',
                     'vcodec': 'avc1.64001f', 'acodec': 'none', 'height': 180},
                    {'format_id': '140', 'url': base+'/audio.m4a', 'ext': 'm4a',
                     'vcodec': 'none', 'acodec': 'mp4a.40.2'}]}
info_file = output/'fixture-info.json'
info_file.write_text(json.dumps(info), encoding='utf-8')
shutil.copy2(fixtures/'audio-ends-early.ts', output/'legacy-incomplete.ts')
try:
    subprocess.run(['dotnet', 'run', '--project', 'windows/VietK.CoreChecks', '-c', 'Release', '--',
                    '--verify-progressive-fixture', str(tools), str(info_file), str(output/'cache')],
                   check=True, timeout=120)
    if not cut or not any(start > 0 for start in audio_ranges):
        raise RuntimeError('Fixture did not prove audio interruption and byte-range recovery')
    report = {'audioDisconnectedAt45Percent': cut, 'audioResumedWithRange': True,
              'completeAudioVideoCoverageVerified': True,
              'validLegacyCacheReusedAndTruncatedLegacyCacheRejected': True}
    (output/'reconnect-verification.json').write_text(json.dumps(report, indent=2))
    print(json.dumps(report))
finally:
    server.shutdown()

"""Fetch only a public Volume 40 TOC range; publish counts, never song data."""
import json
import re
import sys
import urllib.request
from pathlib import Path

URL = 'https://archive.org/download/midi-vision-karaoke-vol-40/UNDEFINED.iso'
OFFSET = 1522703 * 2048  # ISO9660 directory extent independently inspected.
SIZE = 131072
TOTAL = 3800203264
ROOT = Path('.reference/arirang-multak')


def download():
    request = urllib.request.Request(URL, headers={
        'Range': f'bytes={OFFSET}-{OFFSET + SIZE - 1}', 'Accept-Encoding': 'identity'})
    with urllib.request.urlopen(request, timeout=45) as response:
        match = re.fullmatch(r'bytes (\d+)-(\d+)/(\d+)', response.headers.get('Content-Range', ''))
        if response.status != 206 or not match or tuple(map(int, match.groups())) != (OFFSET, OFFSET + SIZE - 1, TOTAL):
            raise ValueError('Public server did not return the exact bounded ISO range')
        data = response.read(SIZE + 1)
    if len(data) != SIZE or data[4:13] != b'multak3.3':
        raise ValueError('Unexpected Volume 40 music-container header')
    ROOT.mkdir(parents=True, exist_ok=True)
    (ROOT / 'header.bin').write_bytes(data)


def report():
    parsed = json.loads((ROOT / 'parsed.json').read_text(encoding='utf-8'))
    assert (parsed['slots'], parsed['nullSlots'], parsed['datPointers'], parsed['da1Pointers']) == (22910, 10, 17111, 5789)
    assert (parsed['first']['TableIndex'], parsed['first']['StorageFile'], parsed['first']['Offset']) == (1, 0, 196608)
    assert (parsed['last']['TableIndex'], parsed['last']['StorageFile'], parsed['last']['Offset']) == (22909, 1, 670754816)
    assert not parsed['songNumbersVerified'] and not parsed['musicEventsDecoded'] and not parsed['playbackVerified']
    parsed.update(sourceUrl=URL, rangeBytesFetched=SIZE)
    output = Path('artifacts/arirang-verification/multak-verification.json')
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(parsed, indent=2), encoding='utf-8')
    print(f"Verified {parsed['datPointers'] + parsed['da1Pointers']} storage pointers; MIDI decoding remains unfinished.")


if __name__ == '__main__':
    {'download': download, 'report': report}[sys.argv[1]]()

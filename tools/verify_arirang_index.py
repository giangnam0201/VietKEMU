"""Validate original disc text records; publish only counts, not the index data."""
import json
import sys
import time
import urllib.request
from pathlib import Path


root = Path('.reference/arirang-index')
url = 'https://archive.org/download/maseco_vol.48/maseco_vol.48/hk2/info.dat'
if sys.argv[1] == 'download':
    root.mkdir(parents=True, exist_ok=True)
    for attempt in range(3):
        try:
            with urllib.request.urlopen(url, timeout=30) as response:
                data = response.read(2 * 1024 * 1024)
            if len(data) != 1320466:
                raise ValueError('Unexpected Volume 48 index size')
            (root / 'INFO.DAT').write_bytes(data)
            break
        except Exception:
            if attempt == 2:
                raise
            time.sleep(2)
elif sys.argv[1] == 'report':
    parsed = json.loads((root / 'parsed.json').read_text(encoding='utf-8'))
    assert parsed['count'] == 3795, 'Reader does not match the independently inspected full English block'
    assert parsed['songs'][0]['RecordOffset'] == 0x89AB3
    assert parsed['songs'][-1]['RecordOffset'] == 0xB2784
    assert not parsed['songIdsVerified'] and not parsed['playbackVerified']
    assert all(song['SongNumber'] is None and not song['PlaybackVerified'] for song in parsed['songs'])
    report = {'sourceUrl': url, 'originalIndexBytes': 1320466,
              'englishRecords': parsed['count'], 'songIdsVerified': False,
              'proprietaryMusicPlaybackVerified': False}
    output = Path('artifacts/arirang-verification/index-verification.json')
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report))
else:
    raise ValueError('Expected download or report')

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


def read_range(offset, size):
    if offset < 0 or size <= 0 or size > 2 * 1024 * 1024 or offset + size > TOTAL:
        raise ValueError('Range exceeds bounded public input')
    request = urllib.request.Request(URL, headers={
        'Range': f'bytes={offset}-{offset + size - 1}', 'Accept-Encoding': 'identity'})
    with urllib.request.urlopen(request, timeout=45) as response:
        match = re.fullmatch(r'bytes (\d+)-(\d+)/(\d+)', response.headers.get('Content-Range', ''))
        if response.status != 206 or not match or tuple(map(int, match.groups())) != (offset, offset + size - 1, TOTAL):
            raise ValueError('Public server did not return the exact bounded ISO range')
        data = response.read(size + 1)
    if len(data) != size:
        raise ValueError('Truncated public ISO range')
    return data


def download():
    data = read_range(OFFSET, SIZE)
    if len(data) != SIZE or data[4:13] != b'multak3.3':
        raise ValueError('Unexpected Volume 40 music-container header')
    ROOT.mkdir(parents=True, exist_ok=True)
    (ROOT / 'header.bin').write_bytes(data)
    index = read_range(1174375 * 2048, 1044480)
    if index[0x7d0:0x7dc] != b'Multak MID10':
        raise ValueError('Unexpected Volume 40 song-index header')
    (ROOT / 'index.idx').write_bytes(index)
    for code in (30001, 30093, 50001):
        base_at = 16 + 2 * (code // 1000)
        base = int.from_bytes(data[base_at:base_at + 2], 'little')
        slot = base + code % 1000
        pointer = data[3360 + slot * 4:3364 + slot * 4]
        if len(pointer) != 4 or pointer == b'\xff\0\xff\xff' or pointer[3] >> 4 > 1:
            raise ValueError('Invalid sample song pointer')
        storage = pointer[3] >> 4
        relative = ((pointer[0] * 60 + pointer[1]) * 75 + pointer[2]) * 2048 + (65536 if storage == 0 else 0)
        extent = 1522703 if storage == 0 else 1195176
        (ROOT / f'{code}.bin').write_bytes(read_range(extent * 2048 + relative, 1024))
    pointers = []
    count = int.from_bytes(data[334:336], 'little')
    for slot in range(count):
        pointer = data[3360 + slot * 4:3364 + slot * 4]
        if pointer == b'\xff\0\xff\xff':
            continue
        storage = pointer[3] >> 4
        relative = ((pointer[0] * 60 + pointer[1]) * 75 + pointer[2]) * 2048 + (65536 if storage == 0 else 0)
        pointers.append((slot, storage, relative))
    for code, expected_bytes in ((30655, 6144), (32153, 4096)):
        at = 16 + 2 * (code // 1000)
        slot = int.from_bytes(data[at:at + 2], 'little') + code % 1000
        _, storage, relative = next(p for p in pointers if p[0] == slot)
        end = min(p[2] for p in pointers if p[1] == storage and p[2] > relative)
        size = end - relative
        if size != expected_bytes:
            raise ValueError('Original layout song extent differs from independent inspection')
        extent = 1522703 if storage == 0 else 1195176
        (ROOT / f'complete-{code}.bin').write_bytes(read_range(extent * 2048 + relative, size))


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
    catalogue = json.loads((ROOT / 'catalogue-parsed.json').read_text(encoding='utf-8'))
    assert (catalogue['catalogueRecords'], catalogue['englishTitles'], catalogue['mappedMusicPointers'], catalogue['uniqueMappedSlots']) == (22900, 4140, 22900, 22900)
    assert catalogue['originalSongHeadersMatched'] == 3 and catalogue['completeOneToOneMapping'] and catalogue['deviceCodeMappingVerified']
    assert not catalogue['musicEventsDecoded'] and not catalogue['playbackVerified']
    catalogue.update(sourceUrl=URL, rangeBytesFetched=SIZE + 1044480 + 3 * 1024)
    (output.parent / 'song-mapping-verification.json').write_text(json.dumps(catalogue, indent=2), encoding='utf-8')
    print(f"Verified {parsed['datPointers'] + parsed['da1Pointers']} storage pointers; MIDI decoding remains unfinished.")


if __name__ == '__main__':
    {'download': download, 'report': report}[sys.argv[1]]()

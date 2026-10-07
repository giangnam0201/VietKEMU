"""Bounded MULTAK title-header research, not a proprietary MIDI decoder.

The header offset must come from an independently established record boundary.
Offsets, song numbering and music masks vary between disc families. Never infer
playability from a readable title. No downloaded source code is executed here.
"""
import argparse
import json
from pathlib import Path

MAX_SAMPLE = 4 * 1024 * 1024


def read_sample(path):
    with Path(path).open('rb') as stream:
        data = stream.read(MAX_SAMPLE + 1)
    if len(data) > MAX_SAMPLE:
        raise ValueError('Use a bounded song sample, not an entire disc container')
    return data


def read_title_header(data, header_offset):
    """Read the observed four-byte title prefix and XOR-encoded terminator.

    Only the first prefix byte is established as the title mask. The remaining
    three bytes are reported raw rather than guessed to be lengths or tempos.
    This operation deliberately does not unmask notes or lyric timing data.
    """
    if header_offset < 0 or header_offset + 4 >= len(data):
        raise ValueError('Title header falls outside the sample')
    mask = data[header_offset]
    start = header_offset + 4
    end = data.find(bytes([mask]), start, min(len(data), start + 512))
    if end < 0:
        raise ValueError('No bounded encoded title terminator')
    decoded = bytes(value ^ mask for value in data[start:end])
    if not decoded or any(value < 32 for value in decoded):
        raise ValueError('Invalid title payload; check the record boundary')
    return {
        'headerOffset': header_offset,
        'titleStart': start,
        'titleEndExclusive': end,
        'mask': mask,
        'uninterpretedPrefix': data[header_offset + 1:start].hex(),
        'decodedTitleBytes': decoded,
    }


def compare_research_pair(raw, reference, header_offset):
    """Check the original author's raw/unmasked sample pair byte for byte.

    Matching this historical transformation is evidence about the sample, not
    evidence that the same mask decodes its music events.
    """
    title = read_title_header(raw, header_offset)
    if len(raw) != len(reference):
        raise ValueError('The reference must have the same sample length')
    start = header_offset + 4
    expected = raw[:start] + bytes(value ^ title['mask'] for value in raw[start:])
    return {
        'sampleBytes': len(raw),
        'titleHeaderOffset': header_offset,
        'titleBytes': len(title['decodedTitleBytes']),
        'referenceTransformationMatches': expected == reference,
        'musicEventsDecoded': False,
        'songNumberVerified': False,
        'playbackVerified': False,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('input', type=Path)
    parser.add_argument('--header-offset', required=True, type=lambda value: int(value, 0))
    parser.add_argument('--reference', type=Path)
    parser.add_argument('--report', type=Path)
    args = parser.parse_args()
    raw = read_sample(args.input)
    if args.reference:
        report = compare_research_pair(raw, read_sample(args.reference), args.header_offset)
        if not report['referenceTransformationMatches']:
            raise ValueError('The historical transformation does not match this pair')
    else:
        title = read_title_header(raw, args.header_offset)
        report = {'sampleBytes': len(raw), 'titleHeaderOffset': args.header_offset,
                  'titleBytes': len(title['decodedTitleBytes']),
                  'musicEventsDecoded': False, 'songNumberVerified': False,
                  'playbackVerified': False}
    text = json.dumps(report, indent=2) + '\n'
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(text, encoding='utf-8')
    print(text, end='')


if __name__ == '__main__':
    main()

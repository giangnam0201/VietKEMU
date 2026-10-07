"""Inspect a caller-supplied candidate Multak bank; never fetch or export samples.

This is format research, not a synthesizer or proof of Arirang compatibility.
The inspected phone player's bank has a 4096-byte wrapper and 4 MiB payload.
"""
import argparse
import json
import struct
from pathlib import Path

PAYLOAD_BYTES = 4 * 1024 * 1024
DIRECTORY = 0x1D0


def transform_blocks(payload: bytes) -> bytes:
    if not payload or len(payload) > PAYLOAD_BYTES or len(payload) % 512:
        raise ValueError("Expected complete 512-byte blocks, at most 4 MiB")
    result = bytearray(payload)
    permutations = []
    for index in range(256):
        reverse = int(f"{index:08b}"[::-1], 2)
        mask = 0x55
        for source, target in enumerate((1, 5, 3, 7, 0, 4, 2, 6)):
            mask ^= ((index >> source) & 1) << target
        permutations.append((reverse, mask))
    for base in range(0, len(result), 512):
        for index, (reverse, mask) in enumerate(permutations):
            left, right = base + index, base + 256 + reverse
            result[left], result[right] = result[right] ^ mask, result[left] ^ mask
    return bytes(result)


def inspect_payload(payload: bytes) -> dict:
    if len(payload) != PAYLOAD_BYTES:
        raise ValueError("This candidate layout requires exactly 4 MiB")

    def word(offset):
        if offset < 0 or offset + 2 > len(payload):
            raise ValueError("Bank pointer lies outside the payload")
        return struct.unpack_from("<H", payload, offset)[0]

    selections = regions = 0
    voice_pointers = set()
    for program in range(128):
        table = word(DIRECTORY + (17 + program) * 2) * 2
        previous_selector = -1
        for entry in range(256):
            selector = word(table + entry * 4)
            if selector == 0xFFFF:
                break
            bank_select = selector & 255
            if bank_select <= previous_selector:
                raise ValueError("Program bank selections are not increasing")
            previous_selector = bank_select
            bank = word(DIRECTORY + ((selector >> 8) + 1) * 2)
            region_table = (bank * 65536 + word(table + entry * 4 + 2)) * 2
            selections += 1
            for region in range(1024):
                offset = region_table + region * 6
                lower, upper, voice = word(offset), word(offset + 2), word(offset + 4)
                low_note, high_note = (lower >> 8) & 127, (upper >> 8) & 127
                low_velocity, high_velocity = lower & 127, upper & 255
                if low_note > high_note or not low_velocity <= high_velocity <= 127:
                    raise ValueError("Invalid instrument note or velocity range")
                voice_pointer = (bank * 65536 + voice) * 2
                word(voice_pointer)
                voice_pointers.add(voice_pointer)
                regions += 1
                if upper & 0x8000:
                    break
            else:
                raise ValueError("Instrument region list lacks a bounded terminator")
        else:
            raise ValueError("Program bank list lacks a bounded terminator")

    return {
        "payloadBytes": len(payload),
        "melodicPrograms": 128,
        "bankSelections": selections,
        "instrumentRegions": regions,
        "distinctVoicePointers": len(voice_pointers),
        "regionBoundsValidated": True,
        "sampleLoopsDecoded": False,
        "envelopesDecoded": False,
        "audiblePlaybackVerified": False,
        "originalArirangCompatibilityVerified": False,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("bank", type=Path)
    parser.add_argument("--decoded", action="store_true", help="Input is already the raw 4 MiB payload")
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()
    expected = PAYLOAD_BYTES if args.decoded else PAYLOAD_BYTES + 4096
    with args.bank.open("rb") as stream:
        data = stream.read(expected + 1)
    if len(data) != expected:
        raise ValueError("Unexpected bank file length")
    if not args.decoded and data[:16] != b"VER\0MIDIROM.BIN\0":
        raise ValueError("Unsupported bank wrapper")
    payload = data if args.decoded else transform_blocks(data[4096:])
    report = inspect_payload(payload)
    text = json.dumps(report, indent=2)
    if args.report:
        args.report.write_text(text + "\n", encoding="utf-8")
    print(text)


if __name__ == "__main__":
    main()

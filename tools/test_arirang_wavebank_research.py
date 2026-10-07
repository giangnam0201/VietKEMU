import struct
import unittest
from arirang_wavebank_research import DIRECTORY, PAYLOAD_BYTES, inspect_payload, transform_blocks


def fixture():
    data = bytearray(PAYLOAD_BYTES)
    for program in range(128):
        struct.pack_into("<H", data, DIRECTORY + (17 + program) * 2, 0x400)
    struct.pack_into("<HHHH", data, 0x800, 0, 0x480, 0xFFFF, 0xFFFF)
    struct.pack_into("<HHH", data, 0x900, 0, 0xFF7F, 0x500)
    return data


class BankChecks(unittest.TestCase):
    def test_permutation_and_mask(self):
        data = bytes(index % 256 for index in range(1024))
        result = transform_blocks(data)
        self.assertEqual(result[0], data[256] ^ 0x55)
        self.assertEqual(result[1], data[384] ^ 0x57)
        self.assertEqual(transform_blocks(result), data)

    def test_program_region_directory(self):
        report = inspect_payload(fixture())
        self.assertEqual(report["bankSelections"], 128)
        self.assertEqual(report["instrumentRegions"], 128)
        self.assertEqual(report["distinctVoicePointers"], 1)
        self.assertFalse(report["audiblePlaybackVerified"])

    def test_invalid_region(self):
        data = fixture()
        struct.pack_into("<H", data, 0x900, 127 << 8)
        struct.pack_into("<H", data, 0x902, 0x807F)
        with self.assertRaises(ValueError):
            inspect_payload(data)

    def test_invalid_bank_pointer(self):
        data = fixture()
        struct.pack_into("<H", data, DIRECTORY + 2, 0xFFFF)
        with self.assertRaises(ValueError):
            inspect_payload(data)

    def test_invalid_sizes(self):
        for data in (b"", b"x" * 511, b"x" * (PAYLOAD_BYTES + 512)):
            with self.assertRaises(ValueError):
                transform_blocks(data)
        with self.assertRaises(ValueError):
            inspect_payload(b"x" * 512)


if __name__ == "__main__":
    unittest.main()

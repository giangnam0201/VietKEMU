import io
import tempfile
import unittest
import zipfile
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from analyze_firmware import reconstruct


class ReconstructionTests(unittest.TestCase):
    def archive(self, transfer, data):
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, 'w') as archive:
            archive.writestr('system.transfer.list', transfer)
            archive.writestr('system.new.dat', data)
        stream.seek(0)
        return zipfile.ZipFile(stream)

    def test_noncontiguous_new_blocks_and_zero_holes(self):
        with self.archive('3\n2\n0\n0\nnew 4,0,1,3,4\nzero 2,1,3\n', b'A' * 4096 + b'B' * 4096) as archive:
            with tempfile.TemporaryDirectory() as directory:
                output = Path(directory) / 'system.img'
                reconstruct(archive, output)
                self.assertEqual(output.read_bytes(), b'A' * 4096 + bytes(8192) + b'B' * 4096)

    def test_rejects_incremental_updates(self):
        with self.archive('3\n1\n0\n0\nmove 2,0,1\n', b'') as archive:
            with tempfile.TemporaryDirectory() as directory:
                with self.assertRaisesRegex(ValueError, 'Incremental'):
                    reconstruct(archive, Path(directory) / 'system.img')

    def test_detects_truncated_input(self):
        with self.archive('3\n1\n0\n0\nnew 2,0,1\n', b'A') as archive:
            with tempfile.TemporaryDirectory() as directory:
                with self.assertRaisesRegex(ValueError, 'Truncated'):
                    reconstruct(archive, Path(directory) / 'system.img')

    def test_detects_extra_blocks(self):
        with self.archive('3\n1\n0\n0\nnew 2,0,1\n', b'A' * 8192) as archive:
            with tempfile.TemporaryDirectory() as directory:
                with self.assertRaisesRegex(ValueError, 'Unconsumed'):
                    reconstruct(archive, Path(directory) / 'system.img')


if __name__ == '__main__':
    unittest.main()

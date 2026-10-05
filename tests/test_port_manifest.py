"""Check binary manifest edits without shipping proprietary firmware fixtures."""
import struct
import unittest
from tools.port_apks import patch_manifest


def document():
    strings = ['manifest', 'application', 'uses-library', 'name',
               'com.evideostb.vdk', 'another.library', 'sharedUserId', 'android.uid.system']
    encoded = [bytes((len(s), len(s))) + s.encode() + b'\0' for s in strings]
    offsets, position = [], 0
    for s in encoded:
        offsets.append(position)
        position += len(s)
    pool = struct.pack('<HHI5I', 1, 28, 28 + 4 * len(strings) + position,
                       len(strings), 0, 256, 28 + 4 * len(strings), 0)
    pool += struct.pack('<' + 'I' * len(offsets), *offsets) + b''.join(encoded)

    def start(name, attrs=()):
        records = b''.join(struct.pack('<IIIHBBI', 0xffffffff, strings.index(k),
                                      strings.index(v), 8, 0, 3, strings.index(v)) for k, v in attrs)
        return struct.pack('<HHIII', 0x102, 16, 36 + len(records), 1, 0xffffffff) + struct.pack(
            '<II6H', 0xffffffff, strings.index(name), 20, 20, len(attrs), 0, 0, 0) + records

    def end(name):
        return struct.pack('<HHIIIII', 0x103, 16, 24, 1, 0xffffffff, 0xffffffff, strings.index(name))

    chunks = [pool, start('manifest', [('sharedUserId', 'android.uid.system')]),
              start('application'), start('uses-library', [('name', 'com.evideostb.vdk')]),
              end('uses-library'), start('uses-library', [('name', 'another.library')]),
              end('uses-library'), end('application'), end('manifest')]
    body = b''.join(chunks)
    return struct.pack('<HHI', 3, 8, len(body) + 8) + body


class ManifestTests(unittest.TestCase):
    def test_vendor_dependency_removed_without_removing_other_nodes(self):
        original = document()
        patched, changes = patch_manifest(original)
        self.assertEqual(len(changes), 1)
        self.assertEqual(len(original) - len(patched), 80)
        self.assertEqual(struct.unpack_from('<I', patched, 4)[0], len(patched))
        second, changes = patch_manifest(patched)
        self.assertEqual(second, patched)
        self.assertEqual(changes, [])

    def test_ordinary_experiment_removes_uid_attribute_only_when_requested(self):
        patched, changes = patch_manifest(document(), remove_system_uid=True)
        self.assertEqual(len(changes), 2)
        self.assertEqual(len(document()) - len(patched), 100)
        self.assertEqual(patch_manifest(patched, True), (patched, []))

    def test_truncated_document_is_rejected(self):
        with self.assertRaises(ValueError):
            patch_manifest(document()[:-1])


if __name__ == '__main__':
    unittest.main()

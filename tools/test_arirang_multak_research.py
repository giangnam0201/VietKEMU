import unittest
from arirang_multak_research import compare_research_pair, read_title_header


class MultakHeaderChecks(unittest.TestCase):
    def sample(self):
        mask = 0xE2
        title = b'TEST//COMPOSER//'
        raw = b'OK' + bytes([mask, 2, 0, 0]) + bytes(x ^ mask for x in title + b'\0') + b'\x90\x40\x7f'
        return raw, title

    def test_title_boundary_does_not_decode_music(self):
        raw, title = self.sample()
        result = read_title_header(raw, 2)
        self.assertEqual(result['decodedTitleBytes'], title)
        self.assertEqual(result['titleEndExclusive'], 6 + len(title))

    def test_historical_pair_match_and_mismatch(self):
        raw, _ = self.sample()
        reference = raw[:6] + bytes(x ^ 0xE2 for x in raw[6:])
        result = compare_research_pair(raw, reference, 2)
        self.assertTrue(result['referenceTransformationMatches'])
        self.assertFalse(result['playbackVerified'])
        self.assertFalse(compare_research_pair(raw, raw, 2)['referenceTransformationMatches'])

    def test_reject_wrong_boundaries_and_unterminated_titles(self):
        for raw, offset in [(b'', 0), (b'abcde', -1), (b'abcde', 4),
                            (b'\0\0\0\0' + b'a' * 600, 0),
                            (b'\0\0\0\0A\x01\0', 0)]:
            with self.assertRaises(ValueError):
                read_title_header(raw, offset)


if __name__ == '__main__':
    unittest.main()

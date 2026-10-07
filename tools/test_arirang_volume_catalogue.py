import unittest

from arirang_volume_catalogue import download_url, normalize_item


class VolumeCatalogueTests(unittest.TestCase):
    def test_disc_is_not_exposed_as_individual_song(self):
        item = normalize_item('volume-48', {'metadata': {'title': 'Midi Vision Karaoke Vol.48'},
                                          'files': [{'name': 'disc.iso', 'size': '4000000000'},
                                                    {'name': 'midi0/ARVNKR.d00'},
                                                    {'name': 'VIDEO_TS/Part 1.mp4'}]})
        self.assertEqual(48, item['volume'])
        self.assertEqual(['disc-image', 'song-data-container', 'volume-video'],
                         [f['kind'] for f in item['files']])
        self.assertTrue(all(not f['individualSongPlaybackVerified'] for f in item['files']))
        self.assertTrue(item['files'][2]['downloadUrl'].endswith('VIDEO_TS/Part%201.mp4'))

    def test_restricted_items_and_private_files_have_no_download_links(self):
        for restricted in (True, 'true', '1'):
            item = normalize_item('disc', {'metadata': {'access-restricted-item': restricted},
                                          'files': [{'name': 'song.mp4'}]})
            self.assertIsNone(item['files'][0]['downloadUrl'])
        item = normalize_item('disc', {'metadata': {'access-restricted-item': 'false'},
                                      'files': [{'name': 'song.mp4', 'private': 'true'},
                                                {'name': 'other.mp4', 'private': 'false'}]})
        self.assertIsNone(item['files'][0]['downloadUrl'])
        self.assertIsNotNone(item['files'][1]['downloadUrl'])

    def test_path_segments_and_query_characters(self):
        for name in ('../secret.mp4', '/song.mp4', 'x/../song.mp4', 'x\\song.mp4'):
            with self.assertRaises(ValueError):
                download_url('disc', name)
        self.assertEqual('https://archive.org/download/disc/x/a%3Fq%3D1%23.mp4',
                         download_url('disc', 'x/a?q=1#.mp4'))


if __name__ == '__main__':
    unittest.main()

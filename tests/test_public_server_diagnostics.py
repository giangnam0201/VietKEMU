import json
import unittest
from tools.probe_public_vietk_services import summarize_payload


class PublicDiagnosticPrivacyTests(unittest.TestCase):
    def test_settings_report_never_copies_tokens_workers_or_arbitrary_values(self):
        secret = 'PRIVATE_SENTINEL_88441'
        payload = {'youtube_download_enabled': 0, 'cdn_enabled': 0,
                   'youtube_offline_enabled': secret, 'token_key': secret,
                   'source': secret, 'errorcode': secret,
                   'workers': [{'url': 'http://example.test/?token=' + secret}],
                   'url': 'http://example.test/?token=' + secret}
        result = summarize_payload('vietk-settings', payload)
        self.assertNotIn(secret, json.dumps(result))
        self.assertEqual({'youtube_download_enabled': 0, 'cdn_enabled': 0}, result['settings'])
        self.assertFalse(result['hasMediaUrl'])

    def test_http_success_with_worker_error_does_not_establish_media(self):
        result = summarize_payload('vietk-video-direct',
                                   {'code': 500, 'source': 'Worker',
                                    'error': '<title>403 Forbidden</title> PRIVATE_SENTINEL'})
        self.assertEqual(500, result['applicationCode'])
        self.assertTrue(result['upstreamForbidden'])
        self.assertFalse(result['hasMediaUrl'])
        self.assertFalse(result['playbackVerified'])
        self.assertNotIn('PRIVATE_SENTINEL', json.dumps(result))

    def test_returned_url_is_recorded_only_as_boolean_not_playback_proof(self):
        result = summarize_payload('vietk-video-direct',
                                   {'url': 'https://example.test/video?token=PRIVATE_SENTINEL'})
        self.assertTrue(result['hasMediaUrl'])
        self.assertFalse(result['playbackVerified'])
        self.assertNotIn('PRIVATE_SENTINEL', json.dumps(result))


if __name__ == '__main__':
    unittest.main()

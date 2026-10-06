"""Bounded public firmware-route diagnostics; no credentials or raw replies.

This experiment is not part of the Windows application. Configuration responses
can contain private application values, so only explicit whitelisted evidence is
written to the report. A reachable endpoint never counts as verified playback.
"""
import argparse
import concurrent.futures
import datetime
import json
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path


ROUTES = (
    ('vietk-settings', 'http://api.vktv.vn:8540/v2/settings'),
    ('vietk-video-direct', 'http://api.vktv.vn:8540/v2/youtube/getlink?id=h77qhzCv-WQ&c=0'),
    ('vietk-video-cached', 'http://api.vktv.vn:8540/v2/youtube/getlink?id=h77qhzCv-WQ&c=1'),
    ('vietk-video-cdn', 'http://api.vktv.vn:8540/v2/youtube/getlinkcdn?id=h77qhzCv-WQ&c=1'),
    ('separate-ktv-service', 'https://ktvapi.duochang.cc/ktv_compatible/'),
)
SETTINGS = ('youtube_download_enabled', 'youtube_offline_enabled', 'cdn_enabled',
            'search_song_in_hdd_only', 'youtube_getlink_mode', 'active_youtube_player')


def summarize_payload(name, payload):
    result = {'hasMediaUrl': False, 'playbackVerified': False}
    if not isinstance(payload, dict):
        result['jsonObject'] = False
        return result
    result['jsonObject'] = True
    code = payload.get('code', payload.get('errorcode'))
    if isinstance(code, int) and not isinstance(code, bool):
        result['applicationCode'] = code
    elif isinstance(code, str) and code.isascii() and code.isdigit() and len(code) <= 6:
        result['applicationCode'] = int(code)
    source = payload.get('source')
    if source in ('Worker', 'Server', 'Client'):
        result['source'] = source
    error = payload.get('error', payload.get('errormessage', ''))
    if isinstance(error, str):
        result['upstreamForbidden'] = '403 Forbidden' in error
        result['authenticationRequired'] = any(s in error.lower() for s in
                                               ('no authority', 'authorization', 'unauthorized'))
    if name == 'vietk-settings':
        result['settings'] = {key: payload[key] for key in SETTINGS
                              if type(payload.get(key)) is int and 0 <= payload[key] <= 100}
    else:
        data = payload.get('data')
        candidates = [payload.get('url')]
        if isinstance(data, dict):
            candidates.append(data.get('url'))
        result['hasMediaUrl'] = any(isinstance(url, str) and
                                   urllib.parse.urlsplit(url).scheme in ('http', 'https') and
                                   bool(urllib.parse.urlsplit(url).hostname) for url in candidates)
    return result


def probe(route):
    name, url = route
    started = time.monotonic()
    result = {'endpoint': name, 'requestedUrl': url, 'playbackVerified': False}
    request = urllib.request.Request(url, headers={'User-Agent': 'VietKEMU-Protocol-Diagnostic/1.0',
                                                  'Accept': 'application/json'})
    try:
        try:
            response = urllib.request.urlopen(request, timeout=15)
        except urllib.error.HTTPError as error:
            response = error
        with response:
            body = response.read(65537)
            result.update(httpStatus=response.status, bytesRead=len(body),
                          redirected=response.geturl() != url)
            if len(body) > 65536:
                result['responseLimitExceeded'] = True
            else:
                try:
                    payload = json.loads(body)
                    result.update(summarize_payload(name, payload))
                except (ValueError, TypeError):
                    result['jsonObject'] = False
    except Exception as error:
        # Exception text and raw URLs can contain untrusted response values.
        result['transportError'] = type(error).__name__
    result['elapsedMilliseconds'] = round((time.monotonic() - started) * 1000)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
        results = list(pool.map(probe, ROUTES))
    report = {'timestampUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
              'scope': 'Public source-confirmed routes only; no login, identities, keys, cookies or media downloads.',
              'playbackVerified': False, 'results': results}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()

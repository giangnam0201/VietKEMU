"""Discover Maseco disc volumes through public Internet Archive JSON APIs.

This research tool downloads metadata only. Disc/background video entries are
deliberately not represented as individually playable karaoke songs.
"""
import argparse
import concurrent.futures
import datetime
import json
import re
import urllib.parse
import urllib.request
from pathlib import Path


BASE = 'https://archive.org'
QUERY = 'creator:"Maseco"'
MAX_JSON_BYTES = 8 * 1024 * 1024


def fetch_json(url):
    request = urllib.request.Request(url, headers={'User-Agent': 'VietKEMU-volume-research/1.0'})
    with urllib.request.urlopen(request, timeout=25) as response:
        data = response.read(MAX_JSON_BYTES + 1)
    if len(data) > MAX_JSON_BYTES:
        raise ValueError('Metadata response exceeded size limit')
    return json.loads(data)


def flag(value):
    return value is True or str(value).lower() in ('true', '1', 'yes')


def download_url(identifier, name):
    # These are remote paths only; never interpret archive names as local paths.
    if not name or name.startswith(('/', '\\')) or '\\' in name:
        raise ValueError('Invalid archive file path')
    if any(part in ('', '.', '..') for part in name.split('/')):
        raise ValueError('Invalid archive file path')
    return f'{BASE}/download/{urllib.parse.quote(identifier, safe="")}/{urllib.parse.quote(name, safe="/")}'


def file_kind(name):
    lower = name.lower()
    if lower.endswith(('.iso', '.img', '.bin', '.nrg')):
        return 'disc-image'
    if re.search(r'(?:^|/)midi\d*/.*\.(?:d\d+|dat|mid|kar)$', lower):
        return 'song-data-container'
    if lower.endswith(('.mp4', '.mkv', '.webm', '.vob', '.mpg', '.mpeg')):
        return 'volume-video'
    if lower.endswith(('.rar', '.zip', '.7z')):
        return 'archive-package'
    if lower.endswith(('.dat', '.ifo', '.bup', '.pdf', '.txt', '.csv', '.json')):
        return 'supporting-data'
    return None


def normalize_item(identifier, document):
    if not isinstance(document, dict) or not isinstance(document.get('metadata'), dict):
        raise ValueError('Item metadata unavailable')
    meta = document['metadata']
    title = str(meta.get('title', identifier))
    match = re.search(r'\bvol(?:ume)?[.\s_-]*(\d+)\b', title, re.I)
    restricted = flag(document.get('is_dark')) or flag(meta.get('access-restricted-item'))
    files = []
    for entry in document.get('files', []):
        name = entry.get('name', '')
        kind = file_kind(name)
        if not kind or name.endswith(('_meta.xml', '_files.xml', '_meta.json')):
            continue
        try:
            url = download_url(identifier, name)
        except ValueError:
            continue
        accessible = not restricted and not flag(entry.get('private'))
        try:
            size = int(entry['size'])
        except (KeyError, ValueError, TypeError):
            size = None
        files.append({'name': name, 'kind': kind, 'bytes': size,
                      'format': entry.get('format'), 'source': entry.get('source'),
                      'downloadUrl': url if accessible else None,
                      'publicMetadataAllowsDownload': accessible,
                      'individualSongPlaybackVerified': False})
    return {'itemId': identifier, 'title': title,
            'volume': int(match.group(1)) if match else None,
            'detailsUrl': f'{BASE}/details/{urllib.parse.quote(identifier, safe="")}',
            'metadataUrl': f'{BASE}/metadata/{urllib.parse.quote(identifier, safe="")}',
            'restricted': restricted, 'files': files}


def discover():
    documents = []
    page = 1
    while True:
        params = urllib.parse.urlencode({'q': QUERY, 'output': 'json', 'rows': 100,
                                        'page': page, 'fl[]': 'identifier'})
        result = fetch_json(f'{BASE}/advancedsearch.php?{params}')['response']
        batch = result['docs']
        documents.extend(batch)
        if len(documents) >= result['numFound']:
            return documents
        if not batch or page >= 100:
            raise ValueError('Archive search ended before all results were received')
        page += 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cache', type=Path, default=Path('.reference/arirang-research'))
    parser.add_argument('--output', type=Path, default=Path('.reference/arirang-research/volume-catalogue.json'))
    parser.add_argument('--offline', action='store_true', help='Normalize previously saved metadata without network access')
    args = parser.parse_args()
    args.cache.mkdir(parents=True, exist_ok=True)
    search_path = args.cache / 'maseco-search.json'
    if args.offline:
        documents = json.loads(search_path.read_text(encoding='utf-8'))['response']['docs']
    else:
        documents = discover()
        search_path.write_text(json.dumps({'response': {'docs': documents}}, ensure_ascii=False), encoding='utf-8')

    def load(entry):
        identifier = entry['identifier']
        # Archive identifiers, unlike filenames, must be a single safe component.
        if not re.fullmatch(r'[A-Za-z0-9_.-]+', identifier) or identifier in ('.', '..'):
            return {'itemId': identifier, 'error': 'Invalid identifier'}
        try:
            path = args.cache / f'{identifier}.json'
            if args.offline:
                data = json.loads(path.read_text(encoding='utf-8'))
            else:
                data = fetch_json(f'{BASE}/metadata/{urllib.parse.quote(identifier, safe="")}')
                path.write_text(json.dumps(data, ensure_ascii=False), encoding='utf-8')
            return normalize_item(identifier, data)
        except Exception as error:
            return {'itemId': identifier, 'error': type(error).__name__ + ': ' + str(error)}

    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as executor:
        items = list(executor.map(load, documents))
    items.sort(key=lambda item: (item.get('volume') is None, item.get('volume') or 0, item['itemId']))
    failures = [item for item in items if 'error' in item]
    report = {'schemaVersion': 1, 'query': QUERY,
              'generatedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
              'source': 'cached-metadata' if args.offline else 'live-public-metadata',
              'complete': not failures, 'itemCount': len(items),
              'songPlaybackVerified': False,
              'note': 'Volume video is not an individual song. Disc containers need a verified song index and decoder.',
              'items': items}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({'output': str(args.output), 'items': len(items), 'errors': len(failures),
                      'discItems': sum(any(f['kind'] == 'disc-image' for f in i.get('files', [])) for i in items)}))
    return 1 if failures else 0


if __name__ == '__main__':
    raise SystemExit(main())

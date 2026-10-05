"""Inventory endpoint hosts from DEX/assets without printing credentials or URLs.

Presence in a binary does not prove an endpoint is used or still reachable.
"""
import argparse
import json
import re
import zipfile
from pathlib import Path
from urllib.parse import urlsplit


def hosts(root):
    result = []
    # Include both original archives and decoded APKtool directories.
    candidates = sorted([*root.rglob('*.apk'), *root.rglob('classes*.dex')])
    for path in candidates:
        sources = []
        if path.suffix == '.apk':
            with zipfile.ZipFile(path) as archive:
                for entry in archive.infolist():
                    if re.fullmatch(r'classes\d*\.dex', entry.filename) or (
                            entry.filename.startswith('assets/') and entry.file_size < 1024 * 1024):
                        sources.append((entry.filename, archive.read(entry)))
        else:
            sources.append((path.name, path.read_bytes()))
        found = set()
        for name, data in sources:
            for value in re.findall(rb'https?://[\x21-\x7e]+', data):
                try:
                    hostname = urlsplit(value.decode('ascii')).hostname
                    if hostname and re.fullmatch(r'[a-zA-Z0-9.-]+', hostname):
                        found.add(hostname.lower())
                except ValueError:
                    pass
        if found:
            result.append({'file': str(path.relative_to(root)), 'hosts': sorted(found)})
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('root', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(hosts(args.root), indent=2), encoding='utf-8')

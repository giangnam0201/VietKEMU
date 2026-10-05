"""Copy original home resources with a verifiable map to the decoded APK.

This packages one translated native component, not a complete karaoke product.
"""
import argparse
import hashlib
import json
import re
import shutil
import sqlite3
import xml.etree.ElementTree as ET
from pathlib import Path


def package(decoded, destination):
    destination.mkdir(parents=True, exist_ok=True)
    app = decoded / 'dualkmbox'
    resources = app / 'apktool/res'
    values = {entry.get('name'): entry.text for entry in ET.parse(resources / 'values/dimens.xml').getroot()}
    strings = {entry.get('name'): entry.text for entry in ET.parse(resources / 'values/strings.xml').getroot()}
    strings.update({entry.get('name'): entry.text for entry in ET.parse(resources / 'values-vi/strings.xml').getroot()})
    def dimension(name):
        return float(re.fullmatch(r'([0-9.]+)(?:dip|dp|sp|px)', values[name])[1])
    source = (app / 'java/sources/com/evideo/kmbox/fragment/HomeNewFragment.java').read_text()
    adapter = (app / 'java/sources/com/evideo/kmbox/fragment/HomeNewAdapter.java').read_text()
    router = (app / 'java/sources/com/evideo/kmbox/fragment/FragmentManagerUtil.java').read_text()
    if 'HomeNewFragment homeFragment = new HomeNewFragment()' not in router:
        raise RuntimeError('Cannot establish that the original default screen is HomeNewFragment')
    # Preserve original order, including the original "soudcloud" tag spelling.
    specs = [('singer', 'icon_singer', 'home_singer', 1),
             ('app', 'icon_app_manager', 'app_manage', -1),
             ('mixcloud', 'icon_mixcloud_home', 'Mixcloud', 35),
             ('youtube', 'icon_youtube_home', 'YouTube', 34),
             ('soudcloud', 'icon_soundcloud_home', 'Soundcloud', 36),
             ('more', 'icon_more', 'language_and_type_more', 38)]
    for tag, image, label, fragment in specs:
        if f'R.drawable.{image}' not in source or f'case "{tag}"' not in adapter:
            raise RuntimeError('Original home contract differs: ' + tag)
    assets = ['main_bg.jpg', 'icon_song_name.png'] + [image + '.png' for _,image,_,_ in specs]
    entries = {entry['path']: entry for entry in json.loads((app / 'original-entries.json').read_text())}
    provenance = []
    for name in assets:
        original = resources / 'drawable-mdpi' / name
        relative = original.relative_to(app / 'apktool').as_posix()
        digest = hashlib.sha256(original.read_bytes()).hexdigest()
        # Apktool normalizes qualifier paths (e.g. drawable-mdpi-v4 -> mdpi).
        # Match the actual original entry by name AND bytes, never by a guessed
        # normalized folder or an unchecked fallback copy.
        matches = [entry for entry in entries.values()
                   if Path(entry['path']).name == name and entry['sha256'] == digest]
        if len(matches) != 1:
            raise RuntimeError('Cannot map decoded resource to exactly one original APK entry: ' + relative)
        shutil.copy2(original, destination / name)
        provenance.append({'resource': matches[0]['path'], 'decoded_resource': relative, 'sha256': digest})
    contract = {
        'tiles': [{'tag': tag, 'image': image+'.png', 'text': strings.get(label, label), 'fragment': fragment}
                  for tag,image,label,fragment in specs],
        'songName': strings['home_songname'],
        'tileHeight': dimension('home_item_height'), 'tileWidth': dimension('home_small_item_width'),
        'rowGap': dimension('home_row_tow_margin'), 'columnGap': dimension('home_small_item_margin_left'),
        'textSize': dimension('home_text_size'), 'textBottom': dimension('home_text_margin_bottom'),
        'paddingTop': 64,
        'provenance': 'HomeNewFragment.java; HomeNewAdapter.java; layout_home_fragment.xml; item_home_new_adapter.xml',
    }
    (destination / 'home.json').write_text(json.dumps(contract, ensure_ascii=False, indent=2), encoding='utf-8')
    (destination / 'asset-provenance.json').write_text(json.dumps(provenance, indent=2))
    catalogue = app / 'apktool/assets/wholekmbox.jpg'
    digest = hashlib.sha256(catalogue.read_bytes()).hexdigest()
    if entries['assets/wholekmbox.jpg']['sha256'] != digest:
        raise RuntimeError('Original catalogue bytes changed')
    shutil.copy2(catalogue, destination / 'wholekmbox.db')
    with sqlite3.connect(f'file:{catalogue}?mode=ro', uri=True) as database:
        count = database.execute('SELECT count(*) FROM tblSong').fetchone()[0]
    (destination / 'catalogue-provenance.json').write_text(json.dumps({
        'original_asset': 'assets/wholekmbox.jpg', 'sha256': digest, 'songs': count,
        'original_methods': 'WholeSongDAO.getCount/getSongById/isExist/isOnline/getCountHasRemote',
        'media_availability': 'not established by catalogue metadata'
    }, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('decoded', type=Path)
    parser.add_argument('destination', type=Path)
    args = parser.parse_args()
    package(args.decoded, args.destination)

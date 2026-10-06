"""Copy original home resources with a verifiable map to the decoded APK.

This packages one translated native component, not a complete karaoke product.
"""
import argparse
import hashlib
import json
import re
import shutil
import sqlite3
import subprocess
import zipfile
from html.parser import HTMLParser
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
    package_bottom(app, destination, entries, strings)
    package_more(app, destination, entries, strings, values)
    seed = app / 'apktool/assets/kmbox.jpg'
    seed_digest = hashlib.sha256(seed.read_bytes()).hexdigest()
    if entries['assets/kmbox.jpg']['sha256'] != seed_digest:
        raise RuntimeError('Original local database seed bytes changed')
    shutil.copy2(seed, destination / 'local-seed.db')
    with sqlite3.connect(f'file:{seed}?mode=ro', uri=True) as database:
        seed_count, local_count = database.execute(
            'SELECT count(*), count(CASE WHEN IsLocalExist BETWEEN 1 AND 2 THEN 1 END) FROM tblSong').fetchone()
    (destination / 'local-seed-provenance.json').write_text(json.dumps({
        'original_asset': 'assets/kmbox.jpg', 'sha256': seed_digest,
        'songs': seed_count, 'local_flags': local_count,
        'upgrade_source': 'SongManager.addColumn; DAOHelper.addColumn',
        'media_availability': 'flags are preserved; actual media files still require verification'
    }, indent=2))


def package_more(app, destination, entries, strings, values):
    android = '{http://schemas.android.com/apk/res/android}'
    layout = ET.parse(app / 'apktool/res/layout/layout_more_fragment.xml').getroot()
    def number(value):
        if value.startswith('@dimen/'): value = values[value.split('/')[1]]
        return float(re.fullmatch(r'([0-9.]+)(?:dip|dp|sp|px)', value)[1])
    top = number(layout.get(android+'paddingTop'))
    tiles = []
    positions = {}
    assets = {'icon_back.png'}
    for view in layout:
        if view.tag != 'FrameLayout': continue
        attrs = view.attrib
        identity = attrs[android+'id'].split('/')[-1]
        width, height = number(attrs[android+'layout_width']), number(attrs[android+'layout_height'])
        x = number(attrs.get(android+'layout_marginLeft', '0px'))
        y = number(attrs.get(android+'layout_marginTop', '0px')) + top
        right_of = attrs.get(android+'layout_toRightOf')
        below = attrs.get(android+'layout_below')
        align = attrs.get(android+'layout_alignLeft')
        if right_of:
            reference = positions[right_of.split('/')[-1]]
            x += reference['x'] + reference['width']
        if below:
            reference = positions[below.split('/')[-1]]
            y += reference['y'] + reference['height'] - top
        if align: x = positions[align.split('/')[-1]]['x']
        label = view[0]
        image = attrs[android+'background'].split('/')[-1] + '.png'
        assets.add(image)
        tile = {'id': identity, 'image': image, 'text': strings[label.get(android+'text').split('/')[-1]],
            'x': x, 'y': y, 'width': width, 'height': height,
            'textBottom': number(label.get(android+'layout_marginBottom')),
            'textPadding': number(label.get(android+'paddingLeft', '0px')),
            'multilingual': label.tag.endswith('MultiLanguageTextView'),
            'singleLine': label.get(android+'singleLine') == 'true'}
        tiles.append(tile); positions[identity] = tile
    for name in assets:
        resource = app / 'apktool/res/drawable-mdpi' / name
        digest = hashlib.sha256(resource.read_bytes()).hexdigest()
        if not any(Path(entry['path']).name == name and entry['sha256'] == digest for entry in entries.values()):
            raise RuntimeError('Original More resource differs: ' + name)
        shutil.copy2(resource, destination / name)
    back = layout[-1]
    colors = {entry.get('name'): entry.text for entry in ET.parse(app / 'apktool/res/values/colors.xml').getroot()}
    contract = {'tiles': tiles, 'backX': number(back.get(android+'layout_marginLeft')),
        'backY': top + number(back.get(android+'layout_marginTop')),
        'backWidth': number('@dimen/icon_back_width'), 'backHeight': number('@dimen/icon_back_height'),
        'backCorner': number('@dimen/icon_back_corner'),
        'backStartColor': colors['bg_btn_ok_star'], 'backEndColor': colors['bg_btn_ok_end']}
    (destination / 'more.json').write_text(json.dumps(contract, ensure_ascii=False, indent=2), encoding='utf-8')


class BottomParser(HTMLParser):
    def __init__(self):
        super().__init__()
        self.anchor = None
        self.buttons = []

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == 'a': self.anchor = attrs
        if tag == 'img' and self.anchor and self.anchor.get('data-genre') == '13':
            self.buttons.append({'href': self.anchor['data-href'], 'src': attrs['src'],
                'x': float(self.anchor['data-posx']), 'y': float(self.anchor['data-posy']),
                'width': float(attrs['data-width']), 'height': float(attrs['data-height'])})

    def handle_endtag(self, tag):
        if tag == 'a': self.anchor = None


def package_bottom(app, destination, entries, strings):
    archive = app / 'apktool/assets/default_template.zip'
    if hashlib.sha256(archive.read_bytes()).hexdigest() != entries['assets/default_template.zip']['sha256']:
        raise RuntimeError('Original template archive differs')
    labels = {'home_imv': 'bottom_home', 'ambience_imv': 'bottom_ambience', 'ori_imv': 'bottom_original',
        'accp_imv': 'bottom_accompaniment', 'voldec': 'bottom_voice_del', 'play_imv': 'bottom_play',
        'pause_imv': 'bottom_pause', 'volinc': 'bottom_voice_add', 'replay_imv': 'bottom_replay',
        'cut_song_imv': 'bottom_cutsong', 'order_bg': 'bottom_order'}
    bottom_source = (app / 'java/sources/com/evideo/kmbox/view/menubar/BottomMenuBarView.java').read_text()
    for key in labels.values():
        if f'R.string.{key}' not in bottom_source: raise RuntimeError('Unmapped original bottom label: ' + key)
    parser = BottomParser()
    provenance = []
    with zipfile.ZipFile(archive) as template:
        parser.feed(template.read('module_bottom/index.html').decode('utf-8'))
        for item in parser.buttons:
            original = template.read('module_bottom/' + item['src'])
            local = destination / ('bottom-' + item['src'])
            local.write_bytes(original)
            png = local.with_suffix('.png')
            subprocess.run(['ffmpeg', '-v', 'error', '-y', '-i', str(local), '-frames:v', '1', str(png)], check=True)
            item['image'] = png.name
            item['text'] = strings[labels[item['href']]]
            provenance.append({'archive': 'assets/default_template.zip', 'entry': 'module_bottom/'+item['src'],
                'original_sha256': hashlib.sha256(original).hexdigest(),
                'windows_png_sha256': hashlib.sha256(png.read_bytes()).hexdigest(),
                'conversion': 'lossless PNG representation for Windows WPF'})
    colors = {entry.get('name'): entry.text for entry in ET.parse(app / 'apktool/res/values/colors.xml').getroot()}
    badge = app / 'apktool/res/drawable-mdpi/icon_playlist_num.png'
    digest = hashlib.sha256(badge.read_bytes()).hexdigest()
    if not any(entry['sha256'] == digest and Path(entry['path']).name == badge.name for entry in entries.values()):
        raise RuntimeError('Original queue count badge differs')
    shutil.copy2(badge, destination / badge.name)
    # Values from MenuBarManager, CommonModuleView and image_btn_with_text_lay.
    contract = {'buttons': parser.buttons, 'y': 660, 'moduleTop': 10,
        'imageWidth': 40, 'imageHeight': 32, 'textTop': 5, 'textHeight': 30,
        'textSize': 16, 'textColor': colors['system_singer_name_color']}
    (destination / 'bottom.json').write_text(json.dumps(contract, ensure_ascii=False, indent=2), encoding='utf-8')
    (destination / 'bottom-provenance.json').write_text(json.dumps(provenance, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('decoded', type=Path)
    parser.add_argument('destination', type=Path)
    args = parser.parse_args()
    package(args.decoded, args.destination)

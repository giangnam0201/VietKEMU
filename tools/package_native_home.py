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
import struct
import zipfile
from html.parser import HTMLParser
import xml.etree.ElementTree as ET
from pathlib import Path


def package(decoded, destination, firmware_ui, firmware=None):
    destination.mkdir(parents=True, exist_ok=True)
    package_fonts(firmware_ui, destination)
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
    assets = ['main_bg.jpg', 'icon_song_name.png', 'icon_youtube.png'] + [image + '.png' for _,image,_,_ in specs]
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
    package_top(app, destination, entries, values)
    package_more(app, destination, entries, strings, values)
    package_song_browser(app, destination, entries, strings, values)
    package_song_grid(app, destination, entries)
    package_order_dependencies(decoded, destination, strings)
    package_player_reference(decoded, destination, firmware)
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


def package_order_dependencies(decoded, destination, strings):
    android = '{http://schemas.android.com/apk/res/android}'
    manifests = []
    actions = []
    for manifest in sorted(decoded.glob('*/apktool/AndroidManifest.xml')):
        app = manifest.parent.parent
        root = ET.parse(manifest).getroot()
        entries = {entry['path']: entry for entry in json.loads((app/'original-entries.json').read_text())}
        manifests.append({'app': app.name, 'package': root.get('package'),
            'originalManifestSha256': entries['AndroidManifest.xml']['sha256'],
            'decodedManifestSha256': hashlib.sha256(manifest.read_bytes()).hexdigest()})
        application = root.find('application')
        if application is None: continue
        for component in list(application):
            if component.tag not in ('activity', 'activity-alias', 'service'): continue
            enabled = application.get(android+'enabled', 'true') == 'true' and component.get(android+'enabled', 'true') == 'true'
            for intent in component.findall('intent-filter'):
                for action in intent.findall('action'):
                    actions.append({'package': root.get('package'), 'component': component.get(android+'name'),
                        'kind': component.tag, 'enabledByManifest': enabled,
                        'action': action.get(android+'name'), 'hasData': bool(intent.findall('data'))})
    if len(manifests) != 23:
        raise RuntimeError(f'Expected all 23 decoded firmware APK manifests, found {len(manifests)}')
    names = ['try_to_connect_incognito', 'try_to_connect_incognito_error', 'order_song_no_disk_tip',
        'order_song_num_max_tip', 'order_song_no_net_tip', 'add_song_from_nas_error']
    contract = {'manifests': manifests, 'actions': actions, 'feedback': {name: strings[name] for name in names},
        'scope': 'Firmware manifest declarations; runtime installed/enabled state and plugin execution are separate.'}
    (destination/'order-dependencies.json').write_text(json.dumps(contract, ensure_ascii=False, indent=2), encoding='utf-8')


def package_song_grid(app, destination, entries):
    names = ['icon_song_default', 'icon_online_bg', 'preview_dialog_button',
             'button_add_song_item_collect', 'button_add_song_item_collect_selected',
             'button_add_song_item_collected_normal', 'button_add_song_item_collected_select',
             'ic_top_song', 'ic_top_song_press']
    icons = {}
    for name in names:
        matches = [path for path in (app / 'apktool/res').glob('drawable*/*')
                   if path.stem == name and path.suffix in ('.png', '.webp')]
        if len(matches) != 1: raise RuntimeError('Ambiguous original grid bitmap: ' + name)
        resource = matches[0]
        digest = hashlib.sha256(resource.read_bytes()).hexdigest()
        original = [entry for entry in entries.values()
                    if Path(entry['path']).name == resource.name and entry['sha256'] == digest]
        if len(original) != 1: raise RuntimeError('Unverified original grid bitmap: ' + name)
        output = destination / ('grid-' + name + '.png')
        if resource.suffix == '.webp':
            subprocess.run(['ffmpeg', '-v', 'error', '-y', '-i', str(resource), '-frames:v', '1', str(output)], check=True)
        else: shutil.copy2(resource, output)
        width, height = struct.unpack('>II', output.read_bytes()[16:24])
        density = 1.5 if '-hdpi' in resource.parent.name else 1
        icons[name] = {'file': output.name, 'width': width / density, 'height': height / density,
                       'originalResource': original[0]['path'], 'originalSha256': digest,
                       'pngSha256': hashlib.sha256(output.read_bytes()).hexdigest()}
    contract = {'icons': icons,
                'provenance': 'fragment_song_recycler_gridview_item.xml; SongRecyclerGridViewAdapter; StrokeTextView; GlideUtil'}
    (destination / 'song-grid.json').write_text(json.dumps(contract, ensure_ascii=False, indent=2), encoding='utf-8')


def package_song_browser(app, destination, entries, strings, values):
    def dim(name):
        return float(re.fullmatch(r'([0-9.]+)(?:dip|dp|sp|px)', values[name])[1])
    java = app / 'java/sources'
    config = (java / 'com/evideo/kmbox/KmConfig.java').read_text()
    if 'KEY_SEARCH_PANEL_SUPPORT_ALL_ID = "5,10,2"' not in config:
        raise RuntimeError('Original default keyboard modes changed')
    # JADX prints named library constants for the phantom dimensions. Resolve
    # their definitions from the same decode rather than guessing their values.
    main = (java / 'com/evideo/kmbox/activity/MainActivity.java').read_text()
    def constant(class_name, field):
        imported = re.search(r'import ([\w.]+\.' + class_name + r');', main)[1]
        source = (java / (imported.replace('.', '/') + '.java')).read_text()
        return int(re.search(r'\b' + field + r'\s*=\s*(\d+)\s*;', source)[1])
    phantom_width = constant('NNTPReply', 'POSTING_NOT_ALLOWED')
    phantom_height = constant('TelnetCommand', 'GA')
    panel_utils = (java / 'com/evideo/spellpanel/retrieve/PanelUtils.java').read_text()
    replacements = {'sysSettingAboutView.VERSION_RE': 'V', 'DCCheckCodeCommu.TYPE_EMAIL_UNBIND': '8',
                    'Marker.ANY_MARKER': '*', 'Marker.ANY_NON_NULL_MARKER': '+'}
    # Resolve the constants from their original decoded classes before using
    # the replacement text in PanelUtils' original arrays.
    for reference, expected in replacements.items():
        class_name, field = reference.split('.')
        imported = re.search(r'import ([\w.]+\.' + class_name + r');', panel_utils)[1]
        source = (java / (imported.replace('.', '/') + '.java')).read_text()
        if re.search(r'\b' + field + r'\s*=\s*"([^"]*)"\s*;', source)[1] != expected:
            raise RuntimeError('Original keyboard character constant changed: ' + reference)
    def letters(field):
        body = re.search(r'\b' + field + r'\s*=\s*\{([^}]+)\}', panel_utils)[1]
        for reference, value in replacements.items(): body = body.replace(reference, json.dumps(value))
        return json.loads('[' + body + ']')
    icons = ['search_keyboard_back.png', 'icon_pen.png', 'keyboard_earth.png']
    provenance = []
    for name in icons:
        resource = app / 'apktool/res/drawable-mdpi' / name
        digest = hashlib.sha256(resource.read_bytes()).hexdigest()
        matches = [entry for entry in entries.values()
                   if Path(entry['path']).name == name and entry['sha256'] == digest]
        if len(matches) != 1: raise RuntimeError('Original keyboard icon mapping differs: ' + name)
        shutil.copy2(resource, destination / name)
        provenance.append({'resource': matches[0]['path'], 'sha256': digest})
    contract = {
        'title': strings['home_middle_songname'], 'emptyMessage': strings['song_to_youtube_tip'],
        'youtubeText': strings['to_youtube'], 'hint': strings['spell_hint_input'],
        'clearText': strings['spell_input_clear'],
        'containerX': dim('song_name_grid_view_margin_left'), 'containerY': dim('song_name_grid_view_margin_top'),
        'containerWidth': dim('song_name_grid_view_width'), 'containerHeight': dim('song_name_grid_view_height'),
        'categoryHeight': dim('categort_view_item_height'),
        'backX': dim('view_song_recycle_back_btn_margin_right'), 'backY': dim('song_name_btn_back_margin_top'),
        'keyboardWidth': dim('search_input_panel_view_width'), 'keyboardY': dim('search_spell_view_margintop'),
        'phantomWidth': phantom_width, 'phantomHeight': phantom_height,
        'keyboardHeight': dim('search_input_panel_view_height'),
        'keyRowHeight': dim('spell_keyboard_item_height'), 'keyGap': dim('spell_keyboard_item_spacing'),
        'keyTextSize': dim('spell_letter_text_size'),
        'alphabetLetters': letters('computerLetters'), 'symbolLetters': letters('numberAndSympols'),
        'provenance': 'SongNameFragment; BaseSongForGridViewFragment; CategoryHomeView; activity_main.xml; '
                      'view_song_name_vertical_scroll.xml; SearchInputKeyboardView; YueNanFirstSpellPanel; '
                      'AllKeyboardWithSoftPanel; PanelUtils'
    }
    (destination / 'song-browser.json').write_text(json.dumps(contract, ensure_ascii=False, indent=2), encoding='utf-8')
    (destination / 'song-browser-provenance.json').write_text(json.dumps(provenance, indent=2))


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


class TopParser(HTMLParser):
    def __init__(self):
        super().__init__()
        self.anchor = None
        self.items = []

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == 'a': self.anchor = attrs
        if tag == 'img' and self.anchor:
            self.items.append({'href': self.anchor.get('data-href', ''), 'src': attrs['src'],
                'x': float(self.anchor['data-posx']), 'y': float(self.anchor['data-posy']),
                'width': float(attrs['data-width']), 'height': float(attrs['data-height'])})

    def handle_endtag(self, tag):
        if tag == 'a': self.anchor = None


def package_top(app, destination, entries, values):
    archive = app / 'apktool/assets/default_template.zip'
    if hashlib.sha256(archive.read_bytes()).hexdigest() != entries['assets/default_template.zip']['sha256']:
        raise RuntimeError('Original top template archive differs')
    parser = TopParser()
    provenance = []
    with zipfile.ZipFile(archive) as template:
        parser.feed(template.read('module_top/index.html').decode('utf-8'))
        for item in parser.items:
            entry = 'module_top/' + item['src']
            original = template.read(entry)
            local = destination / ('top-' + item['src'])
            local.write_bytes(original)
            png = destination / ('top-' + Path(item['src']).stem + '.png')
            if png != local:
                subprocess.run(['ffmpeg', '-v', 'error', '-y', '-i', str(local), '-frames:v', '1', str(png)], check=True)
            item['image'] = png.name
            provenance.append({'archive': 'assets/default_template.zip', 'entry': entry,
                'original_sha256': hashlib.sha256(original).hexdigest(),
                'windows_png_sha256': hashlib.sha256(png.read_bytes()).hexdigest()})
    earth = app / 'apktool/res/drawable-mdpi/keyboard_earth.png'
    digest = hashlib.sha256(earth.read_bytes()).hexdigest()
    if not any(entry['sha256'] == digest and Path(entry['path']).name == earth.name for entry in entries.values()):
        raise RuntimeError('Original language icon differs')
    shutil.copy2(earth, destination / earth.name)
    def dim(name):
        return float(re.fullmatch(r'([0-9.]+)(?:dip|dp|sp|px)', values[name])[1])
    contract = {'items': parser.items, 'dynamicWidth': dim('auto_adaption_layout_width'),
        'commonHeight': dim('auto_adaption_view_common_size'),
        'iconLeft': dim('auto_adaption_view_icon_margin_left'),
        'textLeft': dim('auto_adaption_view_text_margin_left'),
        'languageWidth': dim('top_menu_change_language_btn_width'),
        'languageHeight': dim('top_menu_change_language_btn_height'),
        'languageTop': dim('top_menu_change_language_top_margin'),
        'downloadedLogoWidth': dim('top_logo_width'), 'downloadedLogoHeight': dim('top_logo_height'),
        'branding': 'Bundled VietK logo retained; server touch.png can override it through ui_request_logo_url_list.',
        'scope': 'Landscape header; service-driven controls and animated playing indicator remain incomplete.'}
    (destination / 'top.json').write_text(json.dumps(contract, ensure_ascii=False, indent=2), encoding='utf-8')
    (destination / 'top-provenance.json').write_text(json.dumps(provenance, indent=2))


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


def package_player_reference(decoded, destination, firmware):
    app = decoded / 'dualkmbox'
    entries = {entry['path']: entry for entry in json.loads((app / 'original-entries.json').read_text())}
    original = app / 'apktool/assets/grade_video.mp4'
    if original.exists():
        payload = original.read_bytes()
    else:
        apk = next(firmware.glob('vendor/app/dualkmbox/*.apk'))
        with zipfile.ZipFile(apk) as archive:
            payload = archive.read('assets/grade_video.mp4')
    digest = hashlib.sha256(payload).hexdigest()
    if entries['assets/grade_video.mp4']['sha256'] != digest:
        raise RuntimeError('Original TV reference video differs')
    output = destination / 'player'
    output.mkdir(exist_ok=True)
    (output / original.name).write_bytes(payload)
    # Idle broadcasts live on device/USB storage; do not substitute the scoring
    # clip if the original Demo.mp4 was not included in the firmware export.
    demos = [path for path in firmware.rglob('*') if path.is_file() and path.name.lower() == 'demo.mp4']
    demo_record = {'available': bool(demos), 'originalStoragePath': '/kmbox/video/Demo.mp4'}
    if demos:
        demo = demos[0]
        shutil.copy2(demo, output / 'Demo.mp4')
        demo_record.update({'source': str(demo.relative_to(firmware)), 'sha256': hashlib.sha256(demo.read_bytes()).hexdigest()})
    (output / 'idle-demo.json').write_text(json.dumps(demo_record, indent=2))
    shutil.copy2(decoded / 'daulkmboxosdtv/apktool/res/layout/activity_osd.xml', output / 'activity_osd.xml')
    tv = decoded / 'daulkmboxosdtv'
    tv_entries = json.loads((tv / 'original-entries.json').read_text())
    records = []
    for name in ('play', 'pause', 'replay', 'original', 'accompany', 'play_ctrl_audio_bg'):
        candidates = sorted((tv / 'apktool/res').glob('drawable*/' + name + '.png'))
        if not candidates:
            raise RuntimeError('Original TV control image missing: ' + name)
        image = candidates[0]
        image_hash = hashlib.sha256(image.read_bytes()).hexdigest()
        matches = [item for item in tv_entries if Path(item['path']).name == image.name and item['sha256'] == image_hash]
        if not matches:
            raise RuntimeError('Original TV control image provenance differs: ' + name)
        shutil.copy2(image, output / (name + '.png'))
        records.append({'file': name + '.png', 'sha256': image_hash, 'original': matches[0]['path']})
    (output / 'osd-provenance.json').write_text(json.dumps(records, indent=2))
    tv_dimensions = {item.get('name'): item.text for item in ET.parse(tv / 'apktool/res/values/dimens.xml').getroot()}
    def tv_dimension(name):
        return float(re.sub(r'(dip|dp|px|sp)$', '', tv_dimensions[name]))
    (output / 'osd.json').write_text(json.dumps({
        'controlWidth': tv_dimension('osd_tv_play_ctrl_width'),
        'controlHeight': tv_dimension('osd_tv_play_ctrl_height'),
        'controlY': tv_dimension('osd_tv_play_ctrl_margin_top'),
        'numberY': tv_dimension('osd_tv_play_ctrl_number_margin_top'),
        'numberSize': tv_dimension('osd_tv_play_ctrl_text_size'),
        'timeoutMs': 6000,
        'provenance': 'km_msg_osdtv.xml; KmOSDMessageView; KmConfig.IntonationConfig.DOWNCOUNT_BEGINTIME'
    }, indent=2))
    (output / 'provenance.json').write_text(json.dumps({
        'app': app.name, 'asset': 'assets/grade_video.mp4', 'sha256': digest,
        'scope': 'Original grading video retained for actual Windows decode verification and eventual grading UI; not a karaoke song library.'}, indent=2))


def package_fonts(source, destination):
    records = {item['path']: item for item in json.loads((source / 'provenance.json').read_text())}
    font_config = source / 'system/etc/fonts.xml'
    config_hash = hashlib.sha256(font_config.read_bytes()).hexdigest()
    if records['system/etc/fonts.xml']['sha256'] != config_hash:
        raise RuntimeError('Supplied original font configuration differs')
    family = ET.parse(font_config).getroot().find("family[@name='sans-serif']")
    if family is None:
        raise RuntimeError('Cannot establish original default font')
    output = destination / 'fonts'
    output.mkdir(exist_ok=True)
    provenance = []
    for font in family.findall('font'):
        name = font.text.strip()
        if not name.startswith('Roboto-'):
            raise RuntimeError('Unexpected original sans-serif font: ' + name)
        path = 'system/fonts/' + name
        payload = (source / path).read_bytes()
        if hashlib.sha256(payload).hexdigest() != records[path]['sha256']:
            raise RuntimeError('Supplied original font bytes differ: ' + name)
        (output / name).write_bytes(payload)
        provenance.append(records[path])
    (destination / 'font-provenance.json').write_text(json.dumps({
        'configuration': 'system/etc/fonts.xml', 'configurationSha256': config_hash,
        'sourceRun': 37417309368, 'fonts': provenance,
        'scope': 'Original Roboto glyphs; Android vs WPF text shaping and padding still need visual comparison.'}, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('decoded', type=Path)
    parser.add_argument('destination', type=Path)
    parser.add_argument('--firmware-ui', type=Path, required=True)
    parser.add_argument('--firmware', type=Path)
    args = parser.parse_args()
    package(args.decoded, args.destination, args.firmware_ui, args.firmware)

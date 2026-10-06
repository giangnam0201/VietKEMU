"""Package only original ambience/idle resources, with APK provenance."""
import hashlib
import io
import json
import re
import sys
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path
from loguru import logger
from androguard.core.axml import AXMLPrinter, ARSCParser

logger.remove()
panel_path, tv_path, references, destination = map(Path, sys.argv[1:5])
panel = zipfile.ZipFile(panel_path)
tv = zipfile.ZipFile(tv_path)
sdcard = zipfile.ZipFile(io.BytesIO(panel.read('assets/sdcard.zip')))
values = references/'res'
strings = {e.get('name'): e.text or '' for e in ET.parse(values/'values/strings.xml').getroot()}
strings.update({e.get('name'): e.text or '' for e in ET.parse(values/'values-vi/strings.xml').getroot()})
dimensions = {e.get('name'): float(re.sub(r'(dip|dp|px|sp)$', '', e.text)) for e in ET.parse(values/'values/dimens.xml').getroot() if e.tag == 'dimen' and not e.text.startswith('@') and '%' not in e.text}
colors = {e.get('name'): e.text for e in ET.parse(values/'values/colors.xml').getroot()}
records = []
files = {}

def add(bundle, entry, name, origin):
    payload = bundle.read(entry)
    files[name] = payload
    records.append({'file': name, 'source': origin+'/'+entry, 'sha256': hashlib.sha256(payload).hexdigest()})

names = ['memeda','xianhua','zan','baodeng','wuyafeiguo','zajidan','birthday','zaiyiqi']
for name in names:
    for extension in ['png','wav']:
        add(sdcard, f'kmbox/ambience/{name}.{extension}', f'ambience/{name}.{extension}', 'dualkmbox.apk/assets/sdcard.zip')
add(sdcard, 'kmbox/resource/60003950.mp4', 'player/60003950.mp4', 'dualkmbox.apk/assets/sdcard.zip')
for name in ['dc_overseas_popup_close','dc_overseas_set_on','dc_overseas_set_off','ambience_barrage_item_line']:
    add(panel, f'res/drawable-mdpi-v4/{name}.png', f'ambience/{name}.png', 'dualkmbox.apk')
for name in ['ic_cut_song','ic_cut_song_press','ic_delete','ic_delete_press','ic_top_song','ic_top_song_press',
             'icon_youtube','play_list_select','play_list_select_light','selected_list_clear_all',
             'selected_list_shuffle','selected_song_playing','play_list_sung','play_list_sung_light',
             'selected_item_download_bg','selected_item_download_progress']:
    add(panel, f'res/drawable-mdpi-v4/{name}.png', f'ambience/playlist/{name}.png', 'dualkmbox.apk')
for name in ['barrage_ellipse','barrage_rocket']:
    add(tv, f'res/drawable-tvdpi-v4/{name}.png', f'ambience/{name}.png', 'daulkmboxosdtv.apk')
avatar = next(n for n in tv.namelist() if Path(n).name == 'osd_local_defaultfig.png')
add(tv, avatar, 'ambience/osd_local_defaultfig.png', 'daulkmboxosdtv.apk')
for name in ['dialog_ambience_view','ambience_expression_view','ambience_barrage_view','ambience_tv_screen_view']:
    files[f'ambience/reference/{name}.xml'] = AXMLPrinter(panel.read(f'res/layout/{name}.xml')).get_xml()
for name in ['common_dialog_bg2','btn_normal_bg','spell_full_pop_bg','selector_common_dialog_ok_btn']:
    files[f'ambience/reference/{name}.xml'] = AXMLPrinter(panel.read(f'res/drawable/{name}.xml')).get_xml()
table = ARSCParser(tv.read('resources.arsc'))
package = table.get_packages_names()[0]
def tv_dimension(name):
    candidates = table.get_resolved_res_configs(table.get_res_id_by_key(package,'dimen',name))
    value = next(v for c,v in candidates if c.get_qualifier() == 'tvdpi')
    return float(re.sub(r'(dip|dp|px|sp)$','',value))

contract = {'strings': {key: strings[key] for key in ['ambience_expression','ambience_barrage','ambience_tv',
    'barrage_view_send_content','btn_send','disco_song_mask_text','disco_song_mask_tip_text',*names]},
    'dimensions': {key: value for key,value in dimensions.items() if key.startswith(('ambience_','barrage_','expression_','dialog_ambience_')) or key in
    ['setting_net_wifi_btn_width','setting_net_wifi_btn_height','sys_general_item_title_size','icon_back_corner','btn_text_size']},
    'colors': {key: colors[key] for key in ['common_dialog_bg','common_btn_bg_start','common_btn_bg_end','barrage_input_text']},
    'expressions': names,
    'television': {key:tv_dimension(key) for key in ['danmaku_show_view_width','danmaku_show_view_height',
    'danmaku_show_view_bg_margin_left','danmaku_show_view_bg_margin_top','danmaku_show_view_rocket_margin_left',
    'danmaku_show_view_rocket_margin_top','danmaku_show_view_text_margin_left','danmaku_show_view_text_margin_top',
    'danmaku_show_view_text_size','osd_tv_gif_width','osd_tv_gif_height']},
    'expressionTimeoutMs':6000,'barrageDelayMs':1200,'barrageMaxLength':30,'barrageMaximumLines':10,
    'dialogOffsetX':-95,'initialTab':10,
    'sources':['AmbienceDialog.java','SendExpressionView.java','SendBarrageView.java','DiscoMaskView.java',
    'KmOSDMessageView.java','BarrageManager.java','OuterCommonPresentation.java']}
files['ambience/contract.json'] = json.dumps(contract,ensure_ascii=False,indent=2).encode('utf-8')
files['ambience/provenance.json'] = json.dumps(records,indent=2).encode('utf-8')
destination.parent.mkdir(parents=True,exist_ok=True)
with zipfile.ZipFile(destination,'w',zipfile.ZIP_DEFLATED) as output:
    for name, payload in files.items():
        output.writestr(name,payload)
print(json.dumps({'files':len(files),'bytes':destination.stat().st_size,'originalIdleVideoRecovered':True}))

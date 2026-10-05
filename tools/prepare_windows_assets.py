"""Copy original database and template assets into the private Windows build."""
import argparse
import hashlib
import io
import json
import zipfile
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--apk', type=Path, required=True)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(args.apk) as apk:
    database = apk.read('assets/wholekmbox.jpg')
    (args.output / 'catalog.db').write_bytes(database)
    with zipfile.ZipFile(io.BytesIO(apk.read('assets/default_template.zip'))) as template:
        for entry in template.infolist():
            if not entry.filename.startswith(('module_top/', 'module_bottom/')) or entry.is_dir():
                continue
            target = (args.output / 'template' / entry.filename).resolve()
            if not target.is_relative_to(args.output.resolve()):
                raise ValueError('Invalid template path')
            target.parent.mkdir(parents=True, exist_ok=True)
            data = template.read(entry)
            if target.suffix == '.html':
                bridge = '''<style>body{margin:0;overflow:hidden}img{width:attr(data-width px);height:attr(data-height px)}</style><script>
                document.querySelectorAll('img[data-width]').forEach(i=>{i.style.width=i.dataset.width+'px';i.style.height=i.dataset.height+'px'});
                document.querySelectorAll('[data-href="accp_imv"],[data-href="pause_imv"]').forEach(i=>i.style.display='none');
                document.querySelectorAll('a').forEach(a=>a.onclick=e=>{e.preventDefault();if(a.dataset.href)parent.postMessage({action:a.dataset.href},location.origin)});
                addEventListener('message',e=>{if(e.origin!==location.origin||e.source!==parent||e.data.type!=='state')return;for(const [key,on]of Object.entries({play_imv:!e.data.playing,pause_imv:e.data.playing,ori_imv:e.data.vocal,accp_imv:!e.data.vocal})){let a=document.querySelector('[data-href="'+key+'"]');if(a)a.style.display=on?'':'none'}});
                </script>'''
                data += bridge.encode()
            target.write_bytes(data)
    (args.output / 'provenance.json').write_text(json.dumps({'source':args.apk.name,'catalogSha256':hashlib.sha256(database).hexdigest(),'originalTemplate':'assets/default_template.zip','port':'Windows native host; original assets; feature parity incomplete'},indent=2))

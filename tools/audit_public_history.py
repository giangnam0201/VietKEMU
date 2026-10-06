"""Read-only public-release privacy audit; never prints secret values."""
import hashlib
import json
import re
import subprocess

rules={
    'github_credential':rb'\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,})\b',
    'private_key':rb'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----',
    'device_chip_id':rb'\b0a[0-9a-fA-F]{16}\b',
    'device_serial':rb'\b(?:893L[A-Z0-9]{8,}|F1[8SA][A-Z0-9]{8,})\b',
    'mac_address':rb'\b(?:[0-9a-fA-F]{2}:){5}[0-9a-fA-F]{2}\b',
}
objects=subprocess.check_output(['git','rev-list','--objects','--all'],text=True).splitlines()
findings=[];scanned=0
for entry in objects:
    object_id,_,path=entry.partition(' ')
    if not path or subprocess.check_output(['git','cat-file','-t',object_id],text=True).strip()!='blob':
        continue
    payload=subprocess.check_output(['git','cat-file','blob',object_id]);scanned+=1
    for kind,pattern in rules.items():
        for match in re.finditer(pattern,payload):
            value=match.group().lower()
            # Synthetic locally-administered MACs used by transport fixtures.
            if kind=='mac_address' and value.startswith((b'02:',b'00:')):
                continue
            findings.append({'path':path,'object':object_id[:12],'kind':kind,
                'valueHash':hashlib.sha256(value).hexdigest()[:12],
                'line':payload[:match.start()].count(b'\n')+1})
print(json.dumps({'blobsScanned':scanned,'findings':findings},indent=2))

"""Probe original login with operator-supplied real identity; never invent tokens.

Identity/session files stay local under ignored artifacts. Print only a redacted
result. This is a protocol diagnostic, not an authentication bypass or song list.
"""
import argparse
import hashlib
import json
import urllib.parse
import urllib.request
from pathlib import Path


def signature(chip, salt):
    result = bytearray()
    utf16 = (chip + ':' + salt).encode('utf-16-be', errors='surrogatepass')
    for offset in range(0, len(utf16), 2):
        c = int.from_bytes(utf16[offset:offset+2], 'big')
        if 0 < c < 128:
            result.append(c)
        elif c < 2048:
            result.extend((0xc0 | (c >> 6), 0x80 | (c & 63)))
        else:
            result.extend((0xe0 | (c >> 12), 0x80 | ((c >> 6) & 63), 0x80 | (c & 63)))
    return hashlib.md5(result[:63]).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('identity', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('--song-id', type=int)
    parser.add_argument('--cloud-status', action='store_true', help='Read original cloud-library lock status; never unlock or bind')
    args = parser.parse_args()
    config = json.loads(args.identity.read_text(encoding='utf-8-sig'))
    chip, mac = config['ChipId'], config['Mac']
    if not chip or not mac:
        raise ValueError('Real identity is required')
    args.output.mkdir(parents=True, exist_ok=True)
    body = json.dumps({'cmdid': 'bs_device_login', 'chipid': chip, 'mac': mac}, separators=(',', ':'), ensure_ascii=False)
    headers = {'Accept-Charset': 'utf8', 'Accept-Encoding': '', 'Accept': 'text/json',
               'doubledecode': '', 'User-Agent': config['UserAgent'], 'sessionid': '',
               'validcode': '', 'devicetag': chip, 'signversion': '1.0', 'sign': signature(chip, 'bs_device_login')}
    request = urllib.request.Request(config['LoginUrl'], urllib.parse.urlencode({'body': body}).encode(), headers)
    with urllib.request.urlopen(request, timeout=10) as response:
        reply = json.loads(response.read())
    # A private local session record permits later diagnostics; never printed.
    (args.output / 'login-session.private.json').write_text(json.dumps(reply), encoding='utf-8')
    accepted = bool(reply.get('validatecode') and reply.get('serverip') and reply.get('token'))
    message = str(reply.get('errormessage', '')).replace(chip, '[device]').replace(mac, '[MAC]')
    result = {'identitySource': 'actual Windows BIOS serial and physical network adapter',
              'loginAccepted': accepted, 'errorcode': reply.get('errorcode', ''),
              'errormessage': message,
              'hasValidationCode': bool(reply.get('validatecode')),
              'hasServiceUrl': bool(reply.get('serverip')), 'hasToken': bool(reply.get('token')),
              'scope': 'One original login request; no fabricated identity or token. No music download claim.'}
    (args.output / 'login-result.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps(result, ensure_ascii=False))
    if (args.song_id is None and not args.cloud_status) or not accepted:
        return
    # Same approved host and original media command; the normal server token
    # returned by login supplies the signature salt.
    service = reply['serverip']
    if urllib.parse.urlsplit(service).hostname != urllib.parse.urlsplit(config['LoginUrl']).hostname:
        raise ValueError('Returned service is a different host; inspect before forwarding identity')
    headers['validcode'] = str(reply['validatecode'])
    headers['sign'] = signature(chip, str(reply['token']))
    if args.cloud_status:
        body = json.dumps({'cmdid': 'os_unlock_cloud_information'}, separators=(',', ':'))
        request = urllib.request.Request(service, urllib.parse.urlencode({'body': body}).encode(), headers)
        with urllib.request.urlopen(request, timeout=10) as response:
            status = json.loads(response.read())
        (args.output / 'cloud-status.private.json').write_text(json.dumps(status), encoding='utf-8')
        message = str(status.get('errormessage', ''))
        for secret in (chip, mac, str(reply['token']), str(reply['validatecode'])):
            if secret:
                message = message.replace(secret, '[redacted]')
        status_result = {'command': 'os_unlock_cloud_information',
                         'errorcode': status.get('errorcode', ''),
                         'errormessage': message,
                         'hasLockStatus': 'is_unlock' in status,
                         'isUnlock': status.get('is_unlock'),
                         'scope': 'Read-only original status request. No unlock, account binding or registration performed.'}
        (args.output / 'cloud-status-result.json').write_text(json.dumps(status_result, indent=2), encoding='utf-8')
        print(json.dumps(status_result))
    if args.song_id is None:
        return
    body = json.dumps({'cmdid': 'sn_song_media_list', 'songid': args.song_id, 'mac': mac, 'token': ''}, separators=(',', ':'))
    request = urllib.request.Request(service, urllib.parse.urlencode({'body': body}).encode(), headers)
    with urllib.request.urlopen(request, timeout=10) as response:
        media = json.loads(response.read())
        http_status = response.status
        redirected = response.geturl() != service
    (args.output / 'media-response.private.json').write_text(json.dumps(media), encoding='utf-8')
    items = media.get('medialist') or []
    message = str(media.get('errormessage', ''))
    for secret in (chip, mac, str(reply['token']), str(reply['validatecode'])):
        if secret:
            message = message.replace(secret, '[redacted]')
    media_result = {'songId': args.song_id, 'httpStatus': http_status,
                    'redirected': redirected, 'errorcode': media.get('errorcode', ''),
                    'errormessage': message, 'hasMediaList': 'medialist' in media,
                    'mediaEntries': len(items), 'downloadCompleted': False,
                    'scope': 'A successful login or errorcode 0 alone does not establish media access.'}
    if items:
        url = items[0]['url'].replace('%2F', '/').replace('?attname=', '')
        scheme = urllib.parse.urlsplit(url).scheme
        if scheme not in ('http', 'https'):
            raise ValueError('Returned media is not an HTTP file')
        suffix = '.mp3' if '.mp3' in url else '.ts'
        destination = args.output / (str(args.song_id) + suffix)
        temporary = destination.with_suffix(destination.suffix + '.tmp')
        with urllib.request.urlopen(url, timeout=10) as response:
            total = int(response.headers.get('Content-Length', 0))
            if total <= 0:
                raise ValueError('Media has no valid file length')
            count = 0
            with temporary.open('wb') as stream:
                while block := response.read(65536):
                    stream.write(block)
                    count += len(block)
        if count != total:
            raise ValueError('Incomplete music download')
        temporary.replace(destination)
        with destination.open('rb') as stream:
            prefix = stream.read(512)
        media_result.update(downloadCompleted=True, bytes=count,
                            file=str(destination.resolve()),
                            prefixHex=prefix[:16].hex(),
                            originalTrack=media.get('origininfo'), accompanimentTrack=media.get('accompanyinfo'),
                            defaultVolume=media.get('vol'))
    (args.output / 'media-result.json').write_text(json.dumps(media_result, indent=2), encoding='utf-8')
    print(json.dumps(media_result, ensure_ascii=False))


if __name__ == '__main__':
    main()

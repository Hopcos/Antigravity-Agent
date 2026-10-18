import urllib.request
import ssl
import json

ctx = ssl.create_default_context()
ctx.check_hostname = False
ctx.verify_mode = ssl.CERT_NONE

csrf = 'e2052f7b-f313-436e-b076-de65aa786b85'
headers = {
    'Content-Type': 'application/json',
    'x-codeium-csrf-token': csrf,
    'Connect-Protocol-Version': '1'
}

endpoints = [
    '/exa.language_server_pb.LanguageServerService/GetAuthStatus',
    '/exa.language_server_pb.LanguageServerService/GetLocalUserInfo',
    '/exa.language_server_pb.LanguageServerService/GetLoadCodeAssist',
    '/exa.language_server_pb.LanguageServerService/GetStatus',
]

for ep in endpoints:
    url = f'https://127.0.0.1:17709{ep}'
    try:
        req = urllib.request.Request(url, data=b'{}', headers=headers, method='POST')
        with urllib.request.urlopen(req, context=ctx) as r:
            print(f'=== {ep} SUCCESS ({r.status}) ===')
            res = json.loads(r.read().decode('utf-8'))
            print(json.dumps(res, indent=2)[:500])
    except urllib.error.HTTPError as e:
        print(f'=== {ep} ERROR {e.code} ===', e.read().decode('utf-8')[:300])
    except Exception as e:
        print(f'=== {ep} FAILED ===', e)

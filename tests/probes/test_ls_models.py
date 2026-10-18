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

url = 'https://127.0.0.1:17709/exa.language_server_pb.LanguageServerService/GetAvailableModels'
req = urllib.request.Request(url, data=b'{}', headers=headers, method='POST')
with urllib.request.urlopen(req, context=ctx) as r:
    data = json.loads(r.read().decode('utf-8'))
    models = data['response']['models']
    print(f'Total models: {len(models)}')
    for k, v in models.items():
        m_name = v.get('model')
        quota = v.get('quotaInfo', {}).get('remainingFraction')
        max_tok = v.get('maxTokens')
        print(f"Key: {k:20} Model: {str(m_name):30} MaxTokens: {str(max_tok):8} Quota: {quota}")

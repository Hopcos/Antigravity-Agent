import urllib.request
import urllib.parse
import json
import urllib.error

creds = json.load(open(r'C:\Users\cz\.gemini\oauth_creds.json', 'r', encoding='utf-8'))
token_data = urllib.parse.urlencode({
    'client_id': '681255809395-oo8ft2oprdrnp9e3aqf6av3hmdib135j.apps.googleusercontent.com',
    'client_secret': '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',
    'refresh_token': creds['refresh_token'],
    'grant_type': 'refresh_token'
}).encode('utf-8')

req = urllib.request.Request('https://oauth2.googleapis.com/token', data=token_data, method='POST')
access_token = json.loads(urllib.request.urlopen(req).read().decode('utf-8'))['access_token']

headers = {
    'Authorization': 'Bearer ' + access_token,
    'Content-Type': 'application/json',
    'User-Agent': 'antigravity/2.24.0 (win32; x64)'
}

test_payloads = [
    {'project': '', 'entitlement': {'user_tier': 'free-tier'}},
    {'project': 'aicode-consumers', 'entitlement': {'user_tier': 'free-tier'}},
    {'project': '', 'entitlement': {'user_tier': 'standard-tier'}},
    {'entitlement': {'user_tier': 'free-tier'}},
]

for p in test_payloads:
    try:
        url = 'https://daily-cloudcode-pa.googleapis.com/v1internal:fetchAvailableModels'
        m_req = urllib.request.Request(url, data=json.dumps(p).encode('utf-8'), headers=headers, method='POST')
        with urllib.request.urlopen(m_req) as m_resp:
            res = json.loads(m_resp.read().decode('utf-8'))
            print('SUCCESS with payload:', p)
            print(json.dumps(res, indent=2)[:500])
            break
    except urllib.error.HTTPError as e:
        err_body = e.read().decode('utf-8', errors='ignore')
        print(f'Payload {p} returned {e.code}: {err_body[:200]}')

import urllib.request
import urllib.parse
import json
import urllib.error

creds = json.load(open(r'C:\Users\cz\.gemini\oauth_creds.json', 'r', encoding='utf-8'))
refresh_token = creds['refresh_token']
client_id = '681255809395-oo8ft2oprdrnp9e3aqf6av3hmdib135j.apps.googleusercontent.com'
client_secret = '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'

token_data = urllib.parse.urlencode({
    'client_id': client_id,
    'client_secret': client_secret,
    'refresh_token': refresh_token,
    'grant_type': 'refresh_token'
}).encode('utf-8')

req = urllib.request.Request('https://oauth2.googleapis.com/token', data=token_data, method='POST')
with urllib.request.urlopen(req) as resp:
    token_json = json.loads(resp.read().decode('utf-8'))
    access_token = token_json['access_token']

body = {
    'metadata': {
        'ideType': 'ANTIGRAVITY',
        'ideVersion': '2.24.0',
        'pluginVersion': '2.24.0'
    }
}

headers = {
    'Authorization': 'Bearer ' + access_token,
    'Content-Type': 'application/json',
    'User-Agent': 'antigravity/2.24.0 (win32; x64)'
}

for host in ['https://daily-cloudcode-pa.googleapis.com', 'https://cloudcode-pa.googleapis.com']:
    try:
        url = host + '/v1internal:fetchAvailableModels'
        m_req = urllib.request.Request(url, data=json.dumps(body).encode('utf-8'), headers=headers, method='POST')
        with urllib.request.urlopen(m_req) as m_resp:
            res = json.loads(m_resp.read().decode('utf-8'))
            print(f'{host} fetchAvailableModels SUCCESS:')
            print(json.dumps(res, indent=2))
    except urllib.error.HTTPError as e:
        err_body = e.read().decode('utf-8', errors='ignore')
        print(f'{host} fetchAvailableModels returned {e.code}: {err_body[:300]}')

import ctypes
import json
import urllib.request
import urllib.parse
import urllib.error
from ctypes import wintypes

advapi32 = ctypes.windll.advapi32
class CREDENTIAL(ctypes.Structure):
    _fields_ = [
        ('Flags', wintypes.DWORD), ('Type', wintypes.DWORD), ('TargetName', wintypes.LPWSTR),
        ('Comment', wintypes.LPWSTR), ('LastWritten', wintypes.FILETIME),
        ('CredentialBlobSize', wintypes.DWORD), ('CredentialBlob', ctypes.POINTER(ctypes.c_byte)),
        ('Persist', wintypes.DWORD), ('AttributeCount', wintypes.DWORD),
        ('Attributes', ctypes.c_void_p), ('TargetAlias', wintypes.LPWSTR), ('UserName', wintypes.LPWSTR),
    ]

PCREDENTIAL = ctypes.POINTER(CREDENTIAL)
CredReadW = advapi32.CredReadW
CredReadW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, ctypes.POINTER(PCREDENTIAL)]
CredReadW.restype = wintypes.BOOL
CredFree = advapi32.CredFree
CredFree.argtypes = [ctypes.c_void_p]

pcred = PCREDENTIAL()
if not CredReadW('gemini:antigravity', 1, 0, ctypes.byref(pcred)):
    print("Failed to read credential")
    exit(1)

cred = pcred.contents
blob = bytes(ctypes.cast(cred.CredentialBlob, ctypes.POINTER(ctypes.c_char * cred.CredentialBlobSize)).contents)
data = json.loads(blob.decode('utf-8'))
rt = data['token']['refresh_token']
cid = '1071006060591-tmhssin2h21lcre235vtolojh4g403ep.apps.googleusercontent.com'
sec = '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'
CredFree(pcred)

# Refresh token
payload = {
    'client_id': cid,
    'client_secret': sec,
    'refresh_token': rt,
    'grant_type': 'refresh_token'
}
req = urllib.request.Request('https://oauth2.googleapis.com/token', data=urllib.parse.urlencode(payload).encode('utf-8'), method='POST')
with urllib.request.urlopen(req) as resp:
    token_res = json.loads(resp.read().decode('utf-8'))
    access_token = token_res['access_token']
    print("Got access token with Antigravity scopes!")

headers = {
    'Authorization': 'Bearer ' + access_token,
    'Content-Type': 'application/json',
    'User-Agent': 'antigravity/2.24.0 (win32; x64)'
}

# Test 1: fetchAvailableModels
try:
    url = 'https://daily-cloudcode-pa.googleapis.com/v1internal:fetchAvailableModels'
    m_req = urllib.request.Request(url, data=b'{}', headers=headers, method='POST')
    with urllib.request.urlopen(m_req) as resp:
        res = json.loads(resp.read().decode('utf-8'))
        print("fetchAvailableModels SUCCESS:")
        print(json.dumps(res, indent=2)[:600])
except urllib.error.HTTPError as e:
    print(f"fetchAvailableModels error {e.code}: {e.read().decode('utf-8')[:300]}")

# Test 2: generateContent
try:
    url = 'https://daily-cloudcode-pa.googleapis.com/v1internal:generateContent'
    gen_body = {
        'model': 'gemini-3.8-flash',
        'request': {
            'contents': [
                {'role': 'user', 'parts': [{'text': 'Hello, who are you? Reply in 5 words.'}]}
            ]
        }
    }
    m_req = urllib.request.Request(url, data=json.dumps(gen_body).encode('utf-8'), headers=headers, method='POST')
    with urllib.request.urlopen(m_req) as resp:
        res = json.loads(resp.read().decode('utf-8'))
        print("generateContent SUCCESS:")
        print(json.dumps(res, indent=2)[:600])
except urllib.error.HTTPError as e:
    print(f"generateContent error {e.code}: {e.read().decode('utf-8')[:300]}")

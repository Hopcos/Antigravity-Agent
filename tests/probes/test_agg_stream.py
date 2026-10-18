import ctypes
import json
import urllib.request
import urllib.parse
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
    exit(1)

blob = bytes(ctypes.cast(pcred.contents.CredentialBlob, ctypes.POINTER(ctypes.c_char * pcred.contents.CredentialBlobSize)).contents)
data = json.loads(blob.decode('utf-8'))
rt = data['token']['refresh_token']
CredFree(pcred)

payload = {
    'client_id': '1071006060591-tmhssin2h21lcre235vtolojh4g403ep.apps.googleusercontent.com',
    'client_secret': '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',
    'refresh_token': rt,
    'grant_type': 'refresh_token'
}
req = urllib.request.Request('https://oauth2.googleapis.com/token', data=urllib.parse.urlencode(payload).encode('utf-8'), method='POST')
with urllib.request.urlopen(req) as resp:
    access_token = json.loads(resp.read().decode('utf-8'))['access_token']

headers = {
    'Authorization': 'Bearer ' + access_token,
    'Content-Type': 'application/json',
    'User-Agent': 'antigravity/2.24.0 (win32; x64)'
}

stream_url = 'https://daily-cloudcode-pa.googleapis.com/v1internal:streamGenerateContent?alt=sse'
body = {
    'model': 'gemini-3.8-flash-high',
    'project': 'aicode-consumers',
    'request': {
        'contents': [
            {'role': 'user', 'parts': [{'text': 'Explain what is an AI Agent in 2 sentences.'}]}
        ]
    }
}

req = urllib.request.Request(stream_url, data=json.dumps(body).encode('utf-8'), headers=headers, method='POST')
full_text = ""
usage = None
with urllib.request.urlopen(req) as resp:
    for line in resp:
        s = line.decode('utf-8', errors='ignore').strip()
        if s.startswith('data:'):
            chunk = json.loads(s[5:].strip())
            inner = chunk.get('response', {})
            candidates = inner.get('candidates', [])
            if candidates:
                for part in candidates[0].get('content', {}).get('parts', []):
                    if 'text' in part:
                        full_text += part['text']
            if 'usageMetadata' in inner:
                usage = inner['usageMetadata']

print("AGGREGATED FULL RESPONSE:")
print(full_text)
print("USAGE:", usage)

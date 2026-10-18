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
if CredReadW('gemini:antigravity', 1, 0, ctypes.byref(pcred)):
    cred = pcred.contents
    blob = bytes(ctypes.cast(cred.CredentialBlob, ctypes.POINTER(ctypes.c_char * cred.CredentialBlobSize)).contents)
    data = json.loads(blob.decode('utf-8'))
    rt = data['token']['refresh_token']
    cid = '1071006060591-tmhssin2h21lcre235vtolojh4g403ep.apps.googleusercontent.com'
    CredFree(pcred)

    secrets = [
        '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',
        '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',
        ''
    ]

    for sec in secrets:
        payload = {
            'client_id': cid,
            'refresh_token': rt,
            'grant_type': 'refresh_token'
        }
        if sec:
            payload['client_secret'] = sec
        try:
            req = urllib.request.Request(
                'https://oauth2.googleapis.com/token',
                data=urllib.parse.urlencode(payload).encode('utf-8'),
                method='POST'
            )
            with urllib.request.urlopen(req) as resp:
                res = json.loads(resp.read().decode('utf-8'))
                print(f"SUCCESS with secret [{sec[:15]}...]!")
                print("Access token length:", len(res['access_token']))
                print("expires_in:", res.get('expires_in'))
                print("scope:", res.get('scope'))
                break
        except urllib.error.HTTPError as e:
            err_body = e.read().decode('utf-8', errors='ignore')
            print(f"Secret [{sec[:15]}...] failed: {e.code} {err_body[:150]}")
else:
    print("Could not read gemini:antigravity credential")

"""
Régénère les coffres KeePass de test (mot de passe « Urgence-2026! ») et vérifie que chacun s'ouvre dans
pykeepass et KeePassXC. KDBX 3.1 et 4.1 créés par KeePassXC, KDBX 4.0 par pykeepass ; tous les formats de
fichier clé. Prérequis : keepassxc-cli (KeePassXC 2.7) et « pip install pykeepass ».
Usage : python3 generate.py
"""
import os, subprocess, base64
from pykeepass import create_database, PyKeePass
from pykeepass.kdbx_parsing.kdbx4 import kdf_uuids
PW = 'Urgence-2026!'
OUT = os.path.dirname(os.path.abspath(__file__))
for old in os.listdir(OUT):
    if old.endswith(('.kdbx', '.key', '.keyx')):
        os.remove(os.path.join(OUT, old))

def kxc(*args, stdin=''):
    r = subprocess.run(['keepassxc-cli', *args], input=stdin, capture_output=True, text=True)
    assert r.returncode == 0, (args, r.stderr)
    return r.stdout

# --- Fichiers clés dans tous les formats
open(f'{OUT}/key-raw32.key', 'wb').write(bytes(range(32)))
open(f'{OUT}/key-hex64.key', 'w').write(bytes(range(100, 132)).hex())
open(f'{OUT}/key-any.key', 'wb').write(b'Un fichier quelconque sert de cle : son SHA-256 est utilise.\n' * 3)
data = bytes(range(200, 232))
open(f'{OUT}/key-v1.keyx', 'w').write(
    '<?xml version="1.0" encoding="utf-8"?>\n<KeyFile>\n\t<Meta>\n\t\t<Version>1.00</Version>\n\t</Meta>\n\t<Key>\n'
    f'\t\t<Data>{base64.b64encode(data).decode()}</Data>\n\t</Key>\n</KeyFile>\n')

# --- KDBX 3.1 créé par KeePassXC (AES-KDF, AES-256, Salsa20), avec historique
v3 = f'{OUT}/kxc-kdbx31.kdbx'
kxc('db-create', '-p', v3, stdin=f'{PW}\n{PW}\n')
kxc('add', v3, 'srv-lnx01', '-u', 'root', '--url', 'ssh://srv-lnx01.corp.local:22', '-p', stdin=f'{PW}\nAncien-1\n')
kxc('edit', v3, 'srv-lnx01', '-p', stdin=f'{PW}\nRoot-Pass 1\n')
kxc('mkdir', v3, 'Windows', stdin=f'{PW}\n')
kxc('add', v3, 'Windows/srv-win01', '-u', 'CORP\\administrator', '--url', 'rdp://srv-win01.corp.local', '-p', stdin=f'{PW}\nAdm!n W1n\n')

# --- KDBX 4.1 : KeePassXC passe en 4.1 quand une entrée part à la corbeille
v3 = f'{OUT}/kxc-kdbx41.kdbx'
kxc('db-create', '-p', '-t', '100', v3, stdin=f'{PW}\n{PW}\n')
kxc('add', v3, 'srv-lnx01', '-u', 'root', '--url', 'ssh://srv-lnx01.corp.local:22', '-p', stdin=f'{PW}\nAncien-1\n')
kxc('edit', v3, 'srv-lnx01', '-p', stdin=f'{PW}\nRoot-Pass 1\n')          # crée un historique
kxc('mkdir', v3, 'Windows', stdin=f'{PW}\n')
kxc('add', v3, 'Windows/srv-win01', '-u', 'CORP\\administrator', '--url', 'rdp://srv-win01.corp.local', '-p', stdin=f'{PW}\nAdm!n W1n\n')
kxc('add', v3, 'old-srv', '-u', 'x', '-p', stdin=f'{PW}\nx\n')
kxc('rm', v3, 'old-srv', stdin=f'{PW}\n')                                 # va dans la corbeille

v3k = f'{OUT}/kxc-kdbx31-keyonly.kdbx'
kxc('db-create', '--set-key-file', f'{OUT}/kxc-v2.keyx', '-t', '100', v3k)
kxc('add', '--no-password', '-k', f'{OUT}/kxc-v2.keyx', v3k, 'srv-key', '-u', 'admin', '-p', stdin='Key-Only 2\n')

# --- KDBX 4 créés par pykeepass
def fill(kp):
    g = kp.add_group(kp.root_group, 'Windows')
    kp.add_entry(kp.root_group, 'srv-lnx01', 'root', 'Root-Pass 1', url='ssh://srv-lnx01.corp.local:22',
                 notes='Ligne 1\nLigne 2 — accents éàü')
    kp.add_entry(g, 'srv-win01', 'CORP\\administrator', 'Adm!n W1n', url='rdp://srv-win01.corp.local')
    e = kp.add_entry(kp.root_group, 'db01', 'oracle', 'Or@cle<&>"', tags=['ssh', 'prod'])
    e.set_custom_property('Port', '2222')
    e.set_custom_property('Secret', 'hidden', protect=True)

def kdbx4(name, cipher='aes256', kdf='argon2', password=PW, keyfile=None):
    path = f'{OUT}/{name}'
    kp = create_database(path, password=password, keyfile=keyfile)
    dh = kp.kdbx.header.value.dynamic_header
    if cipher == 'chacha20':
        dh.cipher_id.data = 'chacha20'
        dh.encryption_iv.data = os.urandom(12)
    p = dh.kdf_parameters.data.dict
    if kdf in ('argon2', 'argon2id'):
        p['$UUID'].value = kdf_uuids[kdf]
        p['M'].value = 8 * 1024 * 1024
        p['I'].value = 2
    else:
        p['$UUID'].value = kdf_uuids['aeskdf']; p['$UUID'].next_byte = 5
        r = p['I']; r.key = 'R'; r.value = 20000; r.next_byte = 66
        s = p['S']; s.value = os.urandom(32); s.next_byte = 0
        items = {'$UUID': p['$UUID'], 'R': r, 'S': s}
        p.clear(); p.update(items)
    fill(kp)
    kp.save()

kdbx4('py-kdbx4-argon2d-aes.kdbx')
kdbx4('py-kdbx4-argon2id-chacha20.kdbx', cipher='chacha20', kdf='argon2id')
kdbx4('py-kdbx4-aeskdf-aes.kdbx', kdf='aeskdf')
for kf in ['key-raw32.key', 'key-hex64.key', 'key-any.key', 'key-v1.keyx', 'kxc-v2.keyx']:
    kdbx4(f'py-kdbx4-{kf.split(".")[0]}.kdbx', keyfile=f'{OUT}/{kf}')
kdbx4('py-kdbx4-keyonly.kdbx', password=None, keyfile=f'{OUT}/key-raw32.key')

# --- Vérification croisée : chaque coffre s'ouvre dans pykeepass et KeePassXC
for f in sorted(os.listdir(OUT)):
    if not f.endswith('.kdbx'):
        continue
    kf = None
    if 'keyonly' in f and 'kxc' in f: kf = 'kxc-v2.keyx'
    elif 'keyonly' in f: kf = 'key-raw32.key'
    elif f.startswith('py-kdbx4-key') or f.startswith('py-kdbx4-kxc'): kf = f[len('py-kdbx4-'):-5] + ('.keyx' if f.endswith(('v1.kdbx', 'v2.kdbx')) else '.key')
    pw = None if 'keyonly' in f else PW
    kp = PyKeePass(f'{OUT}/{f}', password=pw, keyfile=f'{OUT}/{kf}' if kf else None)
    args = ['ls', '-R', '-q', f'{OUT}/{f}'] + (['-k', f'{OUT}/{kf}'] if kf else []) + (['--no-password'] if pw is None else [])
    out = kxc(*args, stdin=(pw + '\n') if pw else '')
    h = kp.kdbx.header.value
    print(f'{f:42} {h.major_version}.{h.minor_version} {str(kp.encryption_algorithm):8} {str(kp.kdf_algorithm):9} key={kf}  entries={len(kp.entries)}  kxc: {out.strip().replace(chr(10), " | ")}')

"""Inventory the files owned by this package, without user profiles or build output."""
from pathlib import Path
import argparse
import hashlib
import json
from payload_policy import include_file

parser=argparse.ArgumentParser()
parser.add_argument('payload',type=Path)
parser.add_argument('version')
parser.add_argument('--include-browser-runtime',action='store_true')
args=parser.parse_args()
root=args.payload.resolve()
manifest=root/'app/package.json'
files=[]
for file in sorted(root.rglob('*')):
    if not file.is_file() or file==manifest or not include_file(file.relative_to(root),args.include_browser_runtime):continue
    assert not file.is_symlink()
    assert file.name.lower() not in {'token.dpapi','preferences.json','mnemonics.user.json','history.json'}
    files.append({'Path':file.relative_to(root).as_posix(),'Sha256':hashlib.sha256(file.read_bytes()).hexdigest()})
manifest.write_text(json.dumps({'Schema':1,'ProductId':'MemoLex.Desktop.9A25DBAA-234C-48EA-96DC-1DE36D9872F7','Version':args.version,'Files':files},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(f'Package inventory: {len(files)} owned files')

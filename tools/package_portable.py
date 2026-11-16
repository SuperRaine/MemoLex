"""Create a portable archive from the same inventory used by the MSI."""
import argparse
import hashlib
import json
from pathlib import Path
import zipfile
from payload_policy import include_file

parser=argparse.ArgumentParser()
parser.add_argument('payload',type=Path)
parser.add_argument('output',type=Path)
parser.add_argument('--include-browser-runtime',action='store_true')
args=parser.parse_args()
root=args.payload.resolve()
manifest=json.loads((root/'app/package.json').read_text(encoding='utf-8'))
assert manifest['Schema']==1 and manifest['ProductId']=='MemoLex.Desktop.9A25DBAA-234C-48EA-96DC-1DE36D9872F7'
expected={item['Path']:item['Sha256'] for item in manifest['Files']}
assert {'MemoLex.exe','MemoLex.dll','Uninstall.exe','app/MemoLex.exe'}<=expected.keys()
expected['app/package.json']=None
files=[file for file in sorted(root.rglob('*')) if file.is_file() and include_file(file.relative_to(root),args.include_browser_runtime)]
assert {file.relative_to(root).as_posix() for file in files}==expected.keys(),'Payload changed after inventory was created'
for file in files:
    assert not file.is_symlink()
    digest=expected[file.relative_to(root).as_posix()]
    assert digest is None or hashlib.sha256(file.read_bytes()).hexdigest()==digest,'Payload changed after inventory was created'
args.output.parent.mkdir(parents=True,exist_ok=True)
assert not args.output.resolve().is_relative_to(root),'Archive must be outside the payload'
with zipfile.ZipFile(args.output,'w',zipfile.ZIP_DEFLATED,compresslevel=9) as archive:
    for file in files:archive.write(file,'MemoLex/'+file.relative_to(root).as_posix())
print(f'Portable archive: {len(files)} files, {args.output.stat().st_size:,} bytes')

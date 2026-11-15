"""Generate stable per-file MSI components from the exact portable payload."""
import argparse
import hashlib
from pathlib import Path
import uuid
import xml.etree.ElementTree as ET
import sys

project=Path(__file__).resolve().parents[1]
helpers=project/'release-tools'
if not helpers.is_dir():helpers=project/'tools'
sys.path.insert(0,str(helpers))
from payload_policy import include_file

parser=argparse.ArgumentParser()
parser.add_argument('payload',type=Path);parser.add_argument('output',type=Path)
parser.add_argument('--include-browser-runtime',action='store_true')
args=parser.parse_args();payload=args.payload.resolve()
NS='http://wixtoolset.org/schemas/v4/wxs';ET.register_namespace('',NS)
def node(parent,tag,**attrs):return ET.SubElement(parent,'{'+NS+'}'+tag,attrs)
def key(value):return hashlib.sha256(value.encode('utf-8')).hexdigest()[:24]
root=ET.Element('{'+NS+'}Wix');fragment=node(root,'Fragment')
directory=node(fragment,'DirectoryRef',Id='INSTALLFOLDER')
group=node(node(root,'Fragment'),'ComponentGroup',Id='PayloadGroup')
directories={'.':directory};owners=set()
files=[p for p in sorted(payload.rglob('*')) if p.is_file() and include_file(p.relative_to(payload),args.include_browser_runtime)]
assert files and (payload/'MemoLex.exe').is_file()
for path in files:
    relative=path.relative_to(payload)
    assert not path.is_symlink() and not any(part in {'backups','.git','bin','obj'} for part in relative.parts)
    assert path.name.lower() not in {'token.dpapi','mnemonics.user.json','preferences.json','history.json','smoke-test.json'}
    for parent in reversed(relative.parents):
        name=parent.as_posix()
        if name not in directories:
            directories[name]=node(directories[parent.parent.as_posix()],'Directory',Id='D'+key(name),Name=parent.name)
    parent=directories[relative.parent.as_posix()]
    ident=key(relative.as_posix().lower());component='C'+ident
    comp=node(parent,'Component',Id=component,Guid=str(uuid.uuid5(uuid.UUID('bc8cce69-7ce2-43d0-9b85-d43a60b353c1'),relative.as_posix().lower())).upper(),Bitness='always64')
    node(comp,'File',Id='MemoLexExe' if relative.as_posix()=='MemoLex.exe' else 'F'+ident,Source='$(var.PayloadDir)/'+relative.as_posix())
    node(comp,'RegistryValue',Root='HKCU',Key=r'Software\SuperRaine\MemoLex\Files',Name=ident,Type='integer',Value='1',KeyPath='yes')
    if relative.parent.as_posix() not in owners:
        node(comp,'RemoveFolder',Id='R'+key(relative.parent.as_posix()),On='uninstall')
        owners.add(relative.parent.as_posix())
    node(group,'ComponentRef',Id=component)
ET.indent(root);args.output.parent.mkdir(parents=True,exist_ok=True)
ET.ElementTree(root).write(args.output,encoding='utf-8',xml_declaration=True)
print(f'MSI payload: {len(files)} files, {sum(p.stat().st_size for p in files):,} bytes')

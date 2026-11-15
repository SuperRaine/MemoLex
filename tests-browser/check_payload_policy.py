"""Check the actual future payload selection without generating a software package."""
from pathlib import Path
import argparse,json,sys,subprocess,tempfile,xml.etree.ElementTree as ET

PROJECT=Path(__file__).resolve().parents[1]
ROOT=PROJECT.parents[1]
helpers=PROJECT/'release-tools'
if not helpers.is_dir():helpers=PROJECT/'tools'
sys.path.insert(0,str(helpers))
from payload_policy import include_file
assert not include_file(Path('webview2/msedgewebview2.exe'))
assert include_file(Path('webview2/msedgewebview2.exe'),True)
assert include_file(Path('browser-setup.exe')) and include_file(Path('data/dictionary.data'))
assert not include_file(Path('smoke-test.json')) and not include_file(Path('MemoLex.pdb'))
parser=argparse.ArgumentParser()
parser.add_argument('--payload',type=Path,default=PROJECT/'dist/MemoLex' if (PROJECT/'dist/MemoLex').is_dir() else ROOT/'outputs/MemoLex')
payload=parser.parse_args().payload.resolve()
selected={p.relative_to(payload).as_posix() for p in payload.rglob('*') if p.is_file() and include_file(p.relative_to(payload))}
assert 'browser-setup.exe' in selected
assert not any(p.startswith('webview2/') for p in selected)
with tempfile.TemporaryDirectory(prefix='MemoLex.PayloadPolicy.') as temporary:
    generated=Path(temporary)/'payload.wxs'
    subprocess.run([sys.executable,str(PROJECT/'installer/generate_payload.py'),str(payload),str(generated)],check=True,capture_output=True)
    paths={element.attrib['Source'].removeprefix('$(var.PayloadDir)/') for element in ET.parse(generated).iter() if element.tag.endswith('}File')}
    assert paths==selected,'Installer and portable payload selections differ'
report={'ok':True,'selected_files':len(selected),'uncompressed_bytes':sum((payload/p).stat().st_size for p in selected),
        'optional_browser_files_excluded':True,'installer_and_portable_selections_identical':True,'package_created':False}
version=ET.parse(PROJECT/'src/MemoLex.csproj').findtext('PropertyGroup/Version')
(PROJECT/f'payload-policy-check-{version}.json').write_text(json.dumps(report,indent=2)+'\n','utf-8')
print(json.dumps(report))

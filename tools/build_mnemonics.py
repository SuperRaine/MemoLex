"""Ship only reviewed, user-authored mnemonics as an offline starting set."""
import argparse
import hashlib
import json
from pathlib import Path

parser=argparse.ArgumentParser()
parser.add_argument('--approved',type=Path,required=True)
parser.add_argument('--data-dir',type=Path,required=True)
args=parser.parse_args()
entries={}
for line in args.approved.read_text(encoding='utf-8-sig').splitlines():
    item=json.loads(line)
    if item.get('review_status')!='approved':continue
    word=item['word'].strip()
    assert item['note'].strip() and word.casefold() not in entries
    entries[word.casefold()]=[dict(Id='seed:'+word.casefold(),NoteType=item['note_type'],Text=item['note'],Origin='builtin')]
args.data_dir.mkdir(parents=True,exist_ok=True)
(args.data_dir/'mnemonics.json').write_text(json.dumps(entries,ensure_ascii=False,separators=(',',':'))+'\n',encoding='utf-8')
(args.data_dir/'mnemonics.meta.json').write_text(json.dumps(dict(words=len(entries),scope='Previously reviewed root-and-affix mnemonics; partial coverage of the public exam word lists.',source_file=args.approved.name,source_sha256=hashlib.sha256(args.approved.read_bytes()).hexdigest()),ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(dict(bundled_words=len(entries)),ensure_ascii=False))

"""Build offline synonym/derivational links from Princeton WordNet 3.1."""
from collections import defaultdict
import argparse
import hashlib
import json
from pathlib import Path
import re
import tarfile

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--archive',type=Path,default=Path(__file__).with_name('wn3.1.dict.tar.gz'))
parser.add_argument('--data-dir',type=Path,default=Path('outputs/MemoLex/data'))
args=parser.parse_args()
archive=args.archive;target=args.data_dir
synsets={}
relations=defaultdict(lambda:{'s':set(),'d':set(),'g':''})
def word(text):
    return re.sub(r'\((?:a|p|ip)\)$','',text).replace('_',' ')
with tarfile.open(archive) as package:
    for member in package.getmembers():
        if Path(member.name).name=='LICENSE':
            (target/'WordNet-LICENSE.txt').write_bytes(package.extractfile(member).read())
        if Path(member.name).name not in {'data.noun','data.verb','data.adj','data.adv'}:
            continue
        for raw in package.extractfile(member):
            line=raw.decode('utf-8').strip('\n')
            if not line or not line[0].isdigit():continue
            body,gloss=line.split('|',1); parts=body.split(); count=int(parts[3],16); position=4
            words=[word(parts[position+2*i]) for i in range(count)]; position+=2*count
            pointer_count=int(parts[position]);position+=1
            pointers=[parts[position+4*i:position+4*i+4] for i in range(pointer_count)]
            pos='a' if parts[2]=='s' else parts[2]
            synsets[(pos,parts[0])]=(words,pointers,gloss.strip())
    # The archive has a LICENSE file; otherwise keep the Princeton notice from README.
    if not (target/'WordNet-LICENSE.txt').exists():
        lines=package.extractfile('dict/data.noun').read().decode('utf-8').splitlines()
        notice='\n'.join(re.sub(r'^  \d+ ','',line) for line in lines if re.match(r'^  \d+ ',line))
        assert 'Princeton' in notice and 'permission' in notice.lower()
        (target/'WordNet-LICENSE.txt').write_text(notice+'\n',encoding='utf-8')

for (pos,offset),(words,pointers,gloss) in synsets.items():
    for lemma in words:
        row=relations[lemma];row['s'].update(w for w in words if w.casefold()!=lemma.casefold())
        if not row['g']:row['g']=gloss
    for symbol,destination,dest_pos,indices in pointers:
        # '+' is derivational. Adverb '\\' links specifically to the adjective
        # it derives from; adjective pertainyms are not assumed to be derivations.
        if symbol!='+' and not (symbol=='\\' and pos=='r'):continue
        other=synsets.get(('a' if dest_pos=='s' else dest_pos,destination))
        if not other:continue
        source_index=int(indices[:2],16);target_index=int(indices[2:],16)
        if not (1<=source_index<=len(words) and 1<=target_index<=len(other[0])):continue
        a,b=words[source_index-1],other[0][target_index-1]
        if a.casefold()!=b.casefold():relations[a]['d'].add(b);relations[b]['d'].add(a)

with (target/'related.data').open('wb') as data,(target/'related.index.tsv').open('w',encoding='utf-8',newline='\n') as index:
    seen=set()
    for lemma,row in sorted(relations.items(),key=lambda pair:(pair[0].casefold(),pair[0])):
        key=lemma.casefold()
        if key in seen:continue
        seen.add(key)
        record={'word':lemma,'synonyms':sorted(row['s']),'derivatives':sorted(row['d']),'gloss':row['g']}
        encoded=json.dumps(record,ensure_ascii=False,separators=(',',':')).encode()
        index.write(f'{lemma}\t{data.tell()}\t{len(encoded)}\n');data.write(encoded)

# Common ECDICT entries provide a bounded, frequency-ranked spelling pool.
spellings=[]
with (target/'dictionary.data').open('rb') as data:
    for line in (target/'dictionary.index.tsv').read_text('utf-8').splitlines():
        lemma,offset,length=line.split('\t')
        if not re.fullmatch('[a-z]{3,24}',lemma):continue
        data.seek(int(offset));entry=json.loads(data.read(int(length)))
        ranks=[int(entry.get(field) or 0) for field in ('bnc','frq') if str(entry.get(field) or '0').isdigit()]
        rank=min((value for value in ranks if value>0),default=0)
        if rank and rank<=60000:spellings.append((rank,lemma))
(target/'similar-words.tsv').write_text(''.join(f'{lemma}\t{rank}\n' for rank,lemma in sorted(spellings)),encoding='utf-8')
meta={'source':'https://wordnetcode.princeton.edu/wn3.1.dict.tar.gz','version':'Princeton WordNet 3.1','archive_sha256':hashlib.sha256(archive.read_bytes()).hexdigest(),'word_count':len(seen),'similar_word_pool':len(spellings),'synonyms':'Members of the same synset. Multiple senses may be represented.','derivatives':'Lexical + pointers and adverb-derived-from-adjective pointers, in both directions.','citation':'Princeton University. WordNet: A Lexical Database for English. https://wordnet.princeton.edu/'}
(target/'related.meta.json').write_text(json.dumps(meta,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(meta,ensure_ascii=False))

"""Build an offline Chinese reverse index and sense-aligned COW English synonyms."""
from array import array
from collections import defaultdict
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import struct
import tarfile

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--data-dir',type=Path,default=Path('outputs/MemoLex/data'))
parser.add_argument('--research-dir',type=Path,default=Path(__file__).resolve().parent)
args=parser.parse_args();target=args.data_dir;research=args.research_dir
english=re.compile(r"^[A-Za-z][A-Za-z .’'\-]{0,99}$")
han=re.compile(r'[\u3400-\u9fff]+')
annotations=re.compile(r'\[[^\]]*\]|\([^)]*\)|（[^）]*）')
def normalize(text):
    result=''.join(han.findall(text))
    return result[:-1] if len(result)>2 and result.endswith('的') else result
postings=defaultdict(lambda:array('I'));word_ids={};doc_count=0;indexed_count=0
with (target/'dictionary.data').open('rb') as data:
    for doc_id,line in enumerate((target/'dictionary.index.tsv').read_text('utf-8').splitlines()):
        word,offset,length=line.split('\t');word_ids.setdefault(word.casefold(),doc_id);doc_count+=1
        if not english.fullmatch(word):continue
        data.seek(int(offset));entry=json.loads(data.read(int(length)))
        chunks=han.findall(annotations.sub('',entry.get('translation','')))
        tokens=set()
        for chunk in chunks:
            tokens.update('1:'+character for character in chunk)
            tokens.update('2:'+chunk[i:i+2] for i in range(len(chunk)-1))
        if not tokens:continue
        indexed_count+=1
        for token in tokens:postings[token].append(doc_id)
def variable(value):
    result=bytearray()
    while value>=128:result.append((value&127)|128);value>>=7
    result.append(value);return result
with (target/'chinese.postings').open('wb') as output,(target/'chinese.tokens.tsv').open('w',encoding='utf-8',newline='\n') as index:
    output.write(b'CJX1'+struct.pack('<I',doc_count))
    for token,ids in sorted(postings.items()):
        encoded=bytearray();previous=0
        for value in ids:encoded.extend(variable(value-previous));previous=value
        index.write(f'{token}\t{output.tell()}\t{len(encoded)}\n');output.write(encoded)

# COW uses Princeton WordNet 3.0 offsets. Read the matching English data rather
# than incorrectly joining its offsets against the existing 3.1 relation data.
synsets={}
with tarfile.open(research/'WNdb-3.0.tar.gz') as package:
    for member in package.getmembers():
        if Path(member.name).name not in {'data.noun','data.verb','data.adj','data.adv'}:continue
        raw=package.extractfile(member).read().decode('utf-8')
        if Path(member.name).name=='data.noun':
            notice='\n'.join(re.sub(r'^  \d+ ','',line) for line in raw.splitlines() if re.match(r'^  \d+ ',line))
            assert 'Princeton' in notice and 'Permission' in notice
            (target/'WordNet-3.0-LICENSE.txt').write_text(notice+'\n',encoding='utf-8')
        for line in raw.splitlines():
            if not line or not line[0].isdigit():continue
            parts=line.split('|',1)[0].split();count=int(parts[3],16)
            words=[re.sub(r'\((?:a|p|ip)\)$','',parts[4+2*i]).replace('_',' ') for i in range(count)]
            key=parts[0]+'-'+('a' if parts[2]=='s' else parts[2])
            synsets[key]=[word_ids[word.casefold()] for word in words if english.fullmatch(word) and word.casefold() in word_ids]
cow=defaultdict(set)
for line in (research/'wn-data-cmn.tab').read_text('utf-8').splitlines():
    if line.startswith('#'):continue
    fields=line.split('\t')
    if len(fields)==3 and fields[1]=='cmn:lemma':
        term=normalize(fields[2]);
        if 1<=len(term)<=30:cow[fields[0]].add(term)
term_synsets=defaultdict(set)
for synset,terms in cow.items():
    for term in terms:term_synsets[term].add(synset)
lexicon={}
for term,keys in term_synsets.items():
    ids=set();equivalents=set()
    for key in keys:
        ids.update(synsets.get(key,[]))
        equivalents.update(value for value in cow[key] if value!=term and len(term_synsets[value])==1)
    if ids:lexicon[term]={'ids':sorted(ids),'equivalents':sorted(equivalents,key=lambda value:(len(value),value))[:16]}
(target/'chinese.synonyms.json').write_text(json.dumps(lexicon,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
shutil.copy2(research/'Chinese-Open-Wordnet-LICENSE.txt',target/'Chinese-Open-Wordnet-LICENSE.txt')
meta={'format':1,'dictionary_rows':doc_count,'indexed_entries':indexed_count,'tokens':len(postings),'chinese_lemmas':len(lexicon),
    'dictionary_index_sha256':hashlib.sha256((target/'dictionary.index.tsv').read_bytes()).hexdigest(),
    'sources':[
        {'url':'https://raw.githubusercontent.com/omwn/omw-data/main/wns/cow/wn-data-cmn.tab','sha256':hashlib.sha256((research/'wn-data-cmn.tab').read_bytes()).hexdigest()},
        {'url':'https://wordnetcode.princeton.edu/3.0/WNdb-3.0.tar.gz','sha256':hashlib.sha256((research/'WNdb-3.0.tar.gz').read_bytes()).hexdigest()}],
    'citation':'Shan Wang and Francis Bond. 2013. Building the Chinese Open Wordnet (COW): Starting from Core Synsets. IJCNLP ALR, pp. 10–18.',
    'synonyms':'COW Chinese lemmas joined to the English members of their matching Princeton WordNet 3.0 synsets.'}
(target/'chinese.meta.json').write_text(json.dumps(meta,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(meta,ensure_ascii=False))

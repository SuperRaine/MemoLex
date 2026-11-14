"""Replace circular base-from-derived mnemonics and empty inflection lists."""
from pathlib import Path
from association_rewrites import TYPES

NOTES={}
for line in Path(__file__).with_name('independent-v1.0.3.txt').read_text('utf-8').splitlines():
    if not line.strip() or line.startswith('#'):continue
    word,kind,text=line.split('|',2)
    assert word not in NOTES,word
    NOTES[word]=(TYPES[kind],text.replace('\\n','\n'))
SOURCES={word:['https://github.com/skywind3000/ECDICT','原创拆词、字形、谐音或词义联想'] for word in NOTES}
for word in ['credential','grocer','franchise','scholastic','calorie','seminar','vinegar','inertia','silicon',
             'clientele','capuchin','cassette','etiquette','gymnasium','squirrel','stadium','repertoire','utopia',
             'comrade','museum','opera']:
    SOURCES[word]=['https://www.etymonline.com/word/'+word]

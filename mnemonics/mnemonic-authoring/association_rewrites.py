"""Original, word-specific spelling, sound and meaning bridges; no collocation fallback."""
from pathlib import Path
TYPES={'拆':'拆词联想','形':'形近词联想','音':'谐音联想','义':'多义联想','构':'词根词缀','语':'字形与语法联想','源':'词源联想'}
NOTES={}
for line in Path(__file__).with_name('associations-v1.0.2.txt').read_text('utf-8').splitlines():
    if not line.strip() or line.startswith('#'):continue
    word,kind,text=line.split('|',2)
    assert word not in NOTES,word
    NOTES[word]=(TYPES[kind],text.replace('\\n','\n'))
SOURCES={word:['https://github.com/skywind3000/ECDICT','原创拆词、字形、谐音或词义联想'] for word in NOTES}
SOURCES.update({
 'quarantine':['https://www.etymonline.com/word/quarantine','https://wwwnc.cdc.gov/eid/article/19/2/ET-1902_article'],
 'paradise':['https://www.etymonline.com/word/paradise'],
 'orient':['https://www.etymonline.com/word/orient'],
 'ensue':['https://www.etymonline.com/word/ensue'],
 'siege':['https://www.etymonline.com/word/siege'],
 'sport':['https://www.etymonline.com/word/sport'],
 'taxi':['https://www.etymonline.com/word/taxi'],
 'target':['https://www.etymonline.com/word/target'],
 'window':['https://www.etymonline.com/word/window'],
 'umbrella':['https://www.etymonline.com/word/umbrella'],
 'tunnel':['https://www.etymonline.com/word/tunnel'],
 'veto':['https://www.etymonline.com/word/veto'],
 'very':['https://www.etymonline.com/word/very'],
 'because':['https://www.etymonline.com/word/because'],
 'camera':['https://www.etymonline.com/word/camera'],
})

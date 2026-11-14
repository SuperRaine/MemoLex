"""Individually authored replacements for the retired family-list mnemonic method."""
from pathlib import Path
from association_rewrites import TYPES

NOTES={}
for line in Path(__file__).with_name('family-v1.0.4-rewrites.txt').read_text('utf-8').splitlines():
    if not line.strip() or line.startswith('#'):continue
    word,kind,text=line.split('|',2)
    assert word not in NOTES,word
    assert '词族' not in TYPES[kind],word
    NOTES[word]=(TYPES[kind],text.replace('\\n','\n'))
SOURCES={word:['https://github.com/skywind3000/ECDICT','原创拆词、字形、谐音或词义桥；词根词缀按真实含义解释'] for word in NOTES}

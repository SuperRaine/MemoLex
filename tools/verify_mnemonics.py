"""Verify the shipped mnemonic bundle against its per-word review ledger."""
import argparse,hashlib,json
from pathlib import Path
parser=argparse.ArgumentParser();parser.add_argument('--directory',type=Path,required=True);args=parser.parse_args()
p=args.directory
notes=json.loads((p/'mnemonics.json').read_text('utf-8'))
meta=json.loads((p/'mnemonics.meta.json').read_text('utf-8'))
manifest=json.loads((p/'mnemonics.revisions.json').read_text('utf-8'))
ledger_path=p/meta.get('review_ledger_file','mnemonic-authoring/review-1.3.0/review-ledger.jsonl')
ledger=[json.loads(x) for x in ledger_path.read_text('utf-8').splitlines()]
sha=lambda b:hashlib.sha256(b).hexdigest()
assert sha((p/'mnemonics.json').read_bytes())==meta['sha256']==manifest['BundleSha256']
assert sha(ledger_path.read_bytes())==meta['review_ledger_sha256']
assert len(ledger)==len({r['word'].casefold() for r in ledger})==meta['target_words']==10613
assert len(notes)==meta['words']==10614
for row in ledger:
    entries=notes[row['word'].casefold()]
    assert any(sha(n['Text'].encode())==row['after_sha256'] for n in entries),row['word']
for word,entries in notes.items():
    assert entries,word
    for n in entries:
        assert n['Text'].strip() and len(n['Text'])<=20000,word
        assert n.get('Origin')=='builtin' and not n.get('CloudId') and not n.get('AccountKey'),word
        assert not any(t in n['Text'] for t in ('词族联想：','构词线索：','把线索和本词词义一起回想','只列有来源依据的成分')),word
        assert '词族' not in n['NoteType'],word
text=lambda w:'\n'.join(n['Text'] for n in notes[w])
for word,parts in {'pronounce':['pro-','nounce','发音','宣告'],'student':['stud','-ent','学习的人'],
                   'course':['课程','路线','一道菜','流动'],'side':['inside','outside','beside'],
                   'continent':['con-','tin','-ent','大陆'],'chromosome':['chromo','身体','染色体'],
                   'mental':['ment','心智'],'undergraduate':['本科生'],'remove':['移除'],
                   'rotten':['过去分词'],'wooden':['由……制成']}.items():
    assert all(part in text(word) for part in parts),word
assert 'biker' not in text('bike')
assert all('使成为或变成' not in text(w) for w in ('rotten','wooden'))
if meta.get('mnemonic_revision')=='1.3.1':
    for word,parts in {'foster':['faster','培养','更快'],'chess':['chest','国际象棋'],
                       'represent':['present','呈现','代表'],'manly':['-ly','像……的'],
                       'timely':['-ly','时机'],'absently':['心不在焉'],
                       'implement':['im-','ple','填满','实施'],
                       'thoughtlessly':['thought','-less','-ly','不体贴']}.items():
        assert all(part in text(word) for part in parts),word
    assert '少一个 r' not in text('foster')
    assert '再赠送' not in text('represent')
    assert meta['pending_rewrite_words']==0
    assert meta['individually_rewritten_to_new_standard']==len(ledger)
    assert sum(r['changed'] and r.get('previously_pending',False) for r in ledger)==3163
assert meta['other_notes_pending_new_standard_review']==0
assert meta['reviewed_target_words']==len(ledger)
print(json.dumps({'ok':True,'target_words_verified':len(ledger),'bundle_keys':len(notes),'changed_entries':meta['entries_changed_this_release'],
                  'bundle_checksum':True,'review_ledger_checksum':True,'all_entry_content_checks':True,'regression_examples':12,
                  'pending_rewrites':meta.get('pending_rewrite_words'),
                  'no_personal_profiles_or_cloud_content':True},ensure_ascii=False))

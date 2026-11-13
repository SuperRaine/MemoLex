"""Build MemoLex's random-access offline dictionary from the pinned ECDICT CSV."""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
from pathlib import Path
import shutil

PACKAGE_ROOT = Path(__file__).resolve().parent.parent
COMMIT = '39cfb9672c0906b63569e0453c2e6abfb9ab182a'
SOURCE_SHA256 = 'd1ecbdf9ee2b920afb6f430ca6b6bb9f559588cf0dff86e04bd255f9390843cc'
FIELDS = ('word', 'phonetic', 'definition', 'translation', 'pos', 'collins',
          'oxford', 'tag', 'bnc', 'frq', 'exchange')


def build(source: Path, output: Path, license_file: Path) -> None:
    source, output, license_file = source.resolve(), output.resolve(), license_file.resolve()
    if not source.is_file():
        raise FileNotFoundError(f'CSV not found: {source}. Run download_ecdict.py first.')
    if not license_file.is_file():
        raise FileNotFoundError(f'License not found: {license_file}. Run download_ecdict.py first.')
    digest = hashlib.sha256(source.read_bytes()).hexdigest()
    if digest != SOURCE_SHA256:
        raise ValueError(f'Expected the pinned ECDICT source checksum {SOURCE_SHA256}; got {digest}.')
    csv.field_size_limit(10_000_000)
    output.mkdir(parents=True, exist_ok=True)
    seen: set[str] = set()
    checks: dict[str, tuple[int, int]] = {}
    count = skipped = 0
    data_temp = output / 'dictionary.data.building'
    index_temp = output / 'dictionary.index.tsv.building'
    with source.open(encoding='utf-8-sig', newline='') as csv_file, \
            data_temp.open('wb') as data, \
            index_temp.open('w', encoding='utf-8', newline='\n') as index:
        for row in csv.DictReader(csv_file):
            word = (row.get('word') or '').strip()
            key = word.lower()
            if not word or any(c in word for c in '\t\r\n') or key in seen:
                skipped += 1
                continue
            seen.add(key)
            record = {field: row.get(field) or '' for field in FIELDS}
            record['word'] = word
            for field in ('translation', 'definition'):
                record[field] = record[field].replace('\\n', '\n')
            payload = json.dumps(record, ensure_ascii=False, separators=(',', ':')).encode('utf-8')
            offset = data.tell()
            data.write(payload)
            index.write(f'{word}\t{offset}\t{len(payload)}\n')
            count += 1
            if key in ('apple', 'serendipity', 'ephemeral'):
                checks[key] = (offset, len(payload))
    if len(checks) != 3:
        raise ValueError('The source is missing the required verification words.')
    with data_temp.open('rb') as data:
        for word, (offset, length) in checks.items():
            data.seek(offset)
            record = json.loads(data.read(length))
            if record['word'].lower() != word or not record['translation']:
                raise ValueError(f'Random-access verification failed for {word}.')
    data_temp.replace(output / 'dictionary.data')
    index_temp.replace(output / 'dictionary.index.tsv')
    if license_file != output / 'ECDICT-LICENSE.txt':
        shutil.copyfile(license_file, output / 'ECDICT-LICENSE.txt')
    metadata = {
        'source': 'https://github.com/skywind3000/ECDICT',
        'source_commit': COMMIT,
        'source_csv': f'https://raw.githubusercontent.com/skywind3000/ECDICT/{COMMIT}/ecdict.csv',
        'license': 'MIT (see ECDICT-LICENSE.txt)',
        'entries': count,
        'duplicate_or_invalid_rows_skipped': skipped,
        'source_sha256': digest,
        'format': 'Index: UTF-8 TSV without header: spelling, byte offset, byte length. Data: concatenated UTF-8 JSON records.',
        'record_fields': list(FIELDS),
    }
    (output / 'dictionary.meta.json').write_text(json.dumps(metadata, ensure_ascii=False, indent=2), encoding='utf-8')
    (output / 'README-ECDICT.txt').write_text(
        'ECDICT English–Chinese dictionary data\n\n'
        'Source: https://github.com/skywind3000/ECDICT\n'
        f'Source commit: {COMMIT}\n'
        'License: MIT; the complete upstream notice is included in ECDICT-LICENSE.txt.\n'
        f'Entries: {count:,}\n'
        'Format: dictionary.index.tsv contains spelling<TAB>UTF-8 byte offset<TAB>byte length, no header.\n'
        'dictionary.data contains concatenated UTF-8 JSON records at those offsets.\n'
        'Definition and translation literal escaped newlines are normalized to actual newlines.\n'
        'Case-insensitive duplicate spellings use the first upstream occurrence.\n', encoding='utf-8')
    print(f'Built and verified {count:,} entries in {output}; skipped {skipped}.')


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=PACKAGE_ROOT / 'work' / 'ecdict-source' / 'ecdict.csv',
                        help='CSV input; default: <source-package>/work/ecdict-source/ecdict.csv')
    parser.add_argument('--output', type=Path, default=PACKAGE_ROOT / 'data',
                        help='Output directory; default: <source-package>/data')
    parser.add_argument('--license-file', type=Path,
                        help='Upstream license; default: ECDICT-LICENSE.txt beside --source')
    args = parser.parse_args()
    build(args.source, args.output, args.license_file or args.source.parent / 'ECDICT-LICENSE.txt')


if __name__ == '__main__':
    main()

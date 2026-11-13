"""Download the pinned ECDICT source and license using Python 3's standard library."""
from __future__ import annotations

import argparse
from concurrent.futures import ThreadPoolExecutor
import hashlib
import math
from pathlib import Path
import shutil
import time
import urllib.request

PACKAGE_ROOT = Path(__file__).resolve().parent.parent
COMMIT = '39cfb9672c0906b63569e0453c2e6abfb9ab182a'
SOURCE_ROOT = f'https://raw.githubusercontent.com/skywind3000/ECDICT/{COMMIT}'
SOURCE_SHA256 = 'd1ecbdf9ee2b920afb6f430ca6b6bb9f559588cf0dff86e04bd255f9390843cc'


def download(output: Path, workers: int) -> None:
    output = output.resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    url = f'{SOURCE_ROOT}/ecdict.csv'
    if output.is_file() and hashlib.sha256(output.read_bytes()).hexdigest() == SOURCE_SHA256:
        print(f'Already verified: {output}', flush=True)
    else:
        with urllib.request.urlopen(urllib.request.Request(url, method='HEAD'), timeout=60) as response:
            size = int(response.headers['Content-Length'])
        part_size = 8 * 1024 * 1024
        count = math.ceil(size / part_size)
        parts_dir = output.parent / f'{output.name}.parts'
        parts_dir.mkdir(exist_ok=True)

        def fetch(i: int) -> Path:
            begin = i * part_size
            end = min(size, begin + part_size) - 1
            expected = end - begin + 1
            target = parts_dir / f'{i:04d}.part'
            if target.is_file() and target.stat().st_size == expected:
                return target
            for attempt in range(3):
                try:
                    request = urllib.request.Request(url, headers={'Range': f'bytes={begin}-{end}'})
                    with urllib.request.urlopen(request, timeout=90) as response:
                        expected_range = f'bytes {begin}-{end}/{size}'
                        if response.status != 206 or response.headers.get('Content-Range') != expected_range:
                            raise RuntimeError('The server did not return the requested byte range.')
                        with target.open('wb') as stream:
                            shutil.copyfileobj(response, stream, length=1024 * 1024)
                    if target.stat().st_size != expected:
                        raise RuntimeError('The downloaded range has the wrong size.')
                    print(f'Range {i + 1}/{count} complete', flush=True)
                    return target
                except Exception:
                    if attempt == 2:
                        raise
                    time.sleep(2 ** attempt)
            raise AssertionError('Unreachable')

        with ThreadPoolExecutor(max_workers=workers) as pool:
            parts = list(pool.map(fetch, range(count)))
        temporary = output.with_suffix(output.suffix + '.download')
        with temporary.open('wb') as stream:
            for part in parts:
                with part.open('rb') as chunk:
                    shutil.copyfileobj(chunk, stream, length=1024 * 1024)
        digest = hashlib.sha256(temporary.read_bytes()).hexdigest()
        if digest != SOURCE_SHA256:
            raise RuntimeError(f'Source checksum mismatch: {digest}; the existing output was not replaced.')
        temporary.replace(output)
        print(f'Verified {size:,} bytes: {output}', flush=True)
    license_path = output.parent / 'ECDICT-LICENSE.txt'
    with urllib.request.urlopen(f'{SOURCE_ROOT}/LICENSE', timeout=60) as response:
        license_path.write_bytes(response.read())
    print(f'License: {license_path}', flush=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=PACKAGE_ROOT / 'work' / 'ecdict-source' / 'ecdict.csv',
                        help='CSV path; default: <source-package>/work/ecdict-source/ecdict.csv')
    parser.add_argument('--workers', type=int, default=6, help='Concurrent HTTP ranges (1..16)')
    args = parser.parse_args()
    if not 1 <= args.workers <= 16:
        parser.error('--workers must be between 1 and 16')
    download(args.output, args.workers)


if __name__ == '__main__':
    main()

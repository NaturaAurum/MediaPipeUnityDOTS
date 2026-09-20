#!/usr/bin/env python3
"""개발 프로젝트에 UPM 샘플 사본을 명시적으로 임포트한다."""
import argparse
import json
import shutil
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--replace', action='store_true', help='기존 임포트 사본의 수정사항을 버리고 교체')
    args = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    package = root / 'MediaPipeUnityDOTS/Assets/MediaPipeUnityDots'
    manifest = json.loads((package / 'package.json').read_text())
    output = root / 'MediaPipeUnityDOTS/Assets/Samples' / manifest['displayName'] / manifest['version']
    destinations = [(package / sample['path'], output / sample['displayName']) for sample in manifest['samples']]
    for source, target in destinations:
        if not source.is_dir():
            raise SystemExit(f'샘플 원본이 없습니다: {source}')
        if target.exists() and not args.replace:
            raise SystemExit(f'기존 사본을 보존합니다: {target}\n교체하려면 --replace를 명시하세요.')
    for source, target in destinations:
        if target.exists():
            shutil.rmtree(target)
        shutil.copytree(source, target)
        print(f'Imported: {target}')


if __name__ == '__main__':
    main()

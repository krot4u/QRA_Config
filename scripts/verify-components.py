#!/usr/bin/env python3
"""Check the embedded upstream files against the reviewed inventory."""
import hashlib
import argparse
import json
import logging
from pathlib import Path
import zipfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--nuget-packages', type=Path)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    manifest = json.loads((root / 'internal/bundle/manifest.json').read_text())
    inventory = json.loads((root / 'docs/component-inventory.json').read_text())
    archive = root / 'internal/bundle/platform-tools.zip'
    assert hashlib.sha256(archive.read_bytes()).hexdigest() == inventory['adb']['embedded_archive_sha256'], 'Embedded archive hash'
    assert manifest['sha256'] == inventory['adb']['upstream_archive_sha256'], 'Upstream archive hash'
    assert manifest['url'] == inventory['adb']['download_url'], 'Upstream source'
    with zipfile.ZipFile(archive) as bundle:
        assert set(bundle.namelist()) == set(manifest['files']), 'Unexpected bundled files'
        for entry in inventory['adb']['files']:
            content = bundle.read(entry['path'])
            assert hashlib.sha256(content).hexdigest() == entry['sha256'] == manifest['files'][entry['path']], entry['path']
            assert len(content) == entry['size'], entry['path'] + ' size'
        assert 'Pkg.Revision=37.0.1' in bundle.read('source.properties').decode(), 'ADB version'
    logging.info('PASS: five upstream files, provenance, version and SHA-256')
    if args.nuget_packages:
        packs = json.loads((root / 'docs/runtime-pack-inventory.json').read_text())['packs']
        for pack in packs:
            name, version = pack['name'].lower(), pack['version']
            package = args.nuget_packages / name / version / f'{name}.{version}.nupkg'
            assert hashlib.sha256(package.read_bytes()).hexdigest() == pack['sha256'], name
        logging.info('PASS: pinned Microsoft package archive hashes')


if __name__ == '__main__':
    logging.basicConfig(level=logging.INFO, format='%(message)s')
    main()

#!/usr/bin/env python3
"""Build identical inputs in two clean paths; retain one unsigned candidate."""
import argparse
import hashlib
import logging
import os
from pathlib import Path
import shutil
import subprocess
import tempfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    if args.output.exists():
        raise SystemExit('Output must be a new directory; existing candidates are preserved')
    with tempfile.TemporaryDirectory(prefix='qra-repro-') as temporary:
        builds = []
        for name in ('first', 'second'):
            source = Path(temporary) / name
            source.mkdir()
            for entry in ('desktop', 'gui/assets', 'internal/bundle', 'scripts', 'docs', 'global.json', 'Directory.Build.props', 'NuGet.Config', 'LICENSE'):
                path = root / entry
                target = source / entry
                target.parent.mkdir(parents=True, exist_ok=True)
                if path.is_dir():
                    shutil.copytree(path, target, ignore=shutil.ignore_patterns('bin', 'obj'))
                else:
                    shutil.copy2(path, target)
            output = source / 'dist-dotnet'
            env = dict(os.environ, QRA_DOTNET_OUT=str(output))
            subprocess.run(['bash', str(source / 'scripts/build-dotnet.sh')], cwd=source, env=env, check=True)
            builds.append(output)
        expected = sorted(p.name for p in builds[0].iterdir())
        assert expected == sorted(p.name for p in builds[1].iterdir()), 'Output set differs'
        for name in expected:
            assert (builds[0] / name).read_bytes() == (builds[1] / name).read_bytes(), 'Non-reproducible: ' + name
        shutil.copytree(builds[0], args.output)
        digest = hashlib.sha256((args.output / 'QRA-Config.exe').read_bytes()).hexdigest()
        logging.info('PASS: two clean paths, all output bytes identical; unsigned EXE SHA256=%s', digest)


if __name__ == '__main__':
    logging.basicConfig(level=logging.INFO, format='%(message)s')
    main()

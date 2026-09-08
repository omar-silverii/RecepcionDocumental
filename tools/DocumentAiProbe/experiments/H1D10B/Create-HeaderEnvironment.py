"""Recreate only this experiment's environment from audited local wheel bytes."""
import hashlib
import json
import os
import subprocess
import sys
import venv
import zipfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
TARGET = HERE / 'header-local' / '.venv'


def digest(path):
    h = hashlib.sha256()
    with Path(path).open('rb') as f:
        for chunk in iter(lambda: f.read(1048576), b''):
            h.update(chunk)
    return h.hexdigest().upper()


def main():
    assert sys.version_info[:3] == (3, 11, 3)
    packages = json.loads((HERE / 'header-offline-packages.json').read_text())['Packages']
    assert len(packages) == 21
    for p in packages:
        assert digest(p['CachePath']) == p['Sha256'], p['Name']
    if TARGET.exists():
        # Resume only our exact isolated path; never remove or alter existing files.
        assert TARGET.resolve() == (HERE / 'header-local' / '.venv').resolve()
        cfg = (TARGET / 'pyvenv.cfg').read_text()
        assert 'include-system-site-packages = false' in cfg
        assert 'version = 3.11.3' in cfg
    else:
        venv.EnvBuilder(with_pip=False, system_site_packages=False).create(TARGET)
    site = TARGET / 'Lib' / 'site-packages'
    # Wheel .data/data (SymPy manual page) is installed under the isolated prefix.
    for p in packages:
        with zipfile.ZipFile(p['CachePath']) as z:
            for entry in z.infolist():
                if '.data/' in entry.filename:
                    rest = entry.filename.split('.data/', 1)[1]
                    scheme, name = rest.split('/', 1)
                    roots = {'data': TARGET, 'purelib': site, 'platlib': site,
                             'scripts': TARGET / 'Scripts', 'headers': TARGET / 'Include'}
                    resolved = (roots[scheme] / name).resolve()
                else:
                    resolved = (site / entry.filename).resolve()
                assert resolved.is_relative_to(TARGET.resolve()), entry.filename
                dest = Path('\\\\?\\' + str(resolved))
                if entry.is_dir():
                    dest.mkdir(parents=True, exist_ok=True)
                    continue
                content = z.read(entry)
                if dest.exists():
                    assert dest.read_bytes() == content, 'Existing local package differs: ' + str(resolved)
                else:
                    dest.parent.mkdir(parents=True, exist_ok=True)
                    dest.write_bytes(content)
    result = {'Python': str(TARGET / 'Scripts' / 'python.exe'), 'Venv': str(TARGET),
              'SystemSitePackages': False, 'WithPip': False, 'InternetDownloads': 0,
              'Packages': packages, 'PythonGlobalModified': False}
    (HERE / 'header-environment-install.json').write_text(json.dumps(result, indent=2))
    print(result['Python'], flush=True)


if __name__ == '__main__':
    main()

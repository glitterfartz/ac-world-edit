#!/usr/bin/env python3
"""Run the WorldCorpusTests StormLib differential check against the pinned official StormLib 9.40 DLL."""
import argparse
import hashlib
import os
import subprocess
import sys
import urllib.request
import zipfile
from pathlib import Path

URL = 'https://github.com/ladislav-zezula/StormLib/releases/download/v9.40/stormlib_dll.zip'
ZIP_SHA256 = 'B2C9635E7B63EDEE1BD7C82E7DC180D739F3ACCB2B8994804C7774E464CE89AE'
DLL_SHA256 = '93321F6F030BE5D7D79EB4CBF433D6EF7E83CE20D47D8963717F207D54AFBE16'


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--data-path', default='wow-data')
    args = parser.parse_args()

    root = Path(__file__).resolve().parent.parent
    os.chdir(root)
    tool_root = root / 'artifacts' / 'stormlib-9.40'
    zip_path = tool_root / 'stormlib_dll.zip'
    dll_path = tool_root / 'dll' / 'x64' / 'StormLib.dll'
    tool_root.mkdir(parents=True, exist_ok=True)
    if not zip_path.exists():
        urllib.request.urlretrieve(URL, zip_path)
    if sha256(zip_path) != ZIP_SHA256:
        sys.exit('StormLib 9.40 official release SHA256 mismatch')
    if not dll_path.exists():
        with zipfile.ZipFile(zip_path) as z:
            z.extractall(tool_root / 'dll')
    if sha256(dll_path) != DLL_SHA256:
        sys.exit('StormLib x64 DLL does not match the verified release extraction')

    env = os.environ.copy()
    env['ACWORLD_DATA_DIR'] = str(Path(args.data_path).resolve(strict=True))
    env['STORMLIB_DLL'] = str(dll_path)
    code = subprocess.call(['dotnet', 'test', 'Tests/ACWorldEdit.Tests', '--settings',
                            'Tests/ACWorldEdit.Tests/corpus.runsettings', '-p:NuGetAudit=false',
                            '--filter', 'FullyQualifiedName~WorldCorpusTests'], env=env)
    if code != 0:
        sys.exit('StormLib differential check failed')


main()

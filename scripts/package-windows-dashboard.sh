#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
NAME="TSLoadTestingTool-Dashboard-2026.10.04.9-win-x64"
DIST="$ROOT/dist/$NAME"
# This script replaces only its own generated distribution.
rm -rf "$DIST" "$ROOT/dist/$NAME.zip"
mkdir -p "$DIST/UI" "$DIST/Runner" "$DIST/Instances"
cd "$ROOT"
dotnet publish LoadTestingTool.csproj -c Release -r win-x64 --self-contained true --tl:off -o "$DIST/Runner"
dotnet publish src/LoadTestingTool.UI/LoadTestingTool.UI.csproj -c Release -r win-x64 --self-contained true --tl:off -o "$DIST/UI"
export ROOT DIST
python3 - <<'PY'
import os, json, shutil, subprocess
from pathlib import Path
root, target = Path(os.environ['ROOT']), Path(os.environ['DIST'])
for instance in (root / 'Instances').glob('DataEngine_*'):
    if not instance.is_dir():
        continue
    dest = target / 'Instances' / instance.name
    dest.mkdir(parents=True, exist_ok=True)
    for f in instance.iterdir():
        if f.is_file() and (f.suffix.lower() == '.xlsx' or f.name in ('appsettings.json', 'README.md')):
            if not f.name.startswith('~$') and '.backup.' not in f.name:
                shutil.copy2(f, dest / f.name)
    for directory in ('Templates', 'Queries', 'Scripts', 'Inputs'):
        if (instance / directory).is_dir():
            shutil.copytree(instance / directory, dest / directory)
    for directory in ('Artifacts', 'History', 'Logs', 'Results'):
        (dest / directory).mkdir(exist_ok=True)
manual = root / 'APPLICATION_GUIDE.pdf'
if manual.is_file():
    shutil.copy2(manual, target / manual.name)
manifest = {
    'build': 'Dashboard 2026.10.04.9',
    'sourceCommit': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root, text=True).strip(),
    'runtime': 'win-x64', 'selfContained': True, 'configuration': 'Release'
}
(target / 'BUILD_INFO.json').write_text(json.dumps(manifest, indent=2) + '\n')
(target / 'README.md').write_text('''# Load Testing Tool — Dashboard 2026.10.04.9

Extract the entire archive to a NEW folder, then launch `UI/LoadTestingTool.UI.exe`.
Do not download or copy only the EXE: its sibling DLLs and bundled runtime are required.

The sidebar and title identify this build as **Dashboard 2026.10.04.9**.
The package includes the .NET runtime, `UI/`, `Runner/`, `Instances/`, the manual, and build metadata.
The UI launches `Runner/LoadTestingTool.exe` directly, without system `dotnet`.

## Usage

1. Select an instance row and click **Choose what to run**. Tick named testcases, steps and environments, then Apply.
   Execution mode, testcase order and data IDs remain in **Config**.
2. Applying selection checks the instance for you. Click **Run selected**.
3. Use **Active runs** for processes currently running, **History** for saved result workbooks,
   and **Settings** for resolved paths and the internal-log switch.
4. Use **Overview**, **Assertions** and **Artifacts** for selected-run details.

**Force stop** terminates the process tree and may leave incomplete artifacts.
Files and folders open with your system default applications (no forced Excel or PDF reader).
Path changes are made in `UI/appsettings.json`, followed by restarting the UI.
No complete-run timeout was added.
''')
for directory, executable in (('UI', 'LoadTestingTool.UI.exe'), ('Runner', 'LoadTestingTool.exe')):
    for f in (executable, 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll'):
        assert (target / directory / f).stat().st_size > 0, f'Missing runtime file: {directory}/{f}'
    config = json.loads(next((target / directory).glob('*.runtimeconfig.json')).read_text())
    assert 'includedFrameworks' in config['runtimeOptions'], 'Publish is not self-contained'
ui = json.loads((target / 'UI' / 'appsettings.json').read_text())
assert (target / 'UI' / ui['RunnerDll']).resolve().is_file()
assert (target / 'UI' / ui['InstancesRoot']).resolve().is_dir()
assert list((target / 'Instances').glob('DataEngine_*/*.xlsx')), 'No workbook packaged'
print('Publish layout and bundled runtime validated.')
PY
cd "$ROOT/dist"
zip -qr "$NAME.zip" "$NAME"
unzip -t "$NAME.zip" | tail -1
sha256sum "$NAME.zip" | tee "$NAME.zip.sha256"
printf 'Package: %s\n' "$ROOT/dist/$NAME.zip"

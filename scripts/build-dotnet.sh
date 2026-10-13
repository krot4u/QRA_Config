#!/usr/bin/env bash
set -euo pipefail
root_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet_bin="${QRA_DOTNET:-dotnet}"
output_dir="${QRA_DOTNET_OUT:-$root_dir/dist-dotnet}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_GENERATE_ASPNET_CERTIFICATE=false
export DOTNET_CLI_HOME="${QRA_DOTNET_CLI_HOME:-${TMPDIR:-/tmp}/qra-config-dotnet-cli}"
export NUGET_PACKAGES="${QRA_NUGET_PACKAGES:-${TMPDIR:-/tmp}/qra-config-nuget}"
cd "$root_dir"
python3 "$root_dir/scripts/verify-components.py"
"$dotnet_bin" restore "$root_dir/desktop/QraConfig.csproj" --runtime win-x64 --locked-mode \
  -p:PublishSingleFile=true
python3 "$root_dir/scripts/verify-components.py" --nuget-packages "$NUGET_PACKAGES"
"$dotnet_bin" publish "$root_dir/desktop/QraConfig.csproj" \
  --no-restore \
  --configuration Release --runtime win-x64 --self-contained true \
  --output "$output_dir" \
  -p:EnableWindowsTargeting=true -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=false \
  -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false
if [[ ! -f "$output_dir/QRA-Config.exe" ]]; then
  echo 'Publish completed without QRA-Config.exe' >&2
  exit 1
fi
unzip -p "$root_dir/internal/bundle/platform-tools.zip" NOTICE.txt > "$output_dir/ADB-NOTICE.txt"
cp "$root_dir/docs/third-party-notices.md" "$output_dir/THIRD-PARTY-NOTICES.md"
cp "$root_dir/LICENSE" "$output_dir/LICENSE"
python3 - "$root_dir/desktop/obj/project.assets.json" "$output_dir" <<'PY_LICENSES'
import json, pathlib, shutil, sys
assets = json.loads(pathlib.Path(sys.argv[1]).read_text())
output = pathlib.Path(sys.argv[2])
for package, files in {
    "microsoft.netcore.app.runtime.win-x64": {"LICENSE.TXT": "DOTNET-LICENSE.txt", "THIRD-PARTY-NOTICES.TXT": "DOTNET-THIRD-PARTY-NOTICES.txt"},
    "microsoft.windowsdesktop.app.runtime.win-x64": {"LICENSE": "WPF-LICENSE.txt"},
}.items():
    dependency = next(item for framework in assets["project"]["frameworks"].values() for item in framework.get("downloadDependencies", []) if item["name"].lower() == package)
    resolved = package + "/" + dependency["version"].strip("[]").split(",")[0].strip()
    base = next(pathlib.Path(folder) / resolved.lower() for folder in assets["packageFolders"] if (pathlib.Path(folder) / resolved.lower()).is_dir())
    for source, target in files.items():
        shutil.copyfile(base / source, output / target)
PY_LICENSES
python3 - "$output_dir" <<'PY_HASHES'
import hashlib, pathlib, sys
output = pathlib.Path(sys.argv[1])
names = ['QRA-Config.exe', 'ADB-NOTICE.txt', 'THIRD-PARTY-NOTICES.md', 'DOTNET-LICENSE.txt', 'DOTNET-THIRD-PARTY-NOTICES.txt', 'WPF-LICENSE.txt', 'LICENSE']
(output / 'SHA256SUMS').write_text(''.join(f'{hashlib.sha256((output / name).read_bytes()).hexdigest()}  {name}\n' for name in names))
PY_HASHES
echo "Built native C#/.NET Windows x64 EXE: $output_dir/QRA-Config.exe"
echo 'Publication requires separate Windows and Defender verification.'

#!/usr/bin/env bash

set -euo pipefail

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd "${script_directory}/.." && pwd)"
repository_root="$(cd "${project_root}/../.." && pwd)"
plugin_directory="${project_root}/Assets/Plugins/GamePatchKit"
runtime_output="${repository_root}/src/GamePatchKit.Runtime/bin/Release/netstandard2.1"

dotnet build "${repository_root}/src/GamePatchKit.Runtime/GamePatchKit.Runtime.csproj" \
  -c Release \
  -p:CopyLocalLockFileAssemblies=true

mkdir -p "${plugin_directory}"
install -m 0644 "${runtime_output}/GamePatchKit.Core.dll" "${plugin_directory}/GamePatchKit.Core.dll"
install -m 0644 "${runtime_output}/GamePatchKit.Runtime.dll" "${plugin_directory}/GamePatchKit.Runtime.dll"
install -m 0644 "${runtime_output}/BouncyCastle.Cryptography.dll" "${plugin_directory}/BouncyCastle.Cryptography.dll"

echo "Prepared Unity managed plugins in ${plugin_directory}"

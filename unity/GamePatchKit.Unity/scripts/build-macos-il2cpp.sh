#!/usr/bin/env bash

set -euo pipefail

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd "${script_directory}/.." && pwd)"
unity_editor_path="${GPK_UNITY_EDITOR_PATH:-/Applications/Unity/Hub/Editor/6000.4.4f1/Unity.app/Contents/MacOS/Unity}"
unity_contents="$(cd "$(dirname "${unity_editor_path}")/.." && pwd)"
variations_directory="${unity_contents}/PlaybackEngines/MacStandaloneSupport/Variations"

shopt -s nullglob
il2cpp_player_variations=("${variations_directory}"/macos_*_player_*_il2cpp)
shopt -u nullglob

if (( ${#il2cpp_player_variations[@]} == 0 )); then
  echo "macOS Player IL2CPP support is not installed for ${unity_editor_path}." >&2
  echo "Install 'Mac Build Support (IL2CPP)' for the same Unity Editor version." >&2
  exit 1
fi

"${script_directory}/prepare.sh"
mkdir -p "${project_root}/Logs"

"${unity_editor_path}" \
  -batchmode \
  -nographics \
  -quit \
  -projectPath "${project_root}" \
  -executeMethod GamePatchKit.Unity.Editor.GamePatchKitUnityBuild.BuildMacOsIl2Cpp \
  -logFile "${project_root}/Logs/macos-il2cpp-build.log"

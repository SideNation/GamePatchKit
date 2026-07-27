#!/usr/bin/env bash

set -euo pipefail

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd "${script_directory}/.." && pwd)"
unity_editor_path="${GPK_UNITY_EDITOR_PATH:-/Applications/Unity/Hub/Editor/6000.4.4f1/Unity.app/Contents/MacOS/Unity}"

"${script_directory}/prepare.sh"
mkdir -p "${project_root}/Logs" "${project_root}/TestResults"

"${unity_editor_path}" \
  -batchmode \
  -nographics \
  -projectPath "${project_root}" \
  -runTests \
  -testPlatform EditMode \
  -testResults "${project_root}/TestResults/editmode.xml" \
  -logFile "${project_root}/Logs/editmode-tests.log"

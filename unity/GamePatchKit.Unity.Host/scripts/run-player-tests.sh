#!/usr/bin/env bash
# 같은 테스트를 macOS IL2CPP Player로 빌드해 실행한다. 6000.4.4f1의 Mac Build Support (IL2CPP) 모듈이 필요하다.
set -euo pipefail

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project_path="$(cd "${script_directory}/.." && pwd)"
unity_editor="${GPK_UNITY_EDITOR_PATH:-/Applications/Unity/Hub/Editor/6000.4.4f1/Unity.app/Contents/MacOS/Unity}"
results_path="${project_path}/TestResults/player-osx-il2cpp.xml"

mkdir -p "${project_path}/Logs" "${project_path}/TestResults"
rm -f "${results_path}"

set +e
"${unity_editor}" \
    -batchmode \
    -projectPath "${project_path}" \
    -runTests \
    -testPlatform StandaloneOSX \
    -testSettingsFile "${script_directory}/StandaloneOsxIl2CppTestSettings.json" \
    -testResults "${results_path}" \
    -logFile "${project_path}/Logs/player-tests.log"
exit_code=$?
set -e

if [[ -f "${results_path}" ]]; then
    grep -o -m1 '<test-run [^>]*>' "${results_path}"
fi

exit "${exit_code}"

#!/usr/bin/env bash
# 호스트 프로젝트에서 패키지의 PlayMode 테스트를 Unity 에디터 batchmode로 실행한다.
set -euo pipefail

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project_path="$(cd "${script_directory}/.." && pwd)"
unity_editor="${GPK_UNITY_EDITOR_PATH:-/Applications/Unity/Hub/Editor/6000.4.4f1/Unity.app/Contents/MacOS/Unity}"
results_path="${project_path}/TestResults/playmode.xml"

mkdir -p "${project_path}/Logs" "${project_path}/TestResults"
rm -f "${results_path}"

set +e
"${unity_editor}" \
    -batchmode \
    -nographics \
    -projectPath "${project_path}" \
    -runTests \
    -testPlatform PlayMode \
    -testResults "${results_path}" \
    -logFile "${project_path}/Logs/playmode-tests.log"
exit_code=$?
set -e

if [[ -f "${results_path}" ]]; then
    grep -o -m1 '<test-run [^>]*>' "${results_path}"
fi

exit "${exit_code}"

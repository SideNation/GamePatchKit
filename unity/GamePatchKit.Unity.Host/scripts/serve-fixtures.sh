#!/usr/bin/env bash
# 테스트 픽스처 버킷(실제 gpk build 산출물)을 정적 HTTP로 띄운다. 샘플 씬의 기본 Base URL은 http://127.0.0.1:8765/ 다.
# 기기에서 접속하려면 bind 주소를 0.0.0.0으로 주고 PC의 LAN IP를 Base URL에 넣는다.
#   bash serve-fixtures.sh [bind=127.0.0.1] [port=8765]
set -euo pipefail

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
bucket_path="$(cd "${script_directory}/../../../src/GamePatchKit.Unity/Tests/Runtime/Fixtures/bucket" && pwd)"
bind_address="${1:-127.0.0.1}"
port="${2:-8765}"

echo "serving ${bucket_path} at http://${bind_address}:${port}/"
python3 -m http.server "${port}" --bind "${bind_address}" --directory "${bucket_path}"

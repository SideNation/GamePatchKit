#!/usr/bin/env bash
#
# Unity QuickStart의 서버 쪽. compression:none release를 만들고 publish tree를 로컬
# HTTP로 서빙한 뒤, Unity Inspector에 넣을 세 값을 출력한다. client 쪽 절차는
# docs/guide/unity-quickstart.md 에 있다.
#
#   ./serve.sh            # http://127.0.0.1:8080/
#   PORT=9000 ./serve.sh  # 다른 포트
#
# 생성물은 전부 .work/ 아래에 만들고 실행할 때마다 지운다. Ctrl+C로 종료한다.
#
set -euo pipefail

cd "$(dirname "$0")"
SAMPLE_DIR="$PWD"
REPO_ROOT="$(cd ../.. && pwd)"
WORK="$SAMPLE_DIR/.work"
PORT="${PORT:-8080}"

CLI_PROJECT="$REPO_ROOT/src/GamePatchKit.Cli/GamePatchKit.Cli.csproj"
CLI_DLL="$REPO_ROOT/src/GamePatchKit.Cli/bin/Release/net10.0/GamePatchKit.Cli.dll"

for tool in dotnet python3; do
    command -v "$tool" >/dev/null || { echo "error: '$tool' 이 필요하다." >&2; exit 1; }
done

# gpk의 --json 출력은 canonical JSON 한 줄이라 그대로 파싱할 수 있다.
json_get() {
    python3 -c '
import json, sys
value = json.load(sys.stdin)
for key in sys.argv[1:]:
    value = value[key]
print(value if isinstance(value, str) else json.dumps(value))' "$@"
}

dotnet build "$CLI_PROJECT" -c Release -v q --nologo

rm -rf "$WORK"
mkdir -p "$WORK"
dotnet "$CLI_DLL" package \
    --config gamepatchkit.yml \
    --output-root "$WORK/publish" \
    --json > "$WORK/package.json"

printf '\n\033[1m== Unity Inspector에 넣을 값 ==\033[0m\n'
printf '  Base Url      : http://127.0.0.1:%s/\n' "$PORT"
printf '  Package Id    : %s\n' "$(json_get result packageId < "$WORK/package.json")"
printf '  Data Version  : %s\n' "$(json_get result identity dataVersion < "$WORK/package.json")"
printf '  Manifest Hash : %s\n' "$(json_get result identity manifestHash < "$WORK/package.json")"
printf '\n서빙 중: %s (Ctrl+C로 종료)\n\n' "$WORK/publish"

# loopback에만 bind한다. 다른 기기에서 접근하려면 --bind를 바꾸는 대신 Player
# Settings의 Allow downloads over HTTP를 먼저 확인해야 한다.
exec python3 -m http.server "$PORT" --directory "$WORK/publish" --bind 127.0.0.1

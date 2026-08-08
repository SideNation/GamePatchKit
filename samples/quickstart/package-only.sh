#!/usr/bin/env bash
#
# GamePatchKit quickstart의 package 전용 샘플.
# 원본 game-data/는 건드리지 않고 .work/package-only/에 release를 만든다.

set -euo pipefail

cd "$(dirname "$0")"
SAMPLE_DIR="$PWD"
REPO_ROOT="$(cd ../.. && pwd)"
WORK="$SAMPLE_DIR/.work/package-only"
CLI_PROJECT="$REPO_ROOT/src/GamePatchKit.Cli/GamePatchKit.Cli.csproj"
CLI_DLL="$REPO_ROOT/src/GamePatchKit.Cli/bin/Release/net10.0/GamePatchKit.Cli.dll"

for tool in dotnet python3; do
    command -v "$tool" >/dev/null || { echo "error: '$tool' 이 필요하다." >&2; exit 1; }
done

echo "== 빌드 =="
dotnet build "$CLI_PROJECT" -c Release -v q --nologo

rm -rf "$WORK"
mkdir -p "$WORK"
cp -R game-data "$WORK/game-data"
cp gamepatchkit.yml "$WORK/gamepatchkit.yml"
cd "$WORK"

echo "== package: 최초 release 생성 =="
dotnet "$CLI_DLL" package --config gamepatchkit.yml --output-root publish --json > package.json

python3 - <<'PY'
import json

identity = json.load(open("package.json", encoding="utf-8"))["result"]["identity"]
print(f"dataVersion   : {identity['dataVersion']}")
print(f"compactVersion: {identity['compactVersion']}")
print(f"manifestHash  : {identity['manifestHash']}")
PY

echo "== 생성된 publish tree =="
find publish -type f | sort | sed 's/^/  /'
echo
echo "완료. 생성물: $WORK/publish"

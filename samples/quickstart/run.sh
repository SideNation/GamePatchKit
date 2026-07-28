#!/usr/bin/env bash
#
# GamePatchKit quickstart 샘플. package -> verify -> sign -> Runtime 설치 ->
# incremental -> compact 를 한 번에 실행한다. 생성물은 전부 .work/ 아래에 만들고
# 실행할 때마다 지운다.
#
#   ./run.sh            # 전체 실행
#   PORT=9000 ./run.sh  # 다른 포트로 publish tree 서빙
#
set -euo pipefail

cd "$(dirname "$0")"
SAMPLE_DIR="$PWD"
REPO_ROOT="$(cd ../.. && pwd)"
WORK="$SAMPLE_DIR/.work"
PORT="${PORT:-8080}"

CLI_PROJECT="$REPO_ROOT/src/GamePatchKit.Cli/GamePatchKit.Cli.csproj"
CLIENT_PROJECT="$SAMPLE_DIR/QuickStartClient/QuickStartClient.csproj"
CLI_DLL="$REPO_ROOT/src/GamePatchKit.Cli/bin/Release/net10.0/GamePatchKit.Cli.dll"
CLIENT_DLL="$SAMPLE_DIR/QuickStartClient/bin/Release/net10.0/QuickStartClient.dll"

for tool in dotnet python3; do
    command -v "$tool" >/dev/null || { echo "error: '$tool' 이 필요하다." >&2; exit 1; }
done

banner() { printf '\n\033[1m== %s ==\033[0m\n' "$1"; }

# gpk의 --json 출력은 canonical JSON 한 줄이라 그대로 파싱할 수 있다.
json_get() {
    python3 -c '
import json, sys
value = json.load(sys.stdin)
for key in sys.argv[1:]:
    value = value[key]
print(value if isinstance(value, str) else json.dumps(value))' "$@"
}

gpk() { dotnet "$CLI_DLL" "$@"; }

banner "0. 빌드"
dotnet build "$CLI_PROJECT" -c Release -v q --nologo
dotnet build "$CLIENT_PROJECT" -c Release -v q --nologo

rm -rf "$WORK"
mkdir -p "$WORK"
# 이 샘플은 game-data/ 를 그대로 두기 위해 사본에서 작업한다. incremental 단계에서
# 파일 하나를 바꾸기 때문이다.
cp -R game-data "$WORK/game-data"
cp gamepatchkit.yml "$WORK/gamepatchkit.yml"
cd "$WORK"

banner "1. package: 최초 release 생성"
gpk package --config gamepatchkit.yml --output-root publish --json > package.json
MANIFEST_HASH=$(json_get result identity manifestHash < package.json)
DATA_VERSION=$(json_get result identity dataVersion < package.json)
echo "dataVersion   : $DATA_VERSION"
echo "compactVersion: $(json_get result identity compactVersion < package.json)"
echo "manifestHash  : $MANIFEST_HASH"

banner "2. publish tree"
find publish -type f | sort | sed 's/^/  /'

banner "3. verify: schema -> 참조 무결성 -> artifact byte -> signature"
gpk verify --output-root publish --package-id sample-game-client-data \
    --manifest-hash "$MANIFEST_HASH" --json > verify.json
echo "signature.state: $(json_get result signature state < verify.json)"

banner "4. sign: canonical manifest에 Ed25519 서명"
if openssl genpkey -algorithm ed25519 -out signing-key.pem 2>/dev/null; then
    # PKCS#8 DER의 마지막 32 byte가 raw seed, SPKI DER의 마지막 32 byte가 raw public key다.
    to_base64url() { base64 | tr '+/' '-_' | tr -d '=\n'; }
    GPK_SIGNING_KEY=$(openssl pkey -in signing-key.pem -outform DER | tail -c 32 | to_base64url)
    PUBLIC_KEY=$(openssl pkey -in signing-key.pem -pubout -outform DER | tail -c 32 | to_base64url)
    export GPK_SIGNING_KEY

    gpk sign --output-root publish --package-id sample-game-client-data \
        --manifest-hash "$MANIFEST_HASH" --key-env GPK_SIGNING_KEY --json > sign.json
    echo "keyId: $(json_get result keyId < sign.json)"

    # --trusted-key 없이 verify하면 signature.state가 'present'에 그친다.
    # 그것은 "문서 형식이 맞다"는 뜻일 뿐 서명 증거가 아니다.
    gpk verify --output-root publish --package-id sample-game-client-data \
        --manifest-hash "$MANIFEST_HASH" \
        --trusted-key "$PUBLIC_KEY" --require-signature --json > verify-signed.json
    echo "signature.state: $(json_get result signature state < verify-signed.json)  (신뢰 key로 실제 검증됨)"
else
    echo "openssl이 ed25519를 지원하지 않아 서명 단계를 건너뛴다."
fi

banner "5. Runtime: publish tree를 HTTP로 서빙하고 설치"
python3 -m http.server "$PORT" --directory publish >/dev/null 2>&1 &
SERVER_PID=$!
trap 'kill "$SERVER_PID" 2>/dev/null || true' EXIT

for _ in $(seq 1 50); do
    curl -fsS "http://127.0.0.1:$PORT/" >/dev/null 2>&1 && break
    sleep 0.2
done

dotnet "$CLIENT_DLL" \
    "http://127.0.0.1:$PORT/" \
    "$WORK/runtime-root" \
    sample-game-client-data \
    "$DATA_VERSION" \
    "$MANIFEST_HASH"

# kill 뒤 wait까지 묶어서 조용히 끝낸다. 그러지 않으면 bash의 job 알림이
# "Terminated"를 출력에 섞는다.
{ kill "$SERVER_PID" && wait "$SERVER_PID"; } 2>/dev/null || true
trap - EXIT

banner "6. package-state.json"
python3 -m json.tool "runtime-root/packages/sample-game-client-data/state/package-state.json" | sed 's/^/  /'

banner "7. incremental: bundle group의 파일 하나 변경"
printf 'forest-map-payload-v2\n' > game-data/maps/forest.dat
gpk package --config gamepatchkit.yml --output-root publish \
    --previous "$MANIFEST_HASH" --json > package2.json
NEXT_HASH=$(json_get result identity manifestHash < package2.json)
echo "reused file artifacts : $(json_get result artifacts reusedFileArtifactCount < package2.json)"
echo "created file artifacts: $(json_get result artifacts createdFileArtifactCount < package2.json)  (bundle group 전체가 file override로 전환)"
echo "manifestHash          : $NEXT_HASH"

banner "8. compact: maps group을 새 bundle baseline으로"
gpk compact --config gamepatchkit.yml --output-root publish \
    --source "$NEXT_HASH" --group maps --retained "$MANIFEST_HASH" --json > compact.json
echo "changed       : $(json_get result changed < compact.json)"
echo "dataVersion   : $(json_get result identity dataVersion < compact.json)  (incremental과 동일 - 논리 상태 불변)"
echo "compactVersion: $(json_get result identity compactVersion < compact.json)  (0 -> 1)"
echo "manifestHash  : $(json_get result identity manifestHash < compact.json)  (새 값)"

printf '\n\033[1m완료.\033[0m 생성물은 %s 에 있다.\n' "$WORK"

#!/usr/bin/env bash
# 실제 gpk build 산출물로 Unity EditMode 테스트 픽스처를 다시 만든다.
# 결과는 src/GamePatchKit.Unity/Tests/Runtime/Fixtures 아래에 게시 버킷 트리(bucket/)와 세대별 원본 트리(source/)로 남는다.
set -euo pipefail

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repository_root="$(cd "${script_directory}/../../.." && pwd)"
fixtures_path="${repository_root}/src/GamePatchKit.Unity/Tests/Runtime/Fixtures"
work_path="$(mktemp -d)"
trap 'rm -rf "${work_path}"' EXIT

dotnet build "${repository_root}/src/GamePatchKit.Cli/GamePatchKit.Cli.csproj" --configuration Release --nologo --verbosity quiet
gpk=(dotnet "${repository_root}/src/GamePatchKit.Cli/bin/Release/net10.0/gpk.dll")

repository_path="${work_path}/repository"
source_path="${repository_path}/data"
output_path="${work_path}/output"

git init --quiet --initial-branch=main --object-format=sha1 "${repository_path}"
git -C "${repository_path}" config core.autocrlf false
git -C "${repository_path}" config commit.gpgSign false
git -C "${repository_path}" config user.name "GamePatchKit Fixture"
git -C "${repository_path}" config user.email "gamepatchkit-fixture@example.invalid"

commit() {
    git -C "${repository_path}" add --all
    GIT_AUTHOR_DATE="$1" GIT_COMMITTER_DATE="$1" git -C "${repository_path}" commit --quiet -m "$2"
}

publish() {
    local release_version="$1"
    "${gpk[@]}" build --source "${source_path}" --output "${output_path}"
    "${gpk[@]}" verify --output "${output_path}"
    grep -Fq "\"releaseVersion\":${release_version}," "${output_path}/manifest.json"
    mkdir -p "${fixtures_path}/bucket/manifests" "${fixtures_path}/source/${release_version}"
    cp "${output_path}/manifest.json" "${fixtures_path}/bucket/manifests/${release_version}.json"
    (cd "${source_path}" && find . -type f -not -name gamepatchkit.yml | while IFS= read -r path; do
        mkdir -p "${fixtures_path}/source/${release_version}/$(dirname "${path}")"
        cp "${path}" "${fixtures_path}/source/${release_version}/${path}"
    done)
}

rm -rf "${fixtures_path}/bucket" "${fixtures_path}/source"
mkdir -p "${source_path}/content/maps" "${source_path}/raw"
printf '%s\n' 'groups:' '  - id: content' '    version: 1' '    packing: group' '    compression: zstd' \
    '  - id: raw' '    version: 1' '    packing: file' '    compression: none' > "${source_path}/gamepatchkit.yml"
printf '{"name":"desert","width":64,"height":64,"tiles":"sand sand sand rock sand sand water"}\n' > "${source_path}/content/maps/desert.json"
printf '[{"id":"knight","hp":120},{"id":"archer","hp":80}]\n' > "${source_path}/content/units.json"
printf 'tick-rate=30\nregion=kr\n' > "${source_path}/raw/config.txt"
commit '2000-01-01T00:00:00Z' 'release 0'
publish 0

printf '[{"id":"knight","hp":130},{"id":"archer","hp":80},{"id":"mage","hp":60}]\n' > "${source_path}/content/units.json"
printf '{"name":"forest","width":32,"height":32,"tiles":"tree tree grass tree grass water"}\n' > "${source_path}/content/maps/forest.json"
printf 'tick-rate=60\nregion=kr\n' > "${source_path}/raw/config.txt"
commit '2000-01-02T00:00:00Z' 'release 1'
publish 1

cp -R "${output_path}/archives" "${fixtures_path}/bucket/archives"
cp -R "${output_path}/files" "${fixtures_path}/bucket/files"
find "${fixtures_path}" -type f | sort

set -euo pipefail

tool_path=$1
work_path=$2

if [[ ! -f "$tool_path" ]]; then
    echo "gpk 실행 파일이 없습니다: $tool_path" >&2
    exit 1
fi

if [[ -e "$work_path" ]]; then
    echo "smoke 작업 폴더가 이미 있습니다: $work_path" >&2
    exit 1
fi

repository_path="$work_path/repository"
source_path="$repository_path/data"
group_path="$source_path/content"
output_path="$work_path/output"

git init --initial-branch=main --object-format=sha1 "$repository_path"
git -C "$repository_path" config core.autocrlf false
git -C "$repository_path" config commit.gpgSign false
git -C "$repository_path" config user.name "GamePatchKit CI"
git -C "$repository_path" config user.email "gamepatchkit-ci@example.invalid"
mkdir -p "$group_path"
printf '%s\n' \
    'groups:' \
    '  - id: content' \
    '    version: 1' \
    '    packing: group' \
    '    compression: zstd' \
    > "$source_path/gamepatchkit.yml"
printf 'GamePatchKit zstd smoke payload\n' > "$group_path/payload.bin"
git -C "$repository_path" add --all
GIT_AUTHOR_DATE='2000-01-01T00:00:00Z' \
    GIT_COMMITTER_DATE='2000-01-01T00:00:00Z' \
    git -C "$repository_path" commit -m 'smoke input'

"$tool_path" build --source "$source_path" --output "$output_path"
"$tool_path" verify --output "$output_path"

manifest_path="$output_path/manifest.json"
archive_path="$output_path/archives/content/1.gpka"

if [[ ! -f "$manifest_path" ]]; then
    echo 'smoke 매니페스트가 만들어지지 않았습니다.' >&2
    exit 1
fi

if [[ ! -f "$archive_path" ]]; then
    echo 'smoke zstd 아카이브가 만들어지지 않았습니다.' >&2
    exit 1
fi

grep -Fq '"schemaVersion":1' "$manifest_path"
grep -Fq '"id":"content"' "$manifest_path"
grep -Fq '"compression":"zstd"' "$manifest_path"
grep -Fq '"name":"archives/content/1.gpka"' "$manifest_path"
printf 'Smoke output: %s\n' "$output_path"

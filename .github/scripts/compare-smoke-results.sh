set -euo pipefail

root_path=$1
expected_result_count=${2:-3}

if [[ ! -d "$root_path" ]]; then
    echo "smoke 결과 폴더가 없습니다: $root_path" >&2
    exit 1
fi

manifests=()
archives=()

while IFS= read -r path; do
    manifests+=("$path")
done < <(find "$root_path" -type f -name manifest.json | sort)

while IFS= read -r path; do
    archives+=("$path")
done < <(find "$root_path" -type f -name '*.gpka' | sort)

if [[ ${#manifests[@]} -ne $expected_result_count ]]; then
    echo "manifest.json이 ${expected_result_count}개 필요하지만 ${#manifests[@]}개입니다." >&2
    exit 1
fi

if [[ ${#archives[@]} -ne $expected_result_count ]]; then
    echo "아카이브가 ${expected_result_count}개 필요하지만 ${#archives[@]}개입니다." >&2
    exit 1
fi

hash_file() {
    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$1" | awk '{ print $1 }'
    else
        shasum -a 256 "$1" | awk '{ print $1 }'
    fi
}

manifest_hash=$(hash_file "${manifests[0]}")
archive_hash=$(hash_file "${archives[0]}")

for path in "${manifests[@]:1}"; do
    if [[ $(hash_file "$path") != "$manifest_hash" ]]; then
        echo 'RID별 smoke 매니페스트 바이트가 서로 다릅니다.' >&2
        exit 1
    fi
done

for path in "${archives[@]:1}"; do
    if [[ $(hash_file "$path") != "$archive_hash" ]]; then
        echo 'RID별 smoke zstd 아카이브 바이트가 서로 다릅니다.' >&2
        exit 1
    fi
done

printf 'Manifest SHA-256: %s\n' "$manifest_hash"
printf 'Archive SHA-256: %s\n' "$archive_hash"

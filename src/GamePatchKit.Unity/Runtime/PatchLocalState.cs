#nullable enable
namespace GamePatchKit.Unity
{
    // 저장 폴더가 지금 무엇을 담고 있는지다. PatchClient.ReadLocalState가 네트워크 없이 만든다.
    // 소비자는 이 세 값으로 자기 상태를 조합한다. SDK는 상태 enum을 정의하지 않는다.
    // "루트 폴더가 아예 없음"과 "루트는 있으나 manifest.json이 없음"을 이 타입은 구분하지 않는다.
    // 둘 다 CompletedReleaseVersion이 null이며, 그 둘을 다르게 다룰지는 소비자 정책이다.
    public sealed class PatchLocalState
    {
        internal PatchLocalState(long? completedReleaseVersion, bool hasPendingGeneration, bool isManifestCorrupted)
        {
            CompletedReleaseVersion = completedReleaseVersion;
            HasPendingGeneration = hasPendingGeneration;
            IsManifestCorrupted = isManifestCorrupted;
        }

        // manifest.json이 가리키는 완료 세대. 파일이 없거나 읽을 수 없으면 null이다.
        public long? CompletedReleaseVersion { get; }

        // 루트 바로 아래에 <세대>.json 진행 표식이 남아 있다. 해석할 수 없는 표식도 센다.
        // 표식이 있으면 data 아래에 두 세대가 섞여 있을 수 있으므로 읽지 않는다.
        public bool HasPendingGeneration { get; }

        // manifest.json이 있으나 스키마 검증을 통과하지 못했다. 이때 CompletedReleaseVersion은 null이다.
        public bool IsManifestCorrupted { get; }
    }
}

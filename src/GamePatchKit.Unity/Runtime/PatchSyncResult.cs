#nullable enable
namespace GamePatchKit.Unity
{
    public sealed class PatchSyncResult
    {
        internal PatchSyncResult(
            long releaseVersion,
            long? previousReleaseVersion,
            bool isAlreadyUpToDate,
            int downloadedCount,
            long downloadedBytes,
            int reusedCount,
            int extractedCount,
            int removedCount)
        {
            ReleaseVersion = releaseVersion;
            PreviousReleaseVersion = previousReleaseVersion;
            IsAlreadyUpToDate = isAlreadyUpToDate;
            DownloadedCount = downloadedCount;
            DownloadedBytes = downloadedBytes;
            ReusedCount = reusedCount;
            ExtractedCount = extractedCount;
            RemovedCount = removedCount;
        }

        // 로컬이 가리키는 세대. 동기화했으면 요청한 releaseVersion과 같다.
        public long ReleaseVersion { get; }

        // 동기화 전에 로컬이 갖고 있던 세대. 처음 받는 폴더면 null이다.
        public long? PreviousReleaseVersion { get; }

        // 로컬이 이미 요청한 세대여서 원격을 한 번도 호출하지 않았으면 true다.
        public bool IsAlreadyUpToDate { get; }

        public int DownloadedCount { get; }

        public long DownloadedBytes { get; }

        // 로컬에 같은 이름·크기로 있어 다시 받지 않은 산출물 수
        public int ReusedCount { get; }

        // data 아래에 새로 푼 파일 수
        public int ExtractedCount { get; }

        // 새 세대에 없어 data에서 지운 파일 수
        public int RemovedCount { get; }
    }
}

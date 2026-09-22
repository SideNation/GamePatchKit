#nullable enable
namespace GamePatchKit.Unity
{
    // SyncAsync가 지나는 구간. 각 단계는 한 번씩만 지나가며 되돌아오지 않는다.
    public enum PatchPhase
    {
        // 세대 매니페스트를 받는 중이다. 받기 전에는 크기를 알 수 없어 이 단계의 수치는 모두 0이다.
        FetchingManifest,

        // 산출물을 받는 중이다. 해시 검증은 산출물마다의 내부 동작이라 단계로 나누지 않는다.
        Downloading,

        // 받은 산출물을 data 아래에 푸는 중이다.
        Extracting,
    }

    // 진행 상황의 스냅샷이다. 참조 타입이라 호출자가 필드 하나에 보관했다가 다른 스레드에서 읽어도
    // 찢어진 값을 보지 않는다. Extracting 단계의 보고는 백그라운드 스레드에서 올 수 있다.
    public sealed class PatchSyncProgress
    {
        internal PatchSyncProgress(
            PatchPhase phase,
            int completedCount,
            int totalCount,
            long completedBytes,
            long totalBytes)
        {
            Phase = phase;
            CompletedCount = completedCount;
            TotalCount = totalCount;
            CompletedBytes = completedBytes;
            TotalBytes = totalBytes;
        }

        public PatchPhase Phase { get; }

        // Downloading은 받기를 마친 산출물 수, Extracting은 푼 파일 수다.
        public int CompletedCount { get; }

        // 이 단계가 시작될 때 확정된다. 재사용할 산출물은 제외한 정확한 값이다.
        public int TotalCount { get; }

        // Downloading은 전송 바이트, Extracting은 푼 원본 바이트다. 둘은 압축 때문에 단위가 다르다.
        public long CompletedBytes { get; }

        public long TotalBytes { get; }

        // 현재 단계 안에서의 0~1이다. 단계를 가로지르는 통합 비율은 제공하지 않는다.
        public double Ratio => TotalBytes > 0 ? (double)CompletedBytes / TotalBytes : 0d;
    }
}

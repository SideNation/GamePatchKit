#nullable enable
namespace GamePatchKit.Unity
{
    // SyncAsync가 무엇을 받게 될지다. PatchClient.PlanAsync가 산출물을 받기 전에, 디스크를 바꾸지 않고 만든다.
    // 셀룰러 데이터를 쓰기 전에 사용자에게 알리는 용도다. 받을 것이 없으면 알릴 것도 없다.
    public sealed class PatchSyncPlan
    {
        internal PatchSyncPlan(int downloadCount, long downloadBytes)
        {
            DownloadCount = downloadCount;
            DownloadBytes = downloadBytes;
        }

        // 받아야 하는 산출물 수. 0이면 받을 것이 없다.
        public int DownloadCount { get; }

        // 받아야 하는 전송 바이트 합계. 이미 받아 둔 산출물은 빠진 정확한 값이다.
        public long DownloadBytes { get; }
    }
}

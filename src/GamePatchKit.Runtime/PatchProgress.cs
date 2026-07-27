namespace GamePatchKit.Runtime
{
    public enum PatchStage
    {
        Manifest,
        Planning,
        Downloading,
        Staging,
        Activating,
        Completed,
    }

    public sealed class PatchProgress
    {
        public PatchStage Stage { get; }

        public string? Group { get; }

        public string? RelativePath { get; }

        public int CompletedFiles { get; }

        public int TotalFiles { get; }

        public long CompletedBytes { get; }

        public long TotalBytes { get; }

        public int RetryCount { get; }

        public PatchProgress(
            PatchStage stage,
            string? group = null,
            string? relativePath = null,
            int completedFiles = 0,
            int totalFiles = 0,
            long completedBytes = 0,
            long totalBytes = 0,
            int retryCount = 0)
        {
            Stage = stage;
            Group = group;
            RelativePath = relativePath;
            CompletedFiles = completedFiles;
            TotalFiles = totalFiles;
            CompletedBytes = completedBytes;
            TotalBytes = totalBytes;
            RetryCount = retryCount;
        }
    }
}

namespace GamePatchKit.Cli;

internal sealed record GroupBuildSummary(
    string GroupId,
    int GroupVersion,
    int EntryCount,
    bool IsArchiveCreated,
    int FileObjectCount,
    long WrittenBytes);

internal sealed record FileRevisionAdjustment(
    string GroupId,
    string Path,
    string RequestedVersion,
    string ActualVersion);

internal sealed record BuildSummary(
    IReadOnlyList<GroupBuildSummary> Groups,
    IReadOnlyList<FileRevisionAdjustment> FileRevisionAdjustments);
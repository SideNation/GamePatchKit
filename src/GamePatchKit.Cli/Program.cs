namespace GamePatchKit.Cli;

public static class Program
{
    public static async Task<int> Main(string[] arguments)
    {
        return await RunAsync(arguments, Console.Out, Console.Error);
    }

    internal static async Task<int> RunAsync(string[] arguments, TextWriter output, TextWriter error)
    {
        try
        {
            if (arguments.Length == 0)
            {
                throw new BuildException("명령을 지정해야 합니다. build, verify 또는 upload를 사용하세요.");
            }

            string command = arguments[0];
            string[] commandArguments = arguments[1..];

            switch (command)
            {
                case "build":
                    BuildSummary summary = new BuildCommand().Execute(ArgumentsParser.ParseBuild(commandArguments));
                    WriteBuildSummary(output, summary);
                    break;
                case "verify":
                    IReadOnlyList<ArtifactMismatch> mismatches = new VerifyCommand().Execute(
                        ArgumentsParser.ParseVerify(commandArguments));

                    foreach (ArtifactMismatch mismatch in mismatches)
                    {
                        error.WriteLine($"{mismatch.Name}: {mismatch.Reason}");
                    }

                    if (mismatches.Count > 0)
                    {
                        return 1;
                    }

                    break;
                case "upload":
                    UploadArguments uploadArguments = ArgumentsParser.ParseUpload(commandArguments);
                    UploadSettings uploadSettings = UploadSettingsResolver.Resolve(uploadArguments.EnvFilePath);
                    var storage = new SupabaseUploadStorage(uploadSettings);
                    var uploadCommand = new UploadCommand(storage, uploadSettings.Bucket);
                    UploadSummary uploadSummary = await uploadCommand.ExecuteAsync(uploadArguments.OutputPath);
                    WriteUploadSummary(output, uploadSummary);
                    break;
                default:
                    throw new BuildException($"알 수 없는 명령입니다: {command}");
            }

            return 0;
        }
        catch (BuildException exception)
        {
            error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static void WriteBuildSummary(TextWriter output, BuildSummary summary)
    {
        long totalEntries = 0;
        long totalArchivesCreated = 0;
        long totalFileObjects = 0;
        long totalWrittenBytes = 0;

        foreach (GroupBuildSummary group in summary.Groups)
        {
            string archiveCreated = group.IsArchiveCreated ? "true" : "false";
            output.Write($"그룹 '{group.GroupId}': version={group.GroupVersion}, entries={group.EntryCount}, ");
            output.WriteLine(
                $"archiveCreated={archiveCreated}, fileObjects={group.FileObjectCount}, writtenBytes={group.WrittenBytes}");
            totalEntries += group.EntryCount;
            totalArchivesCreated += group.IsArchiveCreated ? 1 : 0;
            totalFileObjects += group.FileObjectCount;
            totalWrittenBytes = checked(totalWrittenBytes + group.WrittenBytes);
        }

        output.WriteLine(
            $"합계: groups={summary.Groups.Count}, entries={totalEntries}, archivesCreated={totalArchivesCreated}, "
            + $"fileObjects={totalFileObjects}, writtenBytes={totalWrittenBytes}");

        if (summary.FileRevisionAdjustments.Count == 0)
        {
            return;
        }

        output.WriteLine("경고: 파일 리비전이 자동 증가했습니다.");

        foreach (FileRevisionAdjustment adjustment in summary.FileRevisionAdjustments)
        {
            output.Write($"  group='{adjustment.GroupId}', path='{adjustment.Path}', ");
            output.WriteLine($"requested={adjustment.RequestedVersion}, actual={adjustment.ActualVersion}");
        }
    }

    private static void WriteUploadSummary(TextWriter output, UploadSummary summary)
    {
        output.WriteLine(
            $"업로드 산출물: uploaded={summary.UploadedCount}, uploadedBytes={summary.UploadedBytes}, "
            + $"skipped={summary.SkippedCount}");
        output.WriteLine($"세대 매니페스트: releaseVersion={summary.ReleaseVersion}");
    }
}
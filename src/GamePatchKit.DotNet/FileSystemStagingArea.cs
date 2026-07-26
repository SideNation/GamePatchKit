using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GamePatchKit.Runtime;

namespace GamePatchKit.DotNet;

// Files staged here are invisible to OpenInstallationFileAsync until PromoteAsync renames the whole directory
// into installs/ - a reader can never see a partially staged group. The operation id doubles as the eventual
// installation's local id, since staging and installs are different parents and reusing it avoids minting a
// second identifier for the same promotion.
internal sealed class FileSystemStagingArea : IRuntimeStagingArea
{
    private readonly string _runtimeRoot;
    private readonly string _packageId;
    private readonly string _stagingDirectory;
    private readonly string _operationId;
    private bool _promoted;

    public FileSystemStagingArea(string runtimeRoot, string packageId, string stagingDirectory, string operationId)
    {
        _runtimeRoot = runtimeRoot;
        _packageId = packageId;
        _stagingDirectory = stagingDirectory;
        _operationId = operationId;
    }

    public Task<Stream> CreateFileAsync(string relativePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = AdapterPath.Resolve(_stagingDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return Task.FromResult<Stream>(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None));
    }

    public Task<string> PromoteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string installsDirectory = AdapterLayout.InstallsDirectory(_runtimeRoot, _packageId);
        Directory.CreateDirectory(installsDirectory);
        string installationDirectory = Path.Combine(installsDirectory, _operationId);
        Directory.Move(_stagingDirectory, installationDirectory);
        _promoted = true;
        return Task.FromResult(AdapterLayout.InstallationKey(_packageId, _operationId));
    }

    public ValueTask DisposeAsync()
    {
        if (!_promoted && Directory.Exists(_stagingDirectory))
        {
            Directory.Delete(_stagingDirectory, recursive: true);
        }

        return ValueTask.CompletedTask;
    }
}

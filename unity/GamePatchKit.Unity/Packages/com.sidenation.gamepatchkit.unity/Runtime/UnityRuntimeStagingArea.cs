using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GamePatchKit.Runtime;

namespace GamePatchKit.Unity
{
    internal sealed class UnityRuntimeStagingArea : IRuntimeStagingArea
    {
        private readonly string _operationId;
        private readonly string _packageId;
        private readonly string _runtimeRoot;
        private readonly string _stagingDirectory;
        private bool _isPromoted;

        public UnityRuntimeStagingArea(
            string runtimeRoot,
            string packageId,
            string stagingDirectory,
            string operationId)
        {
            _runtimeRoot = runtimeRoot;
            _packageId = packageId;
            _stagingDirectory = stagingDirectory;
            _operationId = operationId;
        }

        public Task<Stream> CreateFileAsync(
            string relativePath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = UnityStoragePath.Resolve(_stagingDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            Stream stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            return Task.FromResult(stream);
        }

        public Task<string> PromoteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string installsDirectory = UnityStorageLayout.InstallsDirectory(_runtimeRoot, _packageId);
            Directory.CreateDirectory(installsDirectory);
            string installationDirectory = Path.Combine(installsDirectory, _operationId);
            Directory.Move(_stagingDirectory, installationDirectory);
            _isPromoted = true;
            return Task.FromResult(UnityStorageLayout.InstallationKey(_packageId, _operationId));
        }

        public ValueTask DisposeAsync()
        {
            if (!_isPromoted && Directory.Exists(_stagingDirectory))
            {
                Directory.Delete(_stagingDirectory, recursive: true);
            }

            return default;
        }
    }
}

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GamePatchKit.Runtime
{
    public interface IArtifactTransport
    {
        Task<Stream> OpenManifestAsync(
            TargetManifestReference target,
            CancellationToken cancellationToken);

        Task<Stream> OpenManifestSignatureAsync(
            TargetManifestReference target,
            CancellationToken cancellationToken);

        Task<Stream> OpenArtifactAsync(
            string packageId,
            string relativePath,
            CancellationToken cancellationToken);
    }
}

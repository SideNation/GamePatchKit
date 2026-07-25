using System.Globalization;

namespace GamePatchKit.Core.Manifests
{
    // Reconstructs the expected physical path for an artifact from its identity (kind/group/hash), per the
    // PRD's <packageId>/artifacts/{files/<hash>/..., bundles/<group>/<hash>...} layout. Used by
    // ManifestValidator to check recorded paths, not to touch the filesystem.
    public static class ContentAddressedPath
    {
        private const string PartFileNameFormat = "part-{0:D5}";

        public static string FileArtifactDirectory(string packageId, string artifactHash)
        {
            return $"{packageId}/artifacts/files/{artifactHash}";
        }

        public static string FileSinglePayloadPath(string packageId, string artifactHash, CompressionKind compression)
        {
            string fileName = compression == CompressionKind.Zstd ? "content.zst" : "content";
            return $"{FileArtifactDirectory(packageId, artifactHash)}/{fileName}";
        }

        public static string FilePartPath(string packageId, string artifactHash, long partIndex)
        {
            string fileName = string.Format(CultureInfo.InvariantCulture, PartFileNameFormat, partIndex);
            return $"{FileArtifactDirectory(packageId, artifactHash)}/{fileName}";
        }

        public static string BundleArtifactPath(string packageId, string group, string bundleHash, CompressionKind compression)
        {
            string extension = compression == CompressionKind.Zstd ? ".tar.zst" : ".tar";
            return $"{packageId}/artifacts/bundles/{group}/{bundleHash}{extension}";
        }
    }
}

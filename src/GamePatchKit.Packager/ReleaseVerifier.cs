using GamePatchKit.Compression.NativeCompressions;
using GamePatchKit.Core;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Manifests;
using GamePatchKit.Core.Signatures;

namespace GamePatchKit.Packager;

// The whole-release verification the CLI and any other host call instead of assembling the steps themselves:
// schema, then Core's semantic and reference integrity, then the actual payload bytes, then the signature
// document. The order is the point - a manifest that fails schema is never asked to reconstruct a file, and a
// signature is only looked at once the bytes it covers are known to be the published ones.
//
// Streaming throughout: artifact objects are hashed and decompressed through fixed buffers, so a release is
// verified without its largest file ever being held in memory.
public sealed class ReleaseVerifier
{
    private const string Stage = "release-verify";

    private readonly ICompressionCodec? _zstdCodec;

    public ReleaseVerifier()
        : this(ZstdCompressionCodecFactory.Create())
    {
    }

    public ReleaseVerifier(ICompressionCodec? zstdCodec)
    {
        _zstdCodec = zstdCodec;
    }

    public async Task<ReleaseVerifyReport> VerifyAsync(
        ReleaseVerifyRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        byte[] manifestBytes = request.GetManifestBytes();
        ReleaseManifest manifest = ReleaseManifestReader.Read(manifestBytes, request.PackageId, request.ManifestHash);

        await PackagePayloadVerifier.VerifyAsync(
            request.OutputRoot,
            manifest,
            _zstdCodec,
            cancellationToken).ConfigureAwait(false);

        (SignatureState state, string? keyId) = await VerifySignatureAsync(
            request,
            manifestBytes,
            cancellationToken).ConfigureAwait(false);

        return new ReleaseVerifyReport(
            manifest,
            request.ManifestHash,
            ReleaseTotals.Compute(manifest),
            state,
            keyId);
    }

    private async Task<(SignatureState State, string? KeyId)> VerifySignatureAsync(
        ReleaseVerifyRequest request,
        byte[] manifestBytes,
        CancellationToken cancellationToken)
    {
        string signaturePath = PackagePath.Resolve(
            request.OutputRoot,
            PackageLayout.SignaturePath(request.PackageId, request.ManifestHash));

        if (!File.Exists(signaturePath))
        {
            if (request.RequireSignature)
            {
                throw Failure("The release has no manifest.sig, but a signature was required.", request.PackageId);
            }

            return (SignatureState.Absent, null);
        }

        byte[] signatureBytes = await BoundedFile.ReadAllBytesAsync(
            signaturePath,
            PackageLayout.MaximumSignatureBytes,
            Stage,
            PackageErrorCodes.InvalidSignature,
            $"The manifest signature is larger than the {PackageLayout.MaximumSignatureBytes} byte limit.",
            request.PackageId,
            cancellationToken).ConfigureAwait(false);
        ManifestSignature signature = SignatureDocumentReader.Read(signatureBytes, request.PackageId, Stage);

        if (request.SignatureVerifier == null)
        {
            return (SignatureState.Present, signature.KeyId);
        }

        if (!request.SignatureVerifier.Verify(manifestBytes, signature))
        {
            throw Failure("The manifest signature is not valid for the published manifest bytes.", request.PackageId);
        }

        return (SignatureState.Verified, signature.KeyId);
    }

    private static PackageException Failure(string message, string packageId)
    {
        return new PackageException(
            new GamePatchKitError(Stage, PackageErrorCodes.InvalidSignature, message, packageId));
    }
}

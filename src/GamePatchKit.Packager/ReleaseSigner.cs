using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Json;
using GamePatchKit.Core.Signatures;

namespace GamePatchKit.Packager;

// Produces manifests/<manifestHash>/manifest.sig for an already-published release.
//
// The manifest is re-read and re-checked before anything is signed: a signature over bytes that are not the
// canonical manifest the hash names would be a valid signature over the wrong thing. Payload bytes are not
// verified here - that is what verify is for, and signing does not need to reconstruct any file to make a
// correct statement about the manifest.
public static class ReleaseSigner
{
    private const string Stage = "manifest-sign";

    public static async Task<SignReleaseResult> SignAsync(
        SignReleaseRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        byte[] manifestBytes = await ReleaseManifestReader.ReadPublishedBytesAsync(
            request.OutputRoot,
            request.PackageId,
            request.ManifestHash,
            cancellationToken).ConfigureAwait(false);

        ReleaseManifestReader.Read(manifestBytes, request.PackageId, request.ManifestHash);

        var signature = new ManifestSignature(
            ManifestSignature.SupportedSchemaVersion,
            ManifestSignature.SupportedAlgorithm,
            request.Signer.KeyId,
            Base64Url.Encode(request.Signer.Sign(manifestBytes)));

        // The signer is injected, so its keyId is unverified input like any other: round-tripping the model
        // through Core's parser is what enforces the ed25519-<hex64> shape and the 64-byte signature rule
        // before those values reach an immutable path.
        if (!ManifestSignature.TryParse(signature.ToJson(), out _, out IReadOnlyList<GamePatchKitError> errors))
        {
            throw new PackageException(errors);
        }

        byte[] canonicalBytes = CanonicalJsonWriter.Write(signature.ToJson());
        string signaturePath = PackagePath.Resolve(
            request.OutputRoot,
            PackageLayout.SignaturePath(request.PackageId, request.ManifestHash));

        if (File.Exists(signaturePath))
        {
            await EnsureExistingSignatureMatchesAsync(
                signaturePath,
                canonicalBytes,
                request.PackageId,
                cancellationToken).ConfigureAwait(false);

            return new SignReleaseResult(signature, canonicalBytes, request.ManifestHash, created: false);
        }

        if (!request.DryRun)
        {
            await WriteAsync(signaturePath, canonicalBytes, request.PackageId, cancellationToken).ConfigureAwait(false);
        }

        return new SignReleaseResult(signature, canonicalBytes, request.ManifestHash, created: true);
    }

    private static async Task EnsureExistingSignatureMatchesAsync(
        string signaturePath,
        byte[] canonicalBytes,
        string packageId,
        CancellationToken cancellationToken)
    {
        byte[] existingBytes = await BoundedFile.ReadAllBytesAsync(
            signaturePath,
            PackageLayout.MaximumSignatureBytes,
            Stage,
            PackageErrorCodes.InvalidSignature,
            $"The published manifest signature is larger than the {PackageLayout.MaximumSignatureBytes} byte limit.",
            packageId,
            cancellationToken).ConfigureAwait(false);

        // Read before comparing: reuse is only safe if what is already published is itself a valid signature
        // document, and a byte comparison alone would happily "reuse" a corrupted file.
        SignatureDocumentReader.Read(existingBytes, packageId, Stage);

        if (!existingBytes.AsSpan().SequenceEqual(canonicalBytes))
        {
            throw new PackageException(new GamePatchKitError(
                Stage,
                PackageErrorCodes.ImmutablePathConflict,
                "A different manifest.sig is already published for this manifestHash; signatures are immutable.",
                packageId));
        }
    }

    private static async Task WriteAsync(
        string signaturePath,
        byte[] canonicalBytes,
        string packageId,
        CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(signaturePath)!;
        string temporaryPath = Path.Combine(directory, $".manifest-sig-{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllBytesAsync(temporaryPath, canonicalBytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, signaturePath, overwrite: false);
        }
        catch (IOException) when (File.Exists(signaturePath))
        {
            // Another writer published first. Whether that is a reuse or a conflict is the same question as
            // for a signature that was already there when this run started.
            await EnsureExistingSignatureMatchesAsync(signaturePath, canonicalBytes, packageId, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}

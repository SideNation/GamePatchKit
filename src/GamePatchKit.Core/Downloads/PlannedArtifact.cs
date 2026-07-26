using System;
using System.Collections.Generic;
using GamePatchKit.Core.Manifests;

namespace GamePatchKit.Core.Downloads
{
    // An artifact the plan has to read to produce at least one missing file.
    //
    // ObjectsToDownload lists only what the content-addressed cache does not already hold, so an artifact
    // whose objects are all cached still appears here with an empty list: the download is done, the file it
    // feeds is not.
    public sealed class PlannedArtifact
    {
        public ManifestArtifact Artifact { get; }

        public IReadOnlyList<ArtifactPayloadObject> ObjectsToDownload { get; }

        // True for every multipart file artifact, including one where only some parts are being fetched:
        // per-part hashes prove each part arrived intact but say nothing about the joined payload, so the
        // combined result is hashed against artifactHash before the file is accepted.
        public bool RequiresCombinedHashVerification { get; }

        public PlannedArtifact(ManifestArtifact artifact, IReadOnlyList<ArtifactPayloadObject> objectsToDownload, bool requiresCombinedHashVerification)
        {
            Artifact = artifact ?? throw new ArgumentNullException(nameof(artifact));
            ObjectsToDownload = objectsToDownload ?? throw new ArgumentNullException(nameof(objectsToDownload));
            RequiresCombinedHashVerification = requiresCombinedHashVerification;
        }
    }
}

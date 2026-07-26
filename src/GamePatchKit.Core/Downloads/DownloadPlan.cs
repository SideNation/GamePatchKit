using System;
using System.Collections.Generic;
using GamePatchKit.Core.Manifests;

namespace GamePatchKit.Core.Downloads
{
    // What a host has to fetch and materialize to bring one activation batch to the target release. Both lists
    // keep the target manifest's canonical order, so the same inputs always produce the same plan.
    public sealed class DownloadPlan
    {
        public IReadOnlyList<PlannedArtifact> Artifacts { get; }

        // Target files in the selected groups whose local path/fileHash pair does not already match. These are
        // what staging has to write; everything else in the selected groups is reused where it is.
        public IReadOnlyList<ManifestFileEntry> MissingFiles { get; }

        public long EstimatedDownloadBytes { get; }

        // Peak extra space the operation needs before promotion: the bytes being added to the cache plus the
        // staged copies of the missing files. Already-cached objects are not counted - they occupy space the
        // host has already paid for.
        public long EstimatedTemporaryBytes { get; }

        // File-artifact objects to download, counting each part of a multipart artifact separately.
        public int FileObjectCount { get; }

        public int BundleCount { get; }

        public DownloadPlan(IReadOnlyList<PlannedArtifact> artifacts, IReadOnlyList<ManifestFileEntry> missingFiles)
        {
            Artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
            MissingFiles = missingFiles ?? throw new ArgumentNullException(nameof(missingFiles));

            long downloadBytes = 0;
            int fileObjectCount = 0;
            int bundleCount = 0;

            foreach (PlannedArtifact planned in artifacts)
            {
                bool isBundle = planned.Artifact is ManifestArtifact.BundleArtifact;

                foreach (ArtifactPayloadObject payload in planned.ObjectsToDownload)
                {
                    downloadBytes = AddSaturating(downloadBytes, payload.Size);

                    if (isBundle)
                    {
                        bundleCount++;
                    }
                    else
                    {
                        fileObjectCount++;
                    }
                }
            }

            long stagingBytes = 0;

            foreach (ManifestFileEntry file in missingFiles)
            {
                stagingBytes = AddSaturating(stagingBytes, file.Size);
            }

            EstimatedDownloadBytes = downloadBytes;
            EstimatedTemporaryBytes = AddSaturating(downloadBytes, stagingBytes);
            FileObjectCount = fileObjectCount;
            BundleCount = bundleCount;
        }

        // Every size is a non-negative I-JSON safe integer, so a manifest declaring enough of them can still
        // exceed long.MaxValue in total. These are advisory estimates: saturating keeps an absurd manifest from
        // reporting a small number through overflow.
        private static long AddSaturating(long current, long addend)
        {
            return current > long.MaxValue - addend ? long.MaxValue : current + addend;
        }
    }
}

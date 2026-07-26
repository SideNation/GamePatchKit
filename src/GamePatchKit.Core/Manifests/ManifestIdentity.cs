using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Core.Manifests
{
    // The dataVersion hash input: packageId, group name/required (logical policy), and each file's
    // normalized path/group/original size/fileHash. Deliberately excludes artifact kind, bundle
    // boundaries, compression, artifact paths/parts, compactVersion, and manifestHash - compacting or
    // re-encoding a release must not change its dataVersion. This is an internal computation contract
    // (Core.Manifests), not one of the four published JSON schemas: dataVersion's actual digest is
    // computed by GamePatchKit.Core's identity API (docs/plan/03), not by this projection.
    public sealed class ManifestIdentity
    {
        public string PackageId { get; }

        public IReadOnlyList<ManifestGroupEntry> Groups { get; }

        public IReadOnlyList<FileIdentity> Files { get; }

        public ManifestIdentity(string packageId, IReadOnlyList<ManifestGroupEntry> groups, IReadOnlyList<FileIdentity> files)
        {
            PackageId = packageId ?? throw new ArgumentNullException(nameof(packageId));
            Groups = ReadOnlySnapshot.Of(groups, nameof(groups));
            Files = ReadOnlySnapshot.Of(files, nameof(files));
        }

        public static ManifestIdentity FromManifest(ReleaseManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            List<FileIdentity> files = manifest.Files
                .Select(file => new FileIdentity(file.Path, file.Group, file.Size, file.FileHash))
                .ToList();

            return new ManifestIdentity(manifest.PackageId, manifest.Groups, files);
        }

        public JObject ToCanonicalValue()
        {
            return new JObject
            {
                ["packageId"] = PackageId,
                ["groups"] = new JArray(Groups.Select(g => g.ToJson())),
                ["files"] = new JArray(Files.Select(f => f.ToJson())),
            };
        }

        public sealed class FileIdentity
        {
            public string Path { get; }

            public string Group { get; }

            public long Size { get; }

            public string FileHash { get; }

            public FileIdentity(string path, string group, long size, string fileHash)
            {
                Path = path ?? throw new ArgumentNullException(nameof(path));
                Group = group ?? throw new ArgumentNullException(nameof(group));
                Size = size;
                FileHash = fileHash ?? throw new ArgumentNullException(nameof(fileHash));
            }

            public JObject ToJson()
            {
                return new JObject { ["path"] = Path, ["group"] = Group, ["size"] = Size, ["fileHash"] = FileHash };
            }
        }
    }
}

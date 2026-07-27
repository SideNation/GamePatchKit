using System;
using System.Collections.Generic;

namespace GamePatchKit.Runtime
{
    public sealed class PackageState
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; }

        public long StateRevision { get; }

        public string PackageId { get; }

        public PackageActiveState Active { get; }

        public IReadOnlyList<PackageGroupState> Groups { get; }

        public PackageState(
            int schemaVersion,
            long stateRevision,
            string packageId,
            PackageActiveState active,
            IReadOnlyList<PackageGroupState> groups)
        {
            SchemaVersion = schemaVersion;
            StateRevision = stateRevision;
            PackageId = packageId ?? throw new ArgumentNullException(nameof(packageId));
            Active = active ?? throw new ArgumentNullException(nameof(active));

            if (groups == null)
            {
                throw new ArgumentNullException(nameof(groups));
            }

            Groups = new List<PackageGroupState>(groups).AsReadOnly();
        }
    }
}

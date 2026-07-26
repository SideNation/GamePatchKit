using System;
using System.Collections.Generic;
using GamePatchKit.Core.Errors;

namespace GamePatchKit.Core.Configuration
{
    // Semantic (layer-2) rules that cross multiple fields/elements and cannot be expressed by
    // package-config.schema.json or a single group's own TryParse: group name uniqueness.
    public static class PackageConfigValidator
    {
        private const string Stage = "package-config";

        public static ValidationResult Validate(PackageConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            var errors = new List<GamePatchKitError>();
            var seenNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (PackageConfigGroup group in config.Groups)
            {
                if (!seenNames.Add(group.Name))
                {
                    errors.Add(new GamePatchKitError(
                        Stage,
                        PackageConfigErrorCodes.DuplicateGroupName,
                        $"Group name '{group.Name}' is declared more than once.",
                        packageId: config.PackageId,
                        group: group.Name));
                }
            }

            return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(errors);
        }
    }
}

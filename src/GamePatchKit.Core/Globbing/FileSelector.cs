using System;
using System.Collections.Generic;
using System.Linq;
using GamePatchKit.Core.Errors;
using GamePatchKit.Core.Paths;

namespace GamePatchKit.Core.Globbing
{
    public static class FileSelector
    {
        public sealed class GroupDefinition
        {
            public string Name { get; }

            public IReadOnlyList<GlobPattern> Include { get; }

            public GroupDefinition(string name, IReadOnlyList<GlobPattern> include)
            {
                if (string.IsNullOrEmpty(name))
                {
                    throw new ArgumentException("Name must not be empty.", nameof(name));
                }

                Name = name;
                Include = include ?? throw new ArgumentNullException(nameof(include));
            }
        }

        public sealed class SelectedFile
        {
            public string Path { get; }

            public string Group { get; }

            public SelectedFile(string path, string group)
            {
                Path = path;
                Group = group;
            }
        }

        public sealed class SelectionResult
        {
            public IReadOnlyList<SelectedFile> SelectedFiles { get; }

            public IReadOnlyList<GamePatchKitError> Errors { get; }

            public bool IsValid => Errors.Count == 0;

            public SelectionResult(IReadOnlyList<SelectedFile> selectedFiles, IReadOnlyList<GamePatchKitError> errors)
            {
                SelectedFiles = selectedFiles;
                Errors = errors;
            }
        }

        private const string DefaultGroupName = "default";
        private const string SelectionStage = "file-selection";

        // include[] OR -> exclude[] OR (always wins, no re-include) -> group include[] OR (>1 match errors,
        // 0 matches falls back to 'default') -> final ordinal UTF-8 byte sort. Filesystem-free: candidatePaths
        // is caller-supplied strings, not an enumeration Core performs itself.
        public static SelectionResult Select(
            IReadOnlyList<string> candidatePaths,
            IReadOnlyList<GlobPattern> include,
            IReadOnlyList<GlobPattern> exclude,
            IReadOnlyList<GroupDefinition> groups)
        {
            if (candidatePaths == null)
            {
                throw new ArgumentNullException(nameof(candidatePaths));
            }

            if (include == null)
            {
                throw new ArgumentNullException(nameof(include));
            }

            if (exclude == null)
            {
                throw new ArgumentNullException(nameof(exclude));
            }

            if (groups == null)
            {
                throw new ArgumentNullException(nameof(groups));
            }

            foreach (GroupDefinition group in groups)
            {
                if (group.Name == DefaultGroupName)
                {
                    throw new ArgumentException("The reserved group name 'default' must not appear in explicit group definitions.", nameof(groups));
                }
            }

            var errors = new List<GamePatchKitError>();
            var normalizedPaths = new List<string>(candidatePaths.Count);

            foreach (string candidate in candidatePaths)
            {
                if (RelativePathNormalizer.TryNormalize(candidate, out string normalized, out string pathErrorCode))
                {
                    normalizedPaths.Add(normalized);
                }
                else
                {
                    errors.Add(new GamePatchKitError(SelectionStage, pathErrorCode, "Candidate path failed normalization.", relativePath: candidate));
                }
            }

            foreach (IReadOnlyList<string> duplicateGroup in RelativePathNormalizer.FindCaseInsensitiveDuplicateGroups(normalizedPaths))
            {
                string joined = string.Join(", ", duplicateGroup);
                errors.Add(new GamePatchKitError(
                    SelectionStage,
                    PathErrorCodes.CaseInsensitiveDuplicate,
                    $"Paths collide under OS-independent case-insensitive comparison: {joined}"));
            }

            var selected = new List<SelectedFile>();

            foreach (string path in normalizedPaths)
            {
                if (!MatchesAny(include, path) || MatchesAny(exclude, path))
                {
                    continue;
                }

                List<string> matchingGroups = groups.Where(group => MatchesAny(group.Include, path)).Select(group => group.Name).ToList();

                if (matchingGroups.Count > 1)
                {
                    string joined = string.Join(", ", matchingGroups);
                    errors.Add(new GamePatchKitError(
                        SelectionStage,
                        GlobErrorCodes.AmbiguousGroupMatch,
                        $"Path matches more than one group: {joined}.",
                        relativePath: path));
                    continue;
                }

                string assignedGroup = matchingGroups.Count == 1 ? matchingGroups[0] : DefaultGroupName;
                selected.Add(new SelectedFile(path, assignedGroup));
            }

            selected.Sort((a, b) => Utf8OrdinalStringComparer.Instance.Compare(a.Path, b.Path));

            return new SelectionResult(selected, errors);
        }

        private static bool MatchesAny(IReadOnlyList<GlobPattern> patterns, string path)
        {
            foreach (GlobPattern pattern in patterns)
            {
                if (pattern.IsMatch(path))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

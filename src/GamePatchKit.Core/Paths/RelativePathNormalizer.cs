using System;
using System.Collections.Generic;
using System.Text;

namespace GamePatchKit.Core.Paths
{
    public static class RelativePathNormalizer
    {
        public static bool TryNormalize(string candidatePath, out string normalizedPath, out string errorCode)
        {
            if (candidatePath == null)
            {
                throw new ArgumentNullException(nameof(candidatePath));
            }

            if (candidatePath.Length == 0)
            {
                normalizedPath = string.Empty;
                errorCode = PathErrorCodes.Empty;
                return false;
            }

            if (PathGrammar.ContainsNul(candidatePath))
            {
                normalizedPath = string.Empty;
                errorCode = PathErrorCodes.ContainsNul;
                return false;
            }

            if (PathGrammar.ContainsBackslash(candidatePath))
            {
                normalizedPath = string.Empty;
                errorCode = PathErrorCodes.ContainsBackslash;
                return false;
            }

            if (PathGrammar.IsAbsolute(candidatePath))
            {
                normalizedPath = string.Empty;
                errorCode = PathErrorCodes.Absolute;
                return false;
            }

            string nfc = candidatePath.Normalize(NormalizationForm.FormC);
            string[] segments = nfc.Split('/');

            foreach (string segment in segments)
            {
                if (segment.Length == 0)
                {
                    normalizedPath = string.Empty;
                    errorCode = PathErrorCodes.EmptySegment;
                    return false;
                }

                if (segment == ".")
                {
                    normalizedPath = string.Empty;
                    errorCode = PathErrorCodes.DotSegment;
                    return false;
                }

                if (segment == "..")
                {
                    normalizedPath = string.Empty;
                    errorCode = PathErrorCodes.DotDotSegment;
                    return false;
                }
            }

            normalizedPath = nfc;
            errorCode = string.Empty;
            return true;
        }

        public static bool IsHiddenSegment(string segment)
        {
            if (segment == null)
            {
                throw new ArgumentNullException(nameof(segment));
            }

            return segment.Length > 0 && segment[0] == '.';
        }

        // Groups paths that collide under StringComparer.OrdinalIgnoreCase, whether byte-identical or only
        // case-different, since either would collide on a case-insensitive filesystem regardless of host OS.
        public static IReadOnlyList<IReadOnlyList<string>> FindCaseInsensitiveDuplicateGroups(IEnumerable<string> normalizedPaths)
        {
            if (normalizedPaths == null)
            {
                throw new ArgumentNullException(nameof(normalizedPaths));
            }

            var buckets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (string path in normalizedPaths)
            {
                if (!buckets.TryGetValue(path, out List<string>? bucket))
                {
                    bucket = new List<string>();
                    buckets[path] = bucket;
                }

                bucket.Add(path);
            }

            var duplicates = new List<IReadOnlyList<string>>();

            foreach (List<string> bucket in buckets.Values)
            {
                if (bucket.Count > 1)
                {
                    duplicates.Add(bucket);
                }
            }

            return duplicates;
        }
    }
}

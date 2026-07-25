using System;
using System.Collections.Generic;
using System.Text;
using GamePatchKit.Core.Paths;

namespace GamePatchKit.Core.Globbing
{
    public sealed class GlobPattern
    {
        private interface ISegment
        {
        }

        private sealed class RecursiveSegment : ISegment
        {
            public static readonly RecursiveSegment Instance = new RecursiveSegment();

            private RecursiveSegment()
            {
            }
        }

        private sealed class LiteralSegment : ISegment
        {
            private readonly string _text;

            public LiteralSegment(string text)
            {
                _text = text;
            }

            public bool Matches(string pathSegment)
            {
                bool pathIsHidden = RelativePathNormalizer.IsHiddenSegment(pathSegment);
                bool patternStartsWithLiteralDot = _text.Length > 0 && _text[0] == '.';

                if (pathIsHidden && !patternStartsWithLiteralDot)
                {
                    return false;
                }

                return MatchesWildcard(_text, pathSegment);
            }

            private static bool MatchesWildcard(string pattern, string text)
            {
                int patternIndex = 0;
                int textIndex = 0;
                int starIndex = -1;
                int matchIndex = 0;

                while (textIndex < text.Length)
                {
                    if (patternIndex < pattern.Length && pattern[patternIndex] == text[textIndex])
                    {
                        patternIndex++;
                        textIndex++;
                    }
                    else if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
                    {
                        starIndex = patternIndex;
                        matchIndex = textIndex;
                        patternIndex++;
                    }
                    else if (starIndex != -1)
                    {
                        patternIndex = starIndex + 1;
                        matchIndex++;
                        textIndex = matchIndex;
                    }
                    else
                    {
                        return false;
                    }
                }

                while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
                {
                    patternIndex++;
                }

                return patternIndex == pattern.Length;
            }
        }

        // '?', '[', ']', '{', '}' cover the named character-class/brace-expansion/'?' rejections. A leading
        // '!' covers negation. Parens/'@'/'+' are left as plain literal characters (common in real filenames,
        // e.g. "image (1).png"): extglob is not implemented, so they can never carry extglob semantics.
        private static readonly char[] _disallowedLiteralCharacters = { '?', '[', ']', '{', '}' };

        private readonly IReadOnlyList<ISegment> _segments;

        public string SourceText { get; }

        private GlobPattern(string sourceText, IReadOnlyList<ISegment> segments)
        {
            SourceText = sourceText;
            _segments = segments;
        }

        public static bool TryParse(string pattern, out GlobPattern? result, out string errorCode)
        {
            if (pattern == null)
            {
                throw new ArgumentNullException(nameof(pattern));
            }

            if (pattern.Length == 0)
            {
                result = null;
                errorCode = GlobErrorCodes.Empty;
                return false;
            }

            if (PathGrammar.ContainsNul(pattern))
            {
                result = null;
                errorCode = GlobErrorCodes.ContainsNul;
                return false;
            }

            if (PathGrammar.ContainsBackslash(pattern))
            {
                result = null;
                errorCode = GlobErrorCodes.ContainsBackslash;
                return false;
            }

            if (PathGrammar.IsAbsolute(pattern))
            {
                result = null;
                errorCode = GlobErrorCodes.Absolute;
                return false;
            }

            // Normalize to NFC so a pattern and a RelativePathNormalizer-normalized candidate path always
            // agree on which of two canonically-equivalent Unicode encodings ("cafe" + combining accent vs
            // the precomposed character) to compare against; matching never NFC-normalizes candidate paths.
            string normalizedPattern = pattern.Normalize(NormalizationForm.FormC);
            string[] rawSegments = normalizedPattern.Split('/');
            var segments = new List<ISegment>(rawSegments.Length);

            foreach (string rawSegment in rawSegments)
            {
                if (!TryParseSegment(rawSegment, out ISegment? segment, out errorCode))
                {
                    result = null;
                    return false;
                }

                segments.Add(segment!);
            }

            result = new GlobPattern(normalizedPattern, segments);
            errorCode = string.Empty;
            return true;
        }

        public bool IsMatch(string normalizedRelativePath)
        {
            if (normalizedRelativePath == null)
            {
                throw new ArgumentNullException(nameof(normalizedRelativePath));
            }

            string[] pathSegments = normalizedRelativePath.Split('/');
            return Matches(_segments, pathSegments);
        }

        private static bool TryParseSegment(string rawSegment, out ISegment? segment, out string errorCode)
        {
            if (rawSegment == "**")
            {
                segment = RecursiveSegment.Instance;
                errorCode = string.Empty;
                return true;
            }

            if (rawSegment.Length == 0)
            {
                segment = null;
                errorCode = GlobErrorCodes.EmptySegment;
                return false;
            }

            if (rawSegment == "." || rawSegment == "..")
            {
                segment = null;
                errorCode = GlobErrorCodes.DotSegment;
                return false;
            }

            if (rawSegment.IndexOf("**", StringComparison.Ordinal) >= 0)
            {
                segment = null;
                errorCode = GlobErrorCodes.PartialRecursive;
                return false;
            }

            if (rawSegment[0] == '!')
            {
                segment = null;
                errorCode = GlobErrorCodes.Negation;
                return false;
            }

            if (rawSegment.IndexOfAny(_disallowedLiteralCharacters) >= 0)
            {
                segment = null;
                errorCode = GlobErrorCodes.DisallowedCharacter;
                return false;
            }

            segment = new LiteralSegment(rawSegment);
            errorCode = string.Empty;
            return true;
        }

        // ** may match zero or more whole path segments but must never implicitly consume a hidden one.
        private static bool Matches(IReadOnlyList<ISegment> patternSegments, string[] pathSegments)
        {
            int patternCount = patternSegments.Count;
            int pathCount = pathSegments.Length;
            var dp = new bool[patternCount + 1, pathCount + 1];
            dp[0, 0] = true;

            for (int i = 1; i <= patternCount; i++)
            {
                dp[i, 0] = dp[i - 1, 0] && patternSegments[i - 1] is RecursiveSegment;
            }

            for (int i = 1; i <= patternCount; i++)
            {
                ISegment segment = patternSegments[i - 1];

                for (int j = 1; j <= pathCount; j++)
                {
                    if (segment is RecursiveSegment)
                    {
                        bool matchesZeroMore = dp[i - 1, j];
                        bool matchesOneMore = dp[i, j - 1] && !RelativePathNormalizer.IsHiddenSegment(pathSegments[j - 1]);
                        dp[i, j] = matchesZeroMore || matchesOneMore;
                    }
                    else
                    {
                        var literal = (LiteralSegment)segment;
                        dp[i, j] = dp[i - 1, j - 1] && literal.Matches(pathSegments[j - 1]);
                    }
                }
            }

            return dp[patternCount, pathCount];
        }
    }
}

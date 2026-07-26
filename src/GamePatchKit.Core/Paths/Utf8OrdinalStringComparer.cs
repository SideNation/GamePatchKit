using System;
using System.Collections.Generic;
using System.Text;

namespace GamePatchKit.Core.Paths
{
    // Ordinal UTF-8 byte order, per the canonical array-ordering rules. This is not the same as UTF-16
    // code-unit order (StringComparer.Ordinal), which RFC 8785 JCS uses for object-key sorting and which
    // disagrees with byte order for supplementary-plane characters (surrogate pairs sort low in UTF-16
    // but their code points are high).
    public sealed class Utf8OrdinalStringComparer : IComparer<string>
    {
        public static readonly Utf8OrdinalStringComparer Instance = new Utf8OrdinalStringComparer();

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x == null)
            {
                return -1;
            }

            if (y == null)
            {
                return 1;
            }

            byte[] xBytes = Encoding.UTF8.GetBytes(x);
            byte[] yBytes = Encoding.UTF8.GetBytes(y);
            int length = Math.Min(xBytes.Length, yBytes.Length);

            for (int i = 0; i < length; i++)
            {
                int diff = xBytes[i] - yBytes[i];
                if (diff != 0)
                {
                    return diff;
                }
            }

            return xBytes.Length - yBytes.Length;
        }
    }
}

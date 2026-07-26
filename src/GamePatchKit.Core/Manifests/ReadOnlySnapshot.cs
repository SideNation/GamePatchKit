using System;
using System.Collections.Generic;

namespace GamePatchKit.Core.Manifests
{
    // Defensive copy for the collections manifest models expose.
    //
    // A manifest is what canonical bytes, dataVersion and manifestHash are computed from, so it has to keep
    // describing the release it described when those were taken. Copying only the outer lists is not enough:
    // a file artifact's parts and a bundle's entries are lists of their own, and leaving those aliased lets a
    // caller change the model out from under bytes that have already been hashed and signed.
    internal static class ReadOnlySnapshot
    {
        public static IReadOnlyList<T> Of<T>(IReadOnlyList<T> source, string parameterName)
        {
            if (source == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            return new List<T>(source).AsReadOnly();
        }
    }
}

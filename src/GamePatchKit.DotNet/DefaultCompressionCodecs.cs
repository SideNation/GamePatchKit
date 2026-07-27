using System.Collections.Generic;
using GamePatchKit.Compression.NativeCompressions;
using GamePatchKit.Core;

namespace GamePatchKit.DotNet;

// Convenience wiring for the PackageRuntime constructor's compressionCodecs parameter, so a host does not have
// to import GamePatchKit.Compression.NativeCompressions itself just to ask for the one codec every package
// currently uses.
public static class DefaultCompressionCodecs
{
    public static IReadOnlyList<ICompressionCodec> Create()
    {
        return new[] { ZstdCompressionCodecFactory.Create() };
    }
}

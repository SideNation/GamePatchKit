using System;

namespace GamePatchKit.Runtime
{
    public sealed class ArtifactTransportException : Exception
    {
        public bool IsTransient { get; }

        // Confirmed absence (e.g. a real HTTP 404), as opposed to any other non-transient failure (401, 403,
        // an unrecognized response). An adapter must only set this when it positively knows the resource does
        // not exist - defaulting to false is the safe choice, since callers that treat absence as tolerable
        // (an unsigned release, say) must not silently accept "something else went wrong" as evidence of that.
        public bool IsNotFound { get; }

        public ArtifactTransportException(string message, bool isTransient, bool isNotFound = false)
            : base(message)
        {
            IsTransient = isTransient;
            IsNotFound = isNotFound;
        }

        public ArtifactTransportException(string message, bool isTransient, Exception innerException)
            : base(message, innerException)
        {
            IsTransient = isTransient;
        }
    }
}

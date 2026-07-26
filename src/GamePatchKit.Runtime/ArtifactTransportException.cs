using System;

namespace GamePatchKit.Runtime
{
    public sealed class ArtifactTransportException : Exception
    {
        public bool IsTransient { get; }

        public ArtifactTransportException(string message, bool isTransient)
            : base(message)
        {
            IsTransient = isTransient;
        }

        public ArtifactTransportException(string message, bool isTransient, Exception innerException)
            : base(message, innerException)
        {
            IsTransient = isTransient;
        }
    }
}

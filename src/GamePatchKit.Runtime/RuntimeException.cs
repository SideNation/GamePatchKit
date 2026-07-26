using System;
using GamePatchKit.Core.Errors;

namespace GamePatchKit.Runtime
{
    public sealed class RuntimeException : Exception
    {
        public GamePatchKitError Error { get; }

        public RuntimeException(GamePatchKitError error)
            : base(error?.ToString())
        {
            Error = error ?? throw new ArgumentNullException(nameof(error));
        }

        public RuntimeException(GamePatchKitError error, Exception innerException)
            : base(error?.ToString(), innerException)
        {
            Error = error ?? throw new ArgumentNullException(nameof(error));
        }
    }
}

using System;

namespace GamePatchKit.Core.Errors
{
    public sealed class GamePatchKitError
    {
        public string Stage { get; }

        public string Code { get; }

        public string Message { get; }

        public string? PackageId { get; }

        public string? RelativePath { get; }

        public string? Group { get; }

        public GamePatchKitError(
            string stage,
            string code,
            string message,
            string? packageId = null,
            string? relativePath = null,
            string? group = null)
        {
            if (string.IsNullOrEmpty(stage))
            {
                throw new ArgumentException("Stage must not be empty.", nameof(stage));
            }

            if (string.IsNullOrEmpty(code))
            {
                throw new ArgumentException("Code must not be empty.", nameof(code));
            }

            if (string.IsNullOrEmpty(message))
            {
                throw new ArgumentException("Message must not be empty.", nameof(message));
            }

            Stage = stage;
            Code = code;
            Message = message;
            PackageId = packageId;
            RelativePath = relativePath;
            Group = group;
        }

        public override string ToString()
        {
            return $"[{Stage}/{Code}] {Message}";
        }
    }
}

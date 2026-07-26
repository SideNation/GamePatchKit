using System;
using System.Collections.Generic;

namespace GamePatchKit.Core.Errors
{
    public sealed class ValidationResult
    {
        private static readonly IReadOnlyList<GamePatchKitError> _noErrors = Array.Empty<GamePatchKitError>();

        public IReadOnlyList<GamePatchKitError> Errors { get; }

        public bool IsValid => Errors.Count == 0;

        private ValidationResult(IReadOnlyList<GamePatchKitError> errors)
        {
            Errors = errors;
        }

        public static ValidationResult Success()
        {
            return new ValidationResult(_noErrors);
        }

        public static ValidationResult Failure(IReadOnlyList<GamePatchKitError> errors)
        {
            if (errors == null)
            {
                throw new ArgumentNullException(nameof(errors));
            }

            if (errors.Count == 0)
            {
                throw new ArgumentException("Failure requires at least one error.", nameof(errors));
            }

            return new ValidationResult(errors);
        }
    }
}

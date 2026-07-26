using System;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Core.Json
{
    // I-JSON safe-integer range shared by every model parser: only integer-typed tokens (no '.'/exponent in
    // the source) within +/-(2^53-1) are accepted; per-field sign/range limits are layered on top by callers.
    public static class JsonNumbers
    {
        public const long MaxSafeInteger = 9007199254740991L;

        public const long MinSafeInteger = -9007199254740991L;

        public static bool TryGetSafeInteger(JToken token, out long value)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            if (token.Type != JTokenType.Integer)
            {
                value = 0;
                return false;
            }

            try
            {
                value = (long)token;
            }
            catch (OverflowException)
            {
                value = 0;
                return false;
            }

            return value >= MinSafeInteger && value <= MaxSafeInteger;
        }
    }
}

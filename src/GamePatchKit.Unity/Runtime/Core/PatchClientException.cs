#nullable enable
using System;

namespace GamePatchKit.Unity
{
    // 동기화가 중단된 이유를 담는다. 취소는 OperationCanceledException으로 따로 전달된다.
    public sealed class PatchClientException : Exception
    {
        public PatchClientException(string message)
            : base(message)
        {
        }

        public PatchClientException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}

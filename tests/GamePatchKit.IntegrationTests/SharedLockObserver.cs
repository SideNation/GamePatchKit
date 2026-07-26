namespace GamePatchKit.IntegrationTests;

// Shared between two ExclusivityTrackingStorageDecorator instances wrapping two SEPARATE FileSystemRuntimeStorage
// instances (simulating two processes), so the exclusivity proof is not weakened by both writers going through
// one shared object - an instance-local (in-memory) lock would pass a single-instance version of this test but
// fail a genuinely cross-instance one.
//
// ArmHoldOnNextEntry/ReleaseHold let a test force a real overlap window instead of hoping Task.WhenAll happens
// to schedule two writers into contention: the next holder to enter blocks there - with the real OS-level lock
// already acquired and held via its own FileSystemRuntimeStorage instance - until the test releases it, giving
// a second holder's own acquisition attempt a guaranteed window to either respect the lock or visibly break it.
internal sealed class SharedLockObserver
{
    private readonly object _gate = new();
    private int _activeCount;
    private TaskCompletionSource? _entered;
    private TaskCompletionSource? _holdGate;

    public int MaxObservedConcurrentLocks { get; private set; }

    public Task ArmHoldOnNextEntry()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_gate)
        {
            _entered = entered;
            _holdGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        return entered.Task;
    }

    public void ReleaseHold()
    {
        TaskCompletionSource? holdGate;

        lock (_gate)
        {
            holdGate = _holdGate;
            _holdGate = null;
        }

        holdGate?.TrySetResult();
    }

    public async Task EnterAsync()
    {
        Task? wait;

        lock (_gate)
        {
            _activeCount++;
            MaxObservedConcurrentLocks = Math.Max(MaxObservedConcurrentLocks, _activeCount);
            _entered?.TrySetResult();
            wait = _holdGate?.Task;
        }

        if (wait != null)
        {
            await wait.ConfigureAwait(false);
        }
    }

    public void Exit()
    {
        lock (_gate)
        {
            _activeCount--;
        }
    }
}

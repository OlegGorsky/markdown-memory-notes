namespace Notes.Sync;

public interface ISyncBackplaneRecoveryNotifier
{
    void SetRecoveredHandler(Func<CancellationToken, Task> handler);
}

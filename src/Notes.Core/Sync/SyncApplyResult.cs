namespace Notes.Core.Sync;

public enum SyncApplyResult
{
    Noop,
    Applied,
    Deleted,
    ConflictSaved,
    ConflictSkipped
}

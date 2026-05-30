using Notes.Core.Memory;

namespace Notes.Desktop.ViewModels;

public sealed class QuietMemoryItemViewModel
{
    public QuietMemoryItemViewModel(MemoryCandidate candidate)
    {
        Candidate = candidate;
    }

    public MemoryCandidate Candidate { get; }
    public string Title => Candidate.Note.Title;
    public string Reason => Candidate.Reason;
    public string Score => Candidate.Score.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

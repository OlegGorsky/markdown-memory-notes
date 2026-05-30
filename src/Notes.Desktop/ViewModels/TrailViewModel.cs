using Notes.Core.Trails;

namespace Notes.Desktop.ViewModels;

public sealed class TrailViewModel
{
    public TrailViewModel(Trail trail)
    {
        Trail = trail;
    }

    public Trail Trail { get; }
    public string Title => Trail.Title;
    public string Count => $"{Trail.Items.Count} items";
}

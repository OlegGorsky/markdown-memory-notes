namespace Notes.Core.Clock;

public interface IClock
{
    DateTimeOffset Now { get; }
}

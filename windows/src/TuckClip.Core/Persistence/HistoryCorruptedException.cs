namespace TuckClip.Core.Persistence;

public sealed class HistoryCorruptedException : Exception
{
    public HistoryCorruptedException(string message)
        : base(message)
    {
    }

    public HistoryCorruptedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

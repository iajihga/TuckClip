namespace TuckClip.Core.Persistence;

public interface IHistoryRepository
{
    Task<IReadOnlyList<ClipItem>> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(IReadOnlyList<ClipItem> items, CancellationToken cancellationToken = default);
}

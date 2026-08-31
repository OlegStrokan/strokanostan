using Domain.Entities;

namespace Domain.Interfaces;

public interface IProcessedEventRepository
{
    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default);

    Task AddAsync(ProcessedEvent processedEvent, CancellationToken cancellationToken = default);
}

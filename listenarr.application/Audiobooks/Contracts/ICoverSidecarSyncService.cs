namespace Listenarr.Application.Audiobooks.Contracts;

public enum CoverSidecarSyncStatus
{
    Disabled,
    Skipped,
    Written,
    Unchanged,
    Failed
}

public sealed record CoverSidecarSyncResult(
    CoverSidecarSyncStatus Status,
    string? Path = null,
    string? Message = null);

public interface ICoverSidecarSyncService
{
    Task<CoverSidecarSyncResult> SyncAsync(Audiobook audiobook, CancellationToken cancellationToken = default);
}

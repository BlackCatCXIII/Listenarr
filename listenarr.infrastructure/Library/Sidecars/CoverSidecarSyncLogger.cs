using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Sidecars;

internal static class CoverSidecarSyncLogger
{
    public static async Task SyncAsync(
        IServiceProvider serviceProvider,
        Audiobook audiobook,
        ILogger logger,
        string context,
        CancellationToken cancellationToken)
    {
        var coverSidecarSyncService = serviceProvider.GetService<ICoverSidecarSyncService>();
        if (coverSidecarSyncService == null)
        {
            return;
        }

        try
        {
            var result = await coverSidecarSyncService.SyncAsync(audiobook, cancellationToken);
            if (result.Status == CoverSidecarSyncStatus.Failed)
            {
                logger.LogWarning(
                    "Cover sidecar sync failed after {Context} for audiobook {AudiobookId}: {Message}",
                    context,
                    audiobook.Id,
                    result.Message);
            }
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
        {
            logger.LogWarning(
                exception,
                "Cover sidecar sync failed after {Context} for audiobook {AudiobookId}",
                context,
                audiobook.Id);
        }
    }
}

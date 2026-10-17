using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using RBACSIX.Data;

namespace RBACSIX.Services;

public sealed class ScheduledClassCleanupService : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);
    private const int BatchSize = 100;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledClassCleanupService> _logger;

    public ScheduledClassCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<ScheduledClassCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DeleteExpiredClassesAsync(stoppingToken);
            }
            catch (DbException exception)
            {
                _logger.LogError(exception, "Could not load expired Zoom classes for cleanup.");
            }

            await Task.Delay(CleanupInterval, stoppingToken);
        }
    }

    private async Task DeleteExpiredClassesAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var zoomMeetingService = scope.ServiceProvider.GetRequiredService<IZoomMeetingService>();
        var now = DateTime.UtcNow;
        var lastProcessedId = 0;
        while (true)
        {
            var expiredClasses = await db.ScheduledClasses
                .Where(scheduledClass =>
                    scheduledClass.Id > lastProcessedId &&
                    scheduledClass.StartTimeUtc.AddMinutes(scheduledClass.DurationMinutes) <= now)
                .OrderBy(scheduledClass => scheduledClass.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (expiredClasses.Count == 0)
                return;

            foreach (var scheduledClass in expiredClasses)
            {
                lastProcessedId = scheduledClass.Id;
                try
                {
                    await zoomMeetingService.DeleteMeetingAsync(
                        scheduledClass.ZoomMeetingId,
                        cancellationToken);
                }
                catch (ZoomMeetingException exception)
                {
                    _logger.LogWarning(
                        exception,
                        "Could not cancel Zoom meeting {MeetingId} for expired class {ScheduledClassId}; retaining the class for retry.",
                        scheduledClass.ZoomMeetingId,
                        scheduledClass.Id);
                    continue;
                }

                db.ScheduledClasses.Remove(scheduledClass);
                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation(
                        "Deleted expired scheduled class {ScheduledClassId} and Zoom meeting {MeetingId}.",
                        scheduledClass.Id,
                        scheduledClass.ZoomMeetingId);
                }
                catch (DbUpdateException exception)
                {
                    db.Entry(scheduledClass).State = EntityState.Detached;
                    _logger.LogError(
                        exception,
                        "Zoom meeting {MeetingId} was canceled, but expired class {ScheduledClassId} could not be deleted from the database.",
                        scheduledClass.ZoomMeetingId,
                        scheduledClass.Id);
                }
            }
        }
    }
}

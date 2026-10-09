using Microsoft.Extensions.Options;

namespace Biktal.WebMVC.Services;

/// <summary>
/// Creates a local database dump about once a week while this application is running.
/// A missed week is caught the next time the app starts.
/// </summary>
public sealed class WeeklyBackupHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly BackupOptions _options;
    private readonly ILogger<WeeklyBackupHostedService> _logger;

    public WeeklyBackupHostedService(
        IServiceScopeFactory scopes,
        IOptions<BackupOptions> options,
        ILogger<WeeklyBackupHostedService> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Weekly database backups are turned off.");
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var checkEvery = TimeSpan.FromHours(Math.Clamp(_options.CheckIntervalHours, 1, 24));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var backup = scope.ServiceProvider.GetRequiredService<DatabaseBackupService>();
                if (await backup.IsWeeklyBackupDueAsync(stoppingToken))
                {
                    _logger.LogInformation("Weekly backup is due. Writing a local dump.");
                    var result = await backup.CreateBackupAsync(DatabaseBackupService.WeeklyLabel, stoppingToken);
                    if (result.Success)
                        _logger.LogInformation("{Message}", result.Message);
                    else
                        _logger.LogError("Weekly backup failed: {Message}", result.Message);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Weekly backup check failed.");
            }

            try
            {
                await Task.Delay(checkEvery, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}

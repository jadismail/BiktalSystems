using System.Diagnostics;
using System.Security.Cryptography;
using Biktal.Domain.Admin;
using Biktal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Biktal.WebMVC.Services;

public sealed class DatabaseBackupService
{
    public const string WeeklyLabel = "Weekly backup";
    public const string ManualLabel = "Manual backup";

    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly BackupOptions _options;
    private readonly ILogger<DatabaseBackupService> _logger;

    public DatabaseBackupService(
        ApplicationDbContext db,
        IConfiguration configuration,
        IHostEnvironment environment,
        IOptions<BackupOptions> options,
        ILogger<DatabaseBackupService> logger)
    {
        _db = db;
        _configuration = configuration;
        _environment = environment;
        _options = options.Value;
        _logger = logger;
    }

    public int IntervalDays => Math.Clamp(_options.IntervalDays, 1, 365);

    public bool AutomaticBackupsEnabled => _options.Enabled;

    public string BackupDirectory => ResolveDirectory();

    public async Task<bool> IsWeeklyBackupDueAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return false;

        var last = await LastRealBackupAsync(cancellationToken);
        if (last is null)
            return true;

        return last.CompletedAtUtc <= DateTimeOffset.UtcNow.AddDays(-IntervalDays);
    }

    public async Task<AdminBackupRecord?> LastRealBackupAsync(CancellationToken cancellationToken)
    {
        return await _db.AdminBackupRecords.AsNoTracking()
            .Where(b => b.Status == AdminBackupStatus.Success && b.SizeBytes != null)
            .OrderByDescending(b => b.CompletedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public string? GetDownloadPath(AdminBackupRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.Notes))
            return null;

        var notes = record.Notes.Trim();
        if (!Path.IsPathRooted(notes))
            return null;

        string full;
        try
        {
            full = Path.GetFullPath(notes);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        var name = Path.GetFileName(full);
        if (!name.StartsWith("ishop-", StringComparison.OrdinalIgnoreCase)
            || !name.EndsWith(".dump", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!IsUnderDirectory(full, BackupDirectory))
            return null;

        return File.Exists(full) ? full : null;
    }

    public async Task<BackupRunResult> CreateBackupAsync(string label, CancellationToken cancellationToken)
    {
        var safeLabel = string.IsNullOrWhiteSpace(label) ? ManualLabel : label.Trim();
        if (safeLabel.Length > 256)
            safeLabel = safeLabel[..256];

        if (!await Gate.WaitAsync(0, cancellationToken))
            return new BackupRunResult(false, "A backup is already running. Try again in a minute.", null);

        try
        {
            return await CreateBackupCoreAsync(safeLabel, cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<BackupRunResult> CreateBackupCoreAsync(string label, CancellationToken cancellationToken)
    {
        string directory;
        try
        {
            directory = ResolveDirectory();
        }
        catch (Exception ex)
        {
            var message = "Could not create the local backup folder. " + ex.Message;
            await TryRecordFailureAsync(label, message, cancellationToken);
            return new BackupRunResult(false, message, null);
        }

        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            const string message = "Database connection string is missing, so a backup could not be created.";
            await TryRecordFailureAsync(label, message, cancellationToken);
            return new BackupRunResult(false, message, null);
        }

        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (Exception ex)
        {
            var message = "Database connection string could not be read. " + ex.Message;
            await TryRecordFailureAsync(label, message, cancellationToken);
            return new BackupRunResult(false, message, null);
        }

        if (string.IsNullOrWhiteSpace(builder.Database))
        {
            const string message = "The connection string has no database name.";
            await TryRecordFailureAsync(label, message, cancellationToken);
            return new BackupRunResult(false, message, null);
        }

        var pgDump = ResolvePgDumpPath();
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var finalPath = Path.Combine(directory, $"ishop-{stamp}.dump");
        if (File.Exists(finalPath))
        {
            var suffix = Guid.NewGuid().ToString("N")[..6];
            finalPath = Path.Combine(directory, $"ishop-{stamp}-{suffix}.dump");
        }
        var partialPath = finalPath + ".partial";

        var startInfo = new ProcessStartInfo
        {
            FileName = pgDump,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WorkingDirectory = directory
        };
        startInfo.ArgumentList.Add("--format=custom");
        startInfo.ArgumentList.Add("--no-owner");
        startInfo.ArgumentList.Add("--no-acl");
        startInfo.ArgumentList.Add("--file");
        startInfo.ArgumentList.Add(partialPath);
        startInfo.ArgumentList.Add("--host");
        startInfo.ArgumentList.Add(string.IsNullOrWhiteSpace(builder.Host) ? "localhost" : builder.Host);
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add(builder.Port.ToString());
        startInfo.ArgumentList.Add("--username");
        startInfo.ArgumentList.Add(string.IsNullOrWhiteSpace(builder.Username) ? "postgres" : builder.Username);
        startInfo.ArgumentList.Add("--dbname");
        startInfo.ArgumentList.Add(builder.Database);
        if (!string.IsNullOrEmpty(builder.Password))
            startInfo.Environment["PGPASSWORD"] = builder.Password;

        try
        {
            using var process = new Process { StartInfo = startInfo };
            try
            {
                if (!process.Start())
                    throw new InvalidOperationException("pg_dump did not start.");
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                var message = "PostgreSQL pg_dump was not found. Install the PostgreSQL tools, or set Backup:PgDumpPath to pg_dump.exe. " + ex.Message;
                await TryRecordFailureAsync(label, message, cancellationToken);
                return new BackupRunResult(false, message, null);
            }

            var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);

            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(30));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            await using var _ = linked.Token.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not stop pg_dump.");
                }
            });

            try
            {
                await process.WaitForExitAsync(linked.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                DeleteIfExists(partialPath);
                const string message = "The backup took longer than 30 minutes and was stopped.";
                await TryRecordFailureAsync(label, message, CancellationToken.None);
                return new BackupRunResult(false, message, null);
            }

            var stderr = await stderrTask;
            await stdoutTask;

            if (process.ExitCode != 0 || !File.Exists(partialPath))
            {
                DeleteIfExists(partialPath);
                var detail = string.IsNullOrWhiteSpace(stderr)
                    ? $"pg_dump exited with code {process.ExitCode}."
                    : stderr.Trim();
                var message = Truncate("Backup failed. " + detail, 2000);
                await TryRecordFailureAsync(label, message, cancellationToken);
                return new BackupRunResult(false, message, null);
            }

            File.Move(partialPath, finalPath, overwrite: true);

            var file = new FileInfo(finalPath);
            var checksum = await ComputeChecksumAsync(finalPath, cancellationToken);
            var record = new AdminBackupRecord
            {
                Id = Guid.NewGuid(),
                Label = label,
                Status = AdminBackupStatus.Success,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Checksum = checksum,
                Notes = finalPath,
                SizeBytes = file.Length
            };
            _db.AdminBackupRecords.Add(record);
            await _db.SaveChangesAsync(cancellationToken);
            ApplyRetention(directory);

            _logger.LogInformation("Database backup saved to {Path} ({Bytes} bytes).", finalPath, file.Length);
            return new BackupRunResult(true, $"Backup saved on this computer: {finalPath}", record);
        }
        catch (OperationCanceledException)
        {
            DeleteIfExists(partialPath);
            throw;
        }
        catch (Exception ex)
        {
            DeleteIfExists(partialPath);
            _logger.LogError(ex, "Database backup failed.");
            var message = Truncate("Backup failed. " + ex.Message, 2000);
            await TryRecordFailureAsync(label, message, CancellationToken.None);
            return new BackupRunResult(false, message, null);
        }
    }

    private async Task TryRecordFailureAsync(string label, string message, CancellationToken cancellationToken)
    {
        try
        {
            _db.ChangeTracker.Clear();
            _db.AdminBackupRecords.Add(new AdminBackupRecord
            {
                Id = Guid.NewGuid(),
                Label = label,
                Status = AdminBackupStatus.Failed,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Notes = Truncate(message, 2000),
                SizeBytes = null
            });
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not record the failed backup.");
        }
    }

    private void ApplyRetention(string directory)
    {
        var keep = Math.Clamp(_options.RetentionCount, 1, 100);
        try
        {
            var extra = new DirectoryInfo(directory)
                .EnumerateFiles("ishop-*.dump")
                .OrderByDescending(f => f.CreationTimeUtc)
                .Skip(keep)
                .ToList();
            foreach (var file in extra)
            {
                file.Delete();
                _logger.LogInformation("Removed old backup {Path}.", file.FullName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not remove old backup files.");
        }
    }

    private string ResolveDirectory()
    {
        var preferred = string.IsNullOrWhiteSpace(_options.Directory)
            ? @"C:\iShopBackups"
            : Path.GetFullPath(_options.Directory);
        var fallback = Path.Combine(_environment.ContentRootPath, "App_Data", "Backups");

        foreach (var directory in new[] { preferred, fallback }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                Directory.CreateDirectory(directory);
                var probe = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return directory;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                _logger.LogWarning(ex, "Backup folder {Directory} is not writable.", directory);
            }
        }

        throw new IOException($"Could not write backups to {preferred} or {fallback}.");
    }

    private string ResolvePgDumpPath()
    {
        if (!string.IsNullOrWhiteSpace(_options.PgDumpPath) && File.Exists(_options.PgDumpPath))
            return _options.PgDumpPath;

        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                 })
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            var postgres = Path.Combine(root, "PostgreSQL");
            if (!Directory.Exists(postgres))
                continue;

            var match = Directory.EnumerateDirectories(postgres)
                .Select(dir => Path.Combine(dir, "bin", "pg_dump.exe"))
                .Where(File.Exists)
                .OrderByDescending(path =>
                {
                    var version = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path))!);
                    return int.TryParse(version, out var number) ? number : 0;
                })
                .FirstOrDefault();
            if (match is not null)
                return match;
        }

        return "pg_dump";
    }

    private static async Task<string> ComputeChecksumAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool IsUnderDirectory(string fullPath, string directory)
    {
        var root = Path.GetFullPath(directory);
        if (!root.EndsWith(Path.DirectorySeparatorChar))
            root += Path.DirectorySeparatorChar;
        return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // The failed dump is already being reported.
        }
    }

    private static string Truncate(string value, int max)
    {
        var trimmed = value.ReplaceLineEndings(" ").Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}

public sealed record BackupRunResult(bool Success, string Message, AdminBackupRecord? Record);

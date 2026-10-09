namespace Biktal.WebMVC.Services;

public sealed class BackupOptions
{
    public const string SectionName = "Backup";

    /// <summary>When false, the weekly job does not run. Manual backups from Admin still work.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Folder on this computer. Empty uses C:\iShopBackups.</summary>
    public string? Directory { get; set; }

    public int IntervalDays { get; set; } = 7;

    /// <summary>How many dump files to keep in the folder. Older ishop-*.dump files are deleted.</summary>
    public int RetentionCount { get; set; } = 8;

    /// <summary>Optional full path to pg_dump.exe. Empty searches the local PostgreSQL install.</summary>
    public string? PgDumpPath { get; set; }

    public int CheckIntervalHours { get; set; } = 1;
}

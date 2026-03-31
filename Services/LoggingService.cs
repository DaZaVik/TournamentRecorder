using TournamentRecorder.Enums;
using TournamentRecorder.Models;
using TournamentRecorder.Services.Interfaces;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Streams;

namespace TournamentRecorder.Services;

public class LoggingService : ILoggingService
{
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly ISettingsService _settingsService;

    private StorageFile? _logFile;

    public string? CurrentLogFilePath => _logFile?.Path;

    public LoggingService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task InitializeSessionLoggingAsync(string sessionFolderName)
    {
        var rootFolder = await GetOrCreateRootFolderAsync();
        var sessionFolder = await rootFolder.CreateFolderAsync(sessionFolderName, CreationCollisionOption.OpenIfExists);
        var logsFolder = await sessionFolder.CreateFolderAsync("logs", CreationCollisionOption.OpenIfExists);

        _logFile = await logsFolder.CreateFileAsync("session.log", CreationCollisionOption.OpenIfExists);

        await LogInfoAsync("LoggingService", "Логирование сессии инициализировано");
    }

    public async Task LogAsync(
        LogLevelType level,
        string source,
        string message,
        int? slotNumber = null,
        string? cameraDisplayName = null,
        string? details = null)
    {
        if (_logFile is null)
        {
            return;
        }

        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Source = source,
            Message = message,
            SlotNumber = slotNumber,
            CameraDisplayName = cameraDisplayName,
            Details = details
        };

        var line = FormatLogEntry(entry);

        await _fileLock.WaitAsync();

        try
        {
            await FileIO.AppendTextAsync(_logFile, line, UnicodeEncoding.Utf8);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public Task LogInfoAsync(
        string source,
        string message,
        int? slotNumber = null,
        string? cameraDisplayName = null,
        string? details = null)
    {
        return LogAsync(LogLevelType.Info, source, message, slotNumber, cameraDisplayName, details);
    }

    public Task LogWarningAsync(
        string source,
        string message,
        int? slotNumber = null,
        string? cameraDisplayName = null,
        string? details = null)
    {
        return LogAsync(LogLevelType.Warning, source, message, slotNumber, cameraDisplayName, details);
    }

    public Task LogErrorAsync(
        string source,
        string message,
        int? slotNumber = null,
        string? cameraDisplayName = null,
        string? details = null)
    {
        return LogAsync(LogLevelType.Error, source, message, slotNumber, cameraDisplayName, details);
    }

    private async Task<StorageFolder> GetOrCreateRootFolderAsync()
    {
        var settings = _settingsService.CurrentSettings;

        if (!string.IsNullOrWhiteSpace(settings.RootRecordingsFolderToken))
        {
            try
            {
                return await StorageApplicationPermissions
                    .FutureAccessList
                    .GetFolderAsync(settings.RootRecordingsFolderToken);
            }
            catch
            {
                // Если токен протух или папка недоступна, уходим в fallback.
            }
        }

        var videosFolder = KnownFolders.VideosLibrary;
        var rootFolderName = settings.RootRecordingsFolder;

        if (string.IsNullOrWhiteSpace(rootFolderName))
        {
            rootFolderName = "турнир_записи";
        }

        return await videosFolder.CreateFolderAsync(rootFolderName, CreationCollisionOption.OpenIfExists);
    }

    private static string FormatLogEntry(LogEntry entry)
    {
        var slotText = entry.SlotNumber.HasValue ? $" | Slot={entry.SlotNumber.Value}" : string.Empty;
        var cameraText = !string.IsNullOrWhiteSpace(entry.CameraDisplayName)
            ? $" | Camera={entry.CameraDisplayName}"
            : string.Empty;
        var detailsText = !string.IsNullOrWhiteSpace(entry.Details)
            ? $" | Details={entry.Details}"
            : string.Empty;

        return $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}] [{entry.Level}] [{entry.Source}] {entry.Message}{slotText}{cameraText}{detailsText}{Environment.NewLine}";
    }
}
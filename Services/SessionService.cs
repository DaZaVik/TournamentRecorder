using TournamentRecorder.Models;
using TournamentRecorder.Services.Interfaces;

namespace TournamentRecorder.Services;

public class SessionService : ISessionService
{
    private readonly ILoggingService _loggingService;
    private readonly ISettingsService _settingsService;

    public SessionInfo CurrentSession { get; private set; } = new();

    public SessionService(
        ILoggingService loggingService,
        ISettingsService settingsService)
    {
        _loggingService = loggingService;
        _settingsService = settingsService;
    }

    public void CreateSession(int cameraCount)
    {
        var createdAt = DateTime.Now;
        var tournamentName = SanitizeFolderName(_settingsService.CurrentSettings.DefaultTournamentName);

        CurrentSession = new SessionInfo
        {
            CameraCount = cameraCount,
            CreatedAt = createdAt,
            SessionFolderName = $"{tournamentName}_{createdAt:yyyy-MM-dd_HH-mm-ss}"
        };

        _ = InitializeLoggingAsync(CurrentSession);
    }

    private async Task InitializeLoggingAsync(SessionInfo session)
    {
        await _loggingService.InitializeSessionLoggingAsync(session.SessionFolderName);
        await _loggingService.LogInfoAsync(
            "SessionService",
            $"Создана новая сессия: {session.SessionFolderName}. Количество камер: {session.CameraCount}");
    }

    private static string SanitizeFolderName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "турнир";
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(value
            .Trim()
            .Select(ch => invalidChars.Contains(ch) ? '_' : ch)
            .ToArray());

        sanitized = sanitized.Replace(' ', '_');

        return string.IsNullOrWhiteSpace(sanitized) ? "турнир" : sanitized;
    }
}
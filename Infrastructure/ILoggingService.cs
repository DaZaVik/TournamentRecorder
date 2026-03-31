using TournamentRecorder.Enums;

namespace TournamentRecorder.Services.Interfaces;

public interface ILoggingService
{
    Task InitializeSessionLoggingAsync(string sessionFolderName);

    Task LogAsync(
        LogLevelType level,
        string source,
        string message,
        int? slotNumber = null,
        string? cameraDisplayName = null,
        string? details = null);

    Task LogInfoAsync(
        string source,
        string message,
        int? slotNumber = null,
        string? cameraDisplayName = null,
        string? details = null);

    Task LogWarningAsync(
        string source,
        string message,
        int? slotNumber = null,
        string? cameraDisplayName = null,
        string? details = null);

    Task LogErrorAsync(
        string source,
        string message,
        int? slotNumber = null,
        string? cameraDisplayName = null,
        string? details = null);

    string? CurrentLogFilePath { get; }
}
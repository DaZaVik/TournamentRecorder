namespace TournamentRecorder.Models;

public class CameraCheckResult
{
    public string DeviceId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool IsSuccess { get; set; }

    public string StatusMessage { get; set; } = string.Empty;
}
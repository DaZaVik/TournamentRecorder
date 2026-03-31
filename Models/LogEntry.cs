using TournamentRecorder.Enums;

namespace TournamentRecorder.Models;

public class LogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;

    public LogLevelType Level { get; set; }

    public string Source { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public int? SlotNumber { get; set; }

    public string? CameraDisplayName { get; set; }

    public string? Details { get; set; }
}
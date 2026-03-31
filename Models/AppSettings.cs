namespace TournamentRecorder.Models;

public class AppSettings
{
    public string RootRecordingsFolder { get; set; } = "турнир_записи";

    public string? RootRecordingsFolderPath { get; set; }

    public string? RootRecordingsFolderToken { get; set; }

    public int SegmentDurationMinutes { get; set; } = 5;

    public string DefaultTournamentName { get; set; } = "турнир";

    public bool UseFixedWindowSize { get; set; } = true;

    public int DefaultWindowWidth { get; set; } = 1400;

    public int DefaultWindowHeight { get; set; } = 900;
    public int UiScalePercent { get; set; } = 100;
    public bool UseAutoWindowSizing { get; set; } = true;
}
namespace TournamentRecorder.Models;

public class SessionMetadata
{
    public string SessionFolderName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public int CameraCount { get; set; }

    public string AppVersion { get; set; } = "1.0";

    public string RootFolderName { get; set; } = "турнир_записи";

    public List<SessionCameraInfo> Cameras { get; set; } = new();
}
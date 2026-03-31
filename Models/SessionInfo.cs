namespace TournamentRecorder.Models;

public class SessionInfo
{
    public int CameraCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string SessionFolderName { get; set; } = string.Empty;
}
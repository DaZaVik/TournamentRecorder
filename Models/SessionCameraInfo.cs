namespace TournamentRecorder.Models;

public class SessionCameraInfo
{
    public int SlotNumber { get; set; }

    public string Comment { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public bool IsPreviewActive { get; set; }

    public bool IsRecording { get; set; }

    public string? SelectedCameraDisplayName { get; set; }

    public string? SelectedCameraDeviceId { get; set; }
}
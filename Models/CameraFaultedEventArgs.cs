namespace TournamentRecorder.Models;

public class CameraFaultedEventArgs : EventArgs
{
    public int SlotNumber { get; set; }

    public string CameraDisplayName { get; set; } = string.Empty;

    public string ErrorMessage { get; set; } = string.Empty;

    public bool WasRecording { get; set; }

    public string Comment { get; set; } = string.Empty;

    public string SessionFolderName { get; set; } = string.Empty;
}
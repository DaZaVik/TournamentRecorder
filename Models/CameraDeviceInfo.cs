namespace TournamentRecorder.Models;

public class CameraDeviceInfo
{
    public string DeviceId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public override string ToString()
    {
        return DisplayName;
    }
}
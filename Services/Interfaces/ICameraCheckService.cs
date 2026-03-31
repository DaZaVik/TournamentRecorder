using TournamentRecorder.Models;

namespace TournamentRecorder.Services.Interfaces;

public interface ICameraCheckService
{
    Task<CameraCheckResult> CheckCameraAsync(CameraDeviceInfo cameraDevice, CancellationToken cancellationToken = default);
}
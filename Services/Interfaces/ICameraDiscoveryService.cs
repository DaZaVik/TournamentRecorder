using TournamentRecorder.Models;

namespace TournamentRecorder.Services.Interfaces;

public interface ICameraDiscoveryService
{
    Task<IReadOnlyList<CameraDeviceInfo>> GetAvailableCamerasAsync(CancellationToken cancellationToken = default);

    Task<bool> IsCameraAvailableAsync(string deviceId, CancellationToken cancellationToken = default);
}
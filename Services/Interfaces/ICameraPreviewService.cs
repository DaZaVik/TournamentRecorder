using Microsoft.UI.Xaml.Controls;
using TournamentRecorder.Models;

namespace TournamentRecorder.Services.Interfaces;

public interface ICameraPreviewService
{
    event EventHandler<CameraFaultedEventArgs>? CameraFaulted;

    Task<OperationResult> StartPreviewAsync(
        int slotNumber,
        CameraDeviceInfo cameraDevice,
        MediaPlayerElement previewElement,
        CancellationToken cancellationToken = default);

    Task StopPreviewAsync(int slotNumber);

    Task StopAllPreviewsAsync();

    Task<RecordingResult> StartRecordingAsync(
        int slotNumber,
        string comment,
        string sessionFolderName,
        CancellationToken cancellationToken = default);

    Task<RecordingResult> StopRecordingAsync(int slotNumber);

    Task<RecordingResult> SaveSegmentAsync(
        int slotNumber,
        string comment,
        string sessionFolderName,
        CancellationToken cancellationToken = default);

    Task<OperationResult> TryRecoverCameraAsync(
        int slotNumber,
        MediaPlayerElement previewElement,
        CancellationToken cancellationToken = default);

    Task<RecordingResult?> TryResumeRecordingAfterRecoveryAsync(
        int slotNumber,
        CancellationToken cancellationToken = default);

    void ForceDeviceLost(int slotNumber, string reason);

    bool IsRecording(int slotNumber);

    bool IsDeviceLost(int slotNumber);

    bool IsRecovering(int slotNumber);

    int GetRecoveryAttempts(int slotNumber);

    string? GetLastDeviceError(int slotNumber);

    DateTimeOffset? GetCurrentSegmentStartedAt(int slotNumber);
}
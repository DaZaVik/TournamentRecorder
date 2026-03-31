using TournamentRecorder.Models;
using TournamentRecorder.Services.Interfaces;
using Windows.Media.Capture;

namespace TournamentRecorder.Services;

public class CameraCheckService : ICameraCheckService
{
    public async Task<CameraCheckResult> CheckCameraAsync(CameraDeviceInfo cameraDevice, CancellationToken cancellationToken = default)
    {
        var result = new CameraCheckResult
        {
            DeviceId = cameraDevice.DeviceId,
            DisplayName = cameraDevice.DisplayName
        };

        MediaCapture? mediaCapture = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            mediaCapture = new MediaCapture();

            var settings = new MediaCaptureInitializationSettings
            {
                VideoDeviceId = cameraDevice.DeviceId,
                StreamingCaptureMode = StreamingCaptureMode.Video
            };

            await mediaCapture.InitializeAsync(settings).AsTask(cancellationToken);

            result.IsSuccess = true;
            result.StatusMessage = "Связь есть";
        }
        catch (OperationCanceledException)
        {
            result.IsSuccess = false;
            result.StatusMessage = "Проверка отменена";
        }
        catch (UnauthorizedAccessException)
        {
            result.IsSuccess = false;
            result.StatusMessage = "Нет доступа к камере";
        }
        catch (Exception ex)
        {
            result.IsSuccess = false;
            result.StatusMessage = string.IsNullOrWhiteSpace(ex.Message)
                ? "Ошибка подключения"
                : $"Ошибка подключения: {ex.Message}";
        }
        finally
        {
            mediaCapture?.Dispose();
        }

        return result;
    }
}
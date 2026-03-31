using Microsoft.UI.Xaml.Controls;
using TournamentRecorder.Models;
using TournamentRecorder.Services.Interfaces;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.AccessCache;

namespace TournamentRecorder.Services;

public class CameraPreviewService : ICameraPreviewService
{
    private sealed class PreviewSession
    {
        public CameraDeviceInfo? CameraDevice { get; set; }
        public MediaCapture? MediaCapture { get; set; }
        public MediaPlayer? MediaPlayer { get; set; }
        public MediaPlayerElement? PreviewElement { get; set; }
        public MediaFrameSource? FrameSource { get; set; }

        public bool IsRecording { get; set; }
        public bool IsDeviceLost { get; set; }
        public bool IsRecovering { get; set; }
        public int RecoveryAttempts { get; set; }
        public bool WasRecordingBeforeFailure { get; set; }

        public string? LastErrorMessage { get; set; }

        public StorageFile? CurrentRecordingFile { get; set; }

        public string CurrentComment { get; set; } = string.Empty;
        public string CurrentSessionFolderName { get; set; } = string.Empty;

        public DateTimeOffset? CurrentSegmentStartedAt { get; set; }

        public PeriodicTimer? SegmentTimer { get; set; }
        public CancellationTokenSource? SegmentTimerCancellationTokenSource { get; set; }

        public MediaCaptureFailedEventHandler? FailedHandler { get; set; }

        public SemaphoreSlim RecordingLock { get; } = new(1, 1);
    }

    private sealed class PreviewRuntime
    {
        public required MediaCapture MediaCapture { get; init; }
        public required MediaPlayer MediaPlayer { get; init; }
        public required MediaFrameSource FrameSource { get; init; }
        public required MediaCaptureFailedEventHandler FailedHandler { get; init; }
    }

    private readonly Dictionary<int, PreviewSession> _sessions = new();
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    private readonly ILoggingService _loggingService;
    private readonly ISettingsService _settingsService;

    public event EventHandler<CameraFaultedEventArgs>? CameraFaulted;

    public CameraPreviewService(
        ILoggingService loggingService,
        ISettingsService settingsService)
    {
        _loggingService = loggingService;
        _settingsService = settingsService;
    }

    public async Task<OperationResult> StartPreviewAsync(
        int slotNumber,
        CameraDeviceInfo cameraDevice,
        MediaPlayerElement previewElement,
        CancellationToken cancellationToken = default)
    {
        await _syncLock.WaitAsync(cancellationToken);

        try
        {
            await StopPreviewInternalAsync(slotNumber);

            var runtime = await CreatePreviewRuntimeAsync(
                slotNumber,
                cameraDevice,
                cancellationToken);

            previewElement.AreTransportControlsEnabled = false;
            previewElement.SetMediaPlayer(runtime.MediaPlayer);

            _sessions[slotNumber] = new PreviewSession
            {
                CameraDevice = cameraDevice,
                MediaCapture = runtime.MediaCapture,
                MediaPlayer = runtime.MediaPlayer,
                PreviewElement = previewElement,
                FrameSource = runtime.FrameSource,
                FailedHandler = runtime.FailedHandler,
                IsRecording = false,
                IsDeviceLost = false,
                IsRecovering = false,
                RecoveryAttempts = 0,
                WasRecordingBeforeFailure = false,
                LastErrorMessage = null
            };

            runtime.MediaPlayer.Play();

            await _loggingService.LogInfoAsync(
                "CameraPreviewService",
                "Превью успешно запущено",
                slotNumber,
                cameraDevice.DisplayName);

            return OperationResult.Success("Превью активно");
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Failure("Запуск превью отменён");
        }
        catch (UnauthorizedAccessException ex)
        {
            await _loggingService.LogErrorAsync(
                "CameraPreviewService",
                "Нет доступа к камере при запуске превью",
                slotNumber,
                cameraDevice.DisplayName,
                ex.Message);

            return OperationResult.Failure("Нет доступа к камере");
        }
        catch (Exception ex)
        {
            await _loggingService.LogErrorAsync(
                "CameraPreviewService",
                "Не удалось запустить превью",
                slotNumber,
                cameraDevice.DisplayName,
                ex.ToString());

            return OperationResult.Failure(
                string.IsNullOrWhiteSpace(ex.Message)
                    ? "Не удалось запустить превью"
                    : $"Не удалось запустить превью: {ex.Message}");
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task StopPreviewAsync(int slotNumber)
    {
        await _syncLock.WaitAsync();

        try
        {
            await StopPreviewInternalAsync(slotNumber);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task StopAllPreviewsAsync()
    {
        await _syncLock.WaitAsync();

        try
        {
            var slotNumbers = _sessions.Keys.ToList();

            foreach (var slotNumber in slotNumbers)
            {
                await StopPreviewInternalAsync(slotNumber);
            }
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task<RecordingResult> StartRecordingAsync(
        int slotNumber,
        string comment,
        string sessionFolderName,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(slotNumber, out var session) || session.MediaCapture is null)
        {
            return RecordingResult.Failure("Превью не инициализировано");
        }

        if (session.IsDeviceLost)
        {
            return RecordingResult.Failure("Камера потеряна. Восстановите подключение.");
        }

        await session.RecordingLock.WaitAsync(cancellationToken);

        try
        {
            if (session.IsRecording)
            {
                return RecordingResult.Failure("Запись уже идёт");
            }

            var safeComment = SanitizeComment(comment, slotNumber);

            var recordingFile = await CreateRecordingFileAsync(safeComment, sessionFolderName);
            var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto);

            await session.MediaCapture.StartRecordToStorageFileAsync(profile, recordingFile).AsTask(cancellationToken);

            session.IsRecording = true;
            session.CurrentRecordingFile = recordingFile;
            session.CurrentComment = safeComment;
            session.CurrentSessionFolderName = sessionFolderName;
            session.CurrentSegmentStartedAt = DateTimeOffset.Now;
            session.IsDeviceLost = false;
            session.LastErrorMessage = null;
            session.WasRecordingBeforeFailure = false;

            StartSegmentTimer(slotNumber, session);

            await _loggingService.LogInfoAsync(
                "CameraPreviewService",
                "Запись успешно запущена",
                slotNumber,
                session.CameraDevice?.DisplayName,
                recordingFile.Path);

            return RecordingResult.Success("Идёт запись", recordingFile.Path);
        }
        catch (OperationCanceledException)
        {
            return RecordingResult.Failure("Запуск записи отменён");
        }
        catch (UnauthorizedAccessException)
        {
            return RecordingResult.Failure("Нет доступа к камере или файлу записи");
        }
        catch (Exception ex)
        {
            return RecordingResult.Failure(
                string.IsNullOrWhiteSpace(ex.Message)
                    ? "Не удалось запустить запись"
                    : $"Не удалось запустить запись: {ex.Message}");
        }
        finally
        {
            session.RecordingLock.Release();
        }
    }

    public async Task<RecordingResult> StopRecordingAsync(int slotNumber)
    {
        if (!_sessions.TryGetValue(slotNumber, out var session) || session.MediaCapture is null)
        {
            return RecordingResult.Failure("Сессия камеры не найдена");
        }

        await session.RecordingLock.WaitAsync();

        try
        {
            if (!session.IsRecording)
            {
                return RecordingResult.Failure("Запись не запущена");
            }

            StopSegmentTimer(session);

            var filePath = session.CurrentRecordingFile?.Path;

            await session.MediaCapture.StopRecordAsync();

            session.IsRecording = false;
            session.CurrentRecordingFile = null;
            session.CurrentSegmentStartedAt = null;
            session.WasRecordingBeforeFailure = false;

            await _loggingService.LogInfoAsync(
                "CameraPreviewService",
                "Запись остановлена",
                slotNumber,
                session.CameraDevice?.DisplayName,
                filePath);

            return RecordingResult.Success("Запись остановлена", filePath);
        }
        catch (Exception ex)
        {
            return RecordingResult.Failure(
                string.IsNullOrWhiteSpace(ex.Message)
                    ? "Не удалось остановить запись"
                    : $"Не удалось остановить запись: {ex.Message}");
        }
        finally
        {
            session.RecordingLock.Release();
        }
    }

    public async Task<RecordingResult> SaveSegmentAsync(
        int slotNumber,
        string comment,
        string sessionFolderName,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(slotNumber, out var session) || session.MediaCapture is null)
        {
            return RecordingResult.Failure("Сессия камеры не найдена");
        }

        if (session.IsDeviceLost)
        {
            return RecordingResult.Failure("Камера потеряна. Сегмент сохранить корректно невозможно.");
        }

        await session.RecordingLock.WaitAsync(cancellationToken);

        try
        {
            if (!session.IsRecording)
            {
                return RecordingResult.Failure("Запись не запущена");
            }

            var safeComment = SanitizeComment(comment, slotNumber);
            var currentFilePath = session.CurrentRecordingFile?.Path;

            StopSegmentTimer(session);

            await session.MediaCapture.StopRecordAsync();

            var newFile = await CreateRecordingFileAsync(safeComment, sessionFolderName);
            var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto);

            await session.MediaCapture.StartRecordToStorageFileAsync(profile, newFile).AsTask(cancellationToken);

            session.IsRecording = true;
            session.CurrentRecordingFile = newFile;
            session.CurrentComment = safeComment;
            session.CurrentSessionFolderName = sessionFolderName;
            session.CurrentSegmentStartedAt = DateTimeOffset.Now;
            session.IsDeviceLost = false;
            session.LastErrorMessage = null;

            StartSegmentTimer(slotNumber, session);

            await _loggingService.LogInfoAsync(
                "CameraPreviewService",
                "Сегмент сохранён, начат новый",
                slotNumber,
                session.CameraDevice?.DisplayName,
                $"Предыдущий файл: {currentFilePath}; Новый файл: {newFile.Path}");

            return RecordingResult.Success(
                $"Сегмент сохранён. Новый сегмент начат. Предыдущий файл: {currentFilePath}",
                newFile.Path);
        }
        catch (OperationCanceledException)
        {
            return RecordingResult.Failure("Сохранение сегмента отменено");
        }
        catch (Exception ex)
        {
            return RecordingResult.Failure(
                string.IsNullOrWhiteSpace(ex.Message)
                    ? "Не удалось сохранить сегмент"
                    : $"Не удалось сохранить сегмент: {ex.Message}");
        }
        finally
        {
            session.RecordingLock.Release();
        }
    }

    public async Task<OperationResult> TryRecoverCameraAsync(
        int slotNumber,
        MediaPlayerElement previewElement,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(slotNumber, out var session))
        {
            return OperationResult.Failure("Сессия камеры не найдена");
        }

        if (session.CameraDevice is null)
        {
            return OperationResult.Failure("Камера для восстановления не определена");
        }

        if (!session.IsDeviceLost)
        {
            return OperationResult.Success("Камера уже активна");
        }

        if (session.IsRecovering)
        {
            return OperationResult.Failure("Восстановление уже выполняется");
        }

        session.IsRecovering = true;
        session.RecoveryAttempts++;

        await _loggingService.LogWarningAsync(
            "CameraPreviewService",
            "Запущена попытка восстановления камеры",
            slotNumber,
            session.CameraDevice.DisplayName,
            $"Попытка #{session.RecoveryAttempts}");

        try
        {
            await Task.Delay(1500, cancellationToken);

            var device = session.CameraDevice;
            var comment = session.CurrentComment;
            var sessionFolderName = session.CurrentSessionFolderName;
            var wasRecording = session.WasRecordingBeforeFailure;

            PreviewRuntime runtime;
            try
            {
                runtime = await CreatePreviewRuntimeAsync(
                    slotNumber,
                    device,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                session.IsDeviceLost = true;
                session.LastErrorMessage = string.IsNullOrWhiteSpace(ex.Message)
                    ? "Не удалось восстановить камеру"
                    : ex.Message;

                return OperationResult.Failure(
                    string.IsNullOrWhiteSpace(ex.Message)
                        ? "Не удалось восстановить камеру"
                        : $"Не удалось восстановить камеру: {ex.Message}");
            }

            await ReleaseRuntimeResourcesAsync(session);

            previewElement.AreTransportControlsEnabled = false;
            previewElement.SetMediaPlayer(runtime.MediaPlayer);

            session.MediaCapture = runtime.MediaCapture;
            session.MediaPlayer = runtime.MediaPlayer;
            session.FrameSource = runtime.FrameSource;
            session.FailedHandler = runtime.FailedHandler;
            session.PreviewElement = previewElement;

            session.IsDeviceLost = false;
            session.LastErrorMessage = null;
            session.CurrentComment = comment;
            session.CurrentSessionFolderName = sessionFolderName;
            session.WasRecordingBeforeFailure = wasRecording;

            runtime.MediaPlayer.Play();

            await _loggingService.LogInfoAsync(
                "CameraPreviewService",
                "Камера успешно восстановлена",
                slotNumber,
                device.DisplayName);

            return OperationResult.Success("Восстановлено");
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Failure("Восстановление отменено");
        }
        catch (Exception ex)
        {
            session.IsDeviceLost = true;
            session.LastErrorMessage = string.IsNullOrWhiteSpace(ex.Message)
                ? "Не удалось восстановить камеру"
                : ex.Message;

            await _loggingService.LogErrorAsync(
                "CameraPreviewService",
                "Ошибка при восстановлении камеры",
                slotNumber,
                session.CameraDevice.DisplayName,
                ex.ToString());

            return OperationResult.Failure(
                string.IsNullOrWhiteSpace(ex.Message)
                    ? "Не удалось восстановить камеру"
                    : $"Не удалось восстановить камеру: {ex.Message}");
        }
        finally
        {
            session.IsRecovering = false;
        }
    }

    public async Task<RecordingResult?> TryResumeRecordingAfterRecoveryAsync(
        int slotNumber,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(slotNumber, out var session))
        {
            return null;
        }

        if (!session.WasRecordingBeforeFailure)
        {
            return null;
        }

        if (session.CameraDevice is null)
        {
            return RecordingResult.Failure("Камера для возобновления записи не определена");
        }

        var result = await StartRecordingAsync(
            slotNumber,
            session.CurrentComment,
            session.CurrentSessionFolderName,
            cancellationToken);

        if (result.IsSuccess)
        {
            session.WasRecordingBeforeFailure = false;

            await _loggingService.LogInfoAsync(
                "CameraPreviewService",
                "Запись после восстановления камеры возобновлена",
                slotNumber,
                session.CameraDevice.DisplayName,
                result.FilePath);
        }

        return result;
    }

    public void ForceDeviceLost(int slotNumber, string reason)
    {
        if (!_sessions.TryGetValue(slotNumber, out var session))
        {
            return;
        }

        if (session.IsDeviceLost)
        {
            return;
        }

        var wasRecording = session.IsRecording;

        session.IsDeviceLost = true;
        session.LastErrorMessage = string.IsNullOrWhiteSpace(reason)
            ? "Потеря связи с камерой"
            : reason;

        session.WasRecordingBeforeFailure = wasRecording;
        session.IsRecording = false;
        session.CurrentSegmentStartedAt = null;
        session.CurrentRecordingFile = null;

        StopSegmentTimer(session);

        _ = _loggingService.LogErrorAsync(
            "CameraPreviewService",
            "Камера принудительно помечена как потерянная",
            slotNumber,
            session.CameraDevice?.DisplayName,
            session.LastErrorMessage);

        CameraFaulted?.Invoke(this, new CameraFaultedEventArgs
        {
            SlotNumber = slotNumber,
            CameraDisplayName = session.CameraDevice?.DisplayName ?? string.Empty,
            ErrorMessage = session.LastErrorMessage,
            WasRecording = wasRecording,
            Comment = session.CurrentComment,
            SessionFolderName = session.CurrentSessionFolderName
        });
    }

    public bool IsRecording(int slotNumber)
    {
        return _sessions.TryGetValue(slotNumber, out var session) && session.IsRecording;
    }

    public bool IsDeviceLost(int slotNumber)
    {
        return _sessions.TryGetValue(slotNumber, out var session) && session.IsDeviceLost;
    }

    public bool IsRecovering(int slotNumber)
    {
        return _sessions.TryGetValue(slotNumber, out var session) && session.IsRecovering;
    }

    public int GetRecoveryAttempts(int slotNumber)
    {
        return _sessions.TryGetValue(slotNumber, out var session) ? session.RecoveryAttempts : 0;
    }

    public string? GetLastDeviceError(int slotNumber)
    {
        return _sessions.TryGetValue(slotNumber, out var session)
            ? session.LastErrorMessage
            : null;
    }

    public DateTimeOffset? GetCurrentSegmentStartedAt(int slotNumber)
    {
        return _sessions.TryGetValue(slotNumber, out var session)
            ? session.CurrentSegmentStartedAt
            : null;
    }

    private async Task<PreviewRuntime> CreatePreviewRuntimeAsync(
        int slotNumber,
        CameraDeviceInfo cameraDevice,
        CancellationToken cancellationToken)
    {
        var mediaCapture = new MediaCapture();

        try
        {
            var settings = new MediaCaptureInitializationSettings
            {
                VideoDeviceId = cameraDevice.DeviceId,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                SharingMode = MediaCaptureSharingMode.SharedReadOnly,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu
            };

            await mediaCapture.InitializeAsync(settings).AsTask(cancellationToken);

            var previewSource = mediaCapture.FrameSources
                .FirstOrDefault(source =>
                    source.Value.Info.MediaStreamType == MediaStreamType.VideoPreview &&
                    source.Value.Info.SourceKind == MediaFrameSourceKind.Color)
                .Value;

            if (previewSource is null)
            {
                previewSource = mediaCapture.FrameSources
                    .FirstOrDefault(source =>
                        source.Value.Info.MediaStreamType == MediaStreamType.VideoRecord &&
                        source.Value.Info.SourceKind == MediaFrameSourceKind.Color)
                    .Value;
            }

            if (previewSource is null)
            {
                throw new InvalidOperationException("У камеры нет подходящего потока превью");
            }

            var mediaPlayer = new MediaPlayer
            {
                AutoPlay = false,
                RealTimePlayback = true,
                Source = MediaSource.CreateFromMediaFrameSource(previewSource)
            };

            MediaCaptureFailedEventHandler failedHandler = (sender, args) =>
            {
                HandleMediaCaptureFailed(slotNumber, cameraDevice.DisplayName, args);
            };

            mediaCapture.Failed += failedHandler;

            return new PreviewRuntime
            {
                MediaCapture = mediaCapture,
                MediaPlayer = mediaPlayer,
                FrameSource = previewSource,
                FailedHandler = failedHandler
            };
        }
        catch
        {
            try
            {
                mediaCapture.Dispose();
            }
            catch
            {
            }

            throw;
        }
    }

    private async Task ReleaseRuntimeResourcesAsync(PreviewSession session)
    {
        try
        {
            if (session.MediaCapture is not null && session.FailedHandler is not null)
            {
                session.MediaCapture.Failed -= session.FailedHandler;
            }
        }
        catch
        {
        }

        try
        {
            if (session.MediaPlayer is not null)
            {
                session.MediaPlayer.Pause();
                session.MediaPlayer.Source = null;
            }

            if (session.PreviewElement is not null)
            {
                session.PreviewElement.SetMediaPlayer(null);
            }
        }
        catch
        {
        }

        try
        {
            session.MediaPlayer?.Dispose();
        }
        catch
        {
        }

        if (session.MediaCapture is not null)
        {
            try
            {
                await session.MediaCapture.StopPreviewAsync();
            }
            catch
            {
            }

            try
            {
                session.MediaCapture.Dispose();
            }
            catch
            {
            }
        }

        session.MediaCapture = null;
        session.MediaPlayer = null;
        session.FrameSource = null;
        session.FailedHandler = null;
        session.PreviewElement = null;
    }

    private void StartSegmentTimer(int slotNumber, PreviewSession session)
    {
        StopSegmentTimer(session);

        var segmentMinutes = _settingsService.CurrentSettings.SegmentDurationMinutes;
        if (segmentMinutes <= 0)
        {
            segmentMinutes = 5;
        }

        session.SegmentTimerCancellationTokenSource = new CancellationTokenSource();
        session.SegmentTimer = new PeriodicTimer(TimeSpan.FromMinutes(segmentMinutes));

        _ = RunSegmentTimerLoopAsync(slotNumber, session, session.SegmentTimerCancellationTokenSource.Token);
    }

    private async Task RunSegmentTimerLoopAsync(int slotNumber, PreviewSession session, CancellationToken cancellationToken)
    {
        try
        {
            if (session.SegmentTimer is null)
            {
                return;
            }

            while (await session.SegmentTimer.WaitForNextTickAsync(cancellationToken))
            {
                if (!session.IsRecording || session.IsDeviceLost)
                {
                    break;
                }

                await RotateSegmentInternalAsync(slotNumber, session, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }

    private async Task RotateSegmentInternalAsync(
        int slotNumber,
        PreviewSession session,
        CancellationToken cancellationToken)
    {
        await session.RecordingLock.WaitAsync(cancellationToken);

        try
        {
            if (!session.IsRecording || session.MediaCapture is null || session.IsDeviceLost)
            {
                return;
            }

            var safeComment = SanitizeComment(session.CurrentComment, slotNumber);

            await session.MediaCapture.StopRecordAsync();

            var newFile = await CreateRecordingFileAsync(safeComment, session.CurrentSessionFolderName);
            var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto);

            await session.MediaCapture.StartRecordToStorageFileAsync(profile, newFile).AsTask(cancellationToken);

            session.CurrentRecordingFile = newFile;
            session.CurrentSegmentStartedAt = DateTimeOffset.Now;

            await _loggingService.LogInfoAsync(
                "CameraPreviewService",
                "Автоматический разрез сегмента выполнен",
                slotNumber,
                session.CameraDevice?.DisplayName,
                newFile.Path);
        }
        finally
        {
            session.RecordingLock.Release();
        }
    }

    private void HandleMediaCaptureFailed(int slotNumber, string cameraDisplayName, MediaCaptureFailedEventArgs args)
    {
        var wasRecording = false;
        var comment = string.Empty;
        var sessionFolderName = string.Empty;

        if (_sessions.TryGetValue(slotNumber, out var session))
        {
            wasRecording = session.IsRecording;
            comment = session.CurrentComment;
            sessionFolderName = session.CurrentSessionFolderName;

            session.IsDeviceLost = true;
            session.LastErrorMessage = string.IsNullOrWhiteSpace(args.Message)
                ? "Потеря связи с камерой"
                : args.Message;

            session.WasRecordingBeforeFailure = wasRecording;
            session.IsRecording = false;
            session.CurrentSegmentStartedAt = null;
            session.CurrentRecordingFile = null;

            StopSegmentTimer(session);
        }

        _ = _loggingService.LogErrorAsync(
            "CameraPreviewService",
            "Потеря связи с камерой",
            slotNumber,
            cameraDisplayName,
            args.Message);

        CameraFaulted?.Invoke(this, new CameraFaultedEventArgs
        {
            SlotNumber = slotNumber,
            CameraDisplayName = cameraDisplayName,
            ErrorMessage = string.IsNullOrWhiteSpace(args.Message)
                ? "Потеря связи с камерой"
                : args.Message,
            WasRecording = wasRecording,
            Comment = comment,
            SessionFolderName = sessionFolderName
        });
    }

    private static void StopSegmentTimer(PreviewSession session)
    {
        try
        {
            session.SegmentTimerCancellationTokenSource?.Cancel();
        }
        catch
        {
        }

        session.SegmentTimer?.Dispose();
        session.SegmentTimer = null;

        session.SegmentTimerCancellationTokenSource?.Dispose();
        session.SegmentTimerCancellationTokenSource = null;
    }

    private async Task StopPreviewInternalAsync(int slotNumber)
    {
        if (!_sessions.TryGetValue(slotNumber, out var session))
        {
            return;
        }

        StopSegmentTimer(session);

        if (session.IsRecording && session.MediaCapture is not null)
        {
            try
            {
                await session.MediaCapture.StopRecordAsync();
            }
            catch
            {
            }

            session.IsRecording = false;
            session.CurrentRecordingFile = null;
            session.CurrentSegmentStartedAt = null;
        }

        await ReleaseRuntimeResourcesAsync(session);

        _sessions.Remove(slotNumber);
    }

    private async Task<StorageFile> CreateRecordingFileAsync(string safeComment, string sessionFolderName)
    {
        var rootFolder = await GetOrCreateRootFolderAsync();
        var sessionFolder = await rootFolder.CreateFolderAsync(sessionFolderName, CreationCollisionOption.OpenIfExists);
        var cameraFolder = await sessionFolder.CreateFolderAsync(safeComment, CreationCollisionOption.OpenIfExists);

        var fileName = $"{safeComment}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mp4";
        return await cameraFolder.CreateFileAsync(fileName, CreationCollisionOption.GenerateUniqueName);
    }

    private async Task<StorageFolder> GetOrCreateRootFolderAsync()
    {
        var settings = _settingsService.CurrentSettings;

        if (!string.IsNullOrWhiteSpace(settings.RootRecordingsFolderToken))
        {
            try
            {
                return await StorageApplicationPermissions
                    .FutureAccessList
                    .GetFolderAsync(settings.RootRecordingsFolderToken);
            }
            catch
            {
            }
        }

        var videosFolder = KnownFolders.VideosLibrary;
        var rootFolderName = settings.RootRecordingsFolder;

        if (string.IsNullOrWhiteSpace(rootFolderName))
        {
            rootFolderName = "турнир_записи";
        }

        return await videosFolder.CreateFolderAsync(rootFolderName, CreationCollisionOption.OpenIfExists);
    }

    private static string SanitizeComment(string comment, int slotNumber)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return $"camera_{slotNumber}";
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(comment
            .Trim()
            .Select(ch => invalidChars.Contains(ch) ? '_' : ch)
            .ToArray());

        while (sanitized.Contains("  "))
        {
            sanitized = sanitized.Replace("  ", " ");
        }

        sanitized = sanitized.Replace(' ', '_');

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return $"camera_{slotNumber}";
        }

        return sanitized;
    }
}
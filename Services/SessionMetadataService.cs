using System.Text.Json;
using TournamentRecorder.Models;
using TournamentRecorder.Services.Interfaces;
using Windows.Storage;
using Windows.Storage.AccessCache;

namespace TournamentRecorder.Services;

public class SessionMetadataService : ISessionMetadataService
{
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly ISettingsService _settingsService;

    private StorageFile? _metadataFile;

    public string? CurrentMetadataFilePath => _metadataFile?.Path;

    public SessionMetadataService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task InitializeAsync(SessionInfo sessionInfo)
    {
        var rootFolder = await GetOrCreateRootFolderAsync();
        var sessionFolder = await rootFolder.CreateFolderAsync(
            sessionInfo.SessionFolderName,
            CreationCollisionOption.OpenIfExists);

        _metadataFile = await sessionFolder.CreateFileAsync(
            "session_info.json",
            CreationCollisionOption.ReplaceExisting);

        var initialMetadata = new SessionMetadata
        {
            SessionFolderName = sessionInfo.SessionFolderName,
            CreatedAt = sessionInfo.CreatedAt,
            CameraCount = sessionInfo.CameraCount,
            AppVersion = "1.0",
            RootFolderName = _settingsService.CurrentSettings.RootRecordingsFolder,
            Cameras = new List<SessionCameraInfo>()
        };

        await WriteMetadataAsync(initialMetadata);
    }

    public async Task UpdateAsync(SessionInfo sessionInfo, IEnumerable<CameraSlotModel> slots)
    {
        if (_metadataFile is null)
        {
            await InitializeAsync(sessionInfo);
        }

        var metadata = new SessionMetadata
        {
            SessionFolderName = sessionInfo.SessionFolderName,
            CreatedAt = sessionInfo.CreatedAt,
            CameraCount = sessionInfo.CameraCount,
            AppVersion = "1.0",
            RootFolderName = _settingsService.CurrentSettings.RootRecordingsFolder,
            Cameras = slots
                .Select(slot => new SessionCameraInfo
                {
                    SlotNumber = slot.SlotNumber,
                    Comment = slot.Comment,
                    Status = slot.Status,
                    IsPreviewActive = slot.IsPreviewActive,
                    IsRecording = slot.IsRecording,
                    SelectedCameraDisplayName = slot.SelectedCamera?.DisplayName,
                    SelectedCameraDeviceId = slot.SelectedCamera?.DeviceId
                })
                .OrderBy(slot => slot.SlotNumber)
                .ToList()
        };

        await WriteMetadataAsync(metadata);
    }

    private async Task WriteMetadataAsync(SessionMetadata metadata)
    {
        if (_metadataFile is null)
        {
            return;
        }

        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await _fileLock.WaitAsync();

        try
        {
            await FileIO.WriteTextAsync(_metadataFile, json);
        }
        finally
        {
            _fileLock.Release();
        }
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
                // Если токен больше невалиден, используем папку Видео.
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
}
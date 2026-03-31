using System.Text.Json;
using TournamentRecorder.Models;
using TournamentRecorder.Services.Interfaces;
using Windows.Storage;

namespace TournamentRecorder.Services;

public class SettingsService : ISettingsService
{
    private const string AppFolderName = "TournamentRecorder";
    private const string SettingsFileName = "settings.json";

    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public AppSettings CurrentSettings { get; private set; } = new();

    public async Task InitializeAsync()
    {
        var file = await GetSettingsFileAsync();

        try
        {
            var json = await FileIO.ReadTextAsync(file);

            if (!string.IsNullOrWhiteSpace(json))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings is not null)
                {
                    CurrentSettings = settings;
                    NormalizeSettings();
                    return;
                }
            }
        }
        catch
        {
            // Если файл битый, просто откатываемся к дефолту.
        }

        CurrentSettings = new AppSettings();
        NormalizeSettings();
        await SaveAsync();
    }

    public async Task SaveAsync()
    {
        var file = await GetSettingsFileAsync();

        var json = JsonSerializer.Serialize(CurrentSettings, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await _fileLock.WaitAsync();

        try
        {
            await FileIO.WriteTextAsync(file, json);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task UpdateAsync(AppSettings settings)
    {
        CurrentSettings = settings ?? new AppSettings();
        NormalizeSettings();
        await SaveAsync();
    }
     
    private void NormalizeSettings()
    {
        if (string.IsNullOrWhiteSpace(CurrentSettings.RootRecordingsFolder))
        {
            CurrentSettings.RootRecordingsFolder = "турнир_записи";
        }

        if (CurrentSettings.SegmentDurationMinutes <= 0)
        {
            CurrentSettings.SegmentDurationMinutes = 5;
        }

        if (CurrentSettings.SegmentDurationMinutes > 180)
        {
            CurrentSettings.SegmentDurationMinutes = 180;
        }

        if (string.IsNullOrWhiteSpace(CurrentSettings.DefaultTournamentName))
        {
            CurrentSettings.DefaultTournamentName = "турнир";
        }

        if (CurrentSettings.DefaultWindowWidth < 800)
        {
            CurrentSettings.DefaultWindowWidth = 1400;
        }

        if (CurrentSettings.DefaultWindowHeight < 600)
        {
            CurrentSettings.DefaultWindowHeight = 900;
        }
    }

    private static async Task<StorageFile> GetSettingsFileAsync()
    {
        var localFolder = ApplicationData.Current.LocalFolder;
        var appFolder = await localFolder.CreateFolderAsync(AppFolderName, CreationCollisionOption.OpenIfExists);
        return await appFolder.CreateFileAsync(SettingsFileName, CreationCollisionOption.OpenIfExists);
    }
}
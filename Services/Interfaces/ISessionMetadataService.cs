using TournamentRecorder.Models;

namespace TournamentRecorder.Services.Interfaces;

public interface ISessionMetadataService
{
    Task InitializeAsync(SessionInfo sessionInfo);

    Task UpdateAsync(SessionInfo sessionInfo, IEnumerable<CameraSlotModel> slots);

    string? CurrentMetadataFilePath { get; }
}
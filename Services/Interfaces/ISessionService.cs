using TournamentRecorder.Models;

namespace TournamentRecorder.Services.Interfaces;

public interface ISessionService
{
    SessionInfo CurrentSession { get; }

    void CreateSession(int cameraCount);
}
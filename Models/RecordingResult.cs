namespace TournamentRecorder.Models;

public class RecordingResult
{
    public bool IsSuccess { get; init; }

    public string Message { get; init; } = string.Empty;

    public string? FilePath { get; init; }

    public static RecordingResult Success(string message = "", string? filePath = null)
    {
        return new RecordingResult
        {
            IsSuccess = true,
            Message = message,
            FilePath = filePath
        };
    }

    public static RecordingResult Failure(string message)
    {
        return new RecordingResult
        {
            IsSuccess = false,
            Message = message
        };
    }
}
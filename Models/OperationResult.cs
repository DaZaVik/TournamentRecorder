namespace TournamentRecorder.Models;

public class OperationResult
{
    public bool IsSuccess { get; init; }

    public string Message { get; init; } = string.Empty;

    public static OperationResult Success(string message = "")
    {
        return new OperationResult
        {
            IsSuccess = true,
            Message = message
        };
    }

    public static OperationResult Failure(string message)
    {
        return new OperationResult
        {
            IsSuccess = false,
            Message = message
        };
    }
}
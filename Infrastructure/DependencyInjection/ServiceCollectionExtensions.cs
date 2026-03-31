using Microsoft.Extensions.DependencyInjection;
using TournamentRecorder.Services;
using TournamentRecorder.Services.Interfaces;

namespace TournamentRecorder.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTournamentRecorderServices(this IServiceCollection services)
    {
        services.AddSingleton<MainWindow>();

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ILoggingService, LoggingService>();
        services.AddSingleton<ISessionService, SessionService>();
        services.AddSingleton<ISessionMetadataService, SessionMetadataService>();
        services.AddSingleton<ICameraDiscoveryService, CameraDiscoveryService>();
        services.AddSingleton<ICameraCheckService, CameraCheckService>();
        services.AddSingleton<ICameraPreviewService, CameraPreviewService>();

        return services;
    }
}
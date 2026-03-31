using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using TournamentRecorder.Infrastructure.DependencyInjection;
using TournamentRecorder.Services.Interfaces;

namespace TournamentRecorder;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    private Window? _mainWindow;

    public App()
    {
        InitializeComponent();

        Services = ConfigureServices();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        base.OnLaunched(args);

        var settingsService = Services.GetRequiredService<ISettingsService>();
        await settingsService.InitializeAsync();

        _mainWindow = Services.GetRequiredService<MainWindow>();
        _mainWindow.Activate();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddTournamentRecorderServices();

        return services.BuildServiceProvider();
    }
}
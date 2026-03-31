using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TournamentRecorder.Services.Interfaces;

namespace TournamentRecorder.Views;

public sealed partial class StartPage : Page
{
    private readonly ISessionService _sessionService;

    public StartPage()
    {
        InitializeComponent();
        _sessionService = App.Services.GetRequiredService<ISessionService>();
    }

    private void StartSessionButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedCount = 1;

        if (CameraCountComboBox.SelectedItem is ComboBoxItem selectedItem &&
            int.TryParse(selectedItem.Tag?.ToString(), out var parsedCount))
        {
            selectedCount = parsedCount;
        }

        _sessionService.CreateSession(selectedCount);
        Frame.Navigate(typeof(MainPage));
    }
}
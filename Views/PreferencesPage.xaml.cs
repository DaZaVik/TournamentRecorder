using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TournamentRecorder.Models;
using TournamentRecorder.Services.Interfaces;

namespace TournamentRecorder.Views;

public sealed partial class PreferencesPage : Page
{
    private readonly ISettingsService _settingsService;

    public PreferencesPage()
    {
        InitializeComponent();
        _settingsService = App.Services.GetRequiredService<ISettingsService>();

        Loaded += PreferencesPage_Loaded;
    }

    private void PreferencesPage_Loaded(object sender, RoutedEventArgs e)
    {
        var settings = _settingsService.CurrentSettings;

        RootFolderTextBox.Text = string.IsNullOrWhiteSpace(settings.RootRecordingsFolder)
            ? "турнир_записи"
            : settings.RootRecordingsFolder;

        TournamentNameTextBox.Text = string.IsNullOrWhiteSpace(settings.DefaultTournamentName)
            ? "турнир"
            : settings.DefaultTournamentName;

        SegmentDurationNumberBox.Value = settings.SegmentDurationMinutes <= 0
            ? 5
            : settings.SegmentDurationMinutes;

        UseFixedWindowSizeCheckBox.IsChecked = settings.UseFixedWindowSize;
        UseAutoWindowSizingCheckBox.IsChecked = settings.UseAutoWindowSizing;

        WindowWidthNumberBox.Value = settings.DefaultWindowWidth < 800
            ? 1400
            : settings.DefaultWindowWidth;

        WindowHeightNumberBox.Value = settings.DefaultWindowHeight < 600
            ? 900
            : settings.DefaultWindowHeight;

        UiScalePercentNumberBox.Value = settings.UiScalePercent < 75
            ? 100
            : settings.UiScalePercent;

        var savedTheme = Windows.Storage.ApplicationData.Current.LocalSettings.Values["UiThemeMode"]?.ToString() ?? "Primary";

        foreach (var item in ThemeComboBox.Items)
        {
            if (item is ComboBoxItem comboBoxItem &&
                comboBoxItem.Tag?.ToString() == savedTheme)
            {
                ThemeComboBox.SelectedItem = comboBoxItem;
                break;
            }
        }
    }

    private string GetSelectedThemeKey()
    {
        if (ThemeComboBox.SelectedItem is ComboBoxItem selectedItem &&
            selectedItem.Tag is not null)
        {
            return selectedItem.Tag.ToString() ?? "Primary";
        }

        return "Primary";
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var newSettings = new AppSettings
        {
            RootRecordingsFolder = string.IsNullOrWhiteSpace(RootFolderTextBox.Text)
                ? "турнир_записи"
                : RootFolderTextBox.Text.Trim(),

            DefaultTournamentName = string.IsNullOrWhiteSpace(TournamentNameTextBox.Text)
                ? "турнир"
                : TournamentNameTextBox.Text.Trim(),

            SegmentDurationMinutes = Math.Clamp((int)SegmentDurationNumberBox.Value, 1, 60),

            UseFixedWindowSize = UseFixedWindowSizeCheckBox.IsChecked ?? false,
            UseAutoWindowSizing = UseAutoWindowSizingCheckBox.IsChecked ?? true,

            DefaultWindowWidth = Math.Max(800, (int)WindowWidthNumberBox.Value),
            DefaultWindowHeight = Math.Max(600, (int)WindowHeightNumberBox.Value),

            UiScalePercent = Math.Clamp((int)UiScalePercentNumberBox.Value, 85, 110)
        };

        await _settingsService.UpdateAsync(newSettings);

        var selectedTheme = GetSelectedThemeKey();
        MainWindow.Instance?.ApplyVisualTheme(selectedTheme, persist: true);
        MainWindow.Instance?.ApplyUiScale();

        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }
}
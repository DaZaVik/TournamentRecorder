using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TournamentRecorder.Services.Interfaces;
using WinRT.Interop;
using Windows.Storage;

namespace TournamentRecorder;

public sealed partial class MainWindow : Window
{
    public static MainWindow? Instance { get; private set; }

    private readonly ISettingsService _settingsService;

    public Frame RootFrame => RootFrameControl;

    public MainWindow()
    {
        InitializeComponent();

        Instance = this;
        _settingsService = App.Services.GetRequiredService<ISettingsService>();

        ConfigureBackdrop();
        ConfigureTitleBar();
        ConfigureWindow();
        ApplySavedTheme();
        ApplyUiScale();

        RootFrame.Navigate(typeof(Views.StartPage));
    }

    public void ApplyVisualTheme(string themeKey, bool persist = true)
    {
        WindowRoot.RequestedTheme = themeKey switch
        {
            "Light" => ElementTheme.Light,
            "Black" => ElementTheme.Dark,
            _ => ElementTheme.Dark
        };

        ApplyPalette(themeKey);

        if (persist)
        {
            ApplicationData.Current.LocalSettings.Values["UiThemeMode"] = themeKey;
        }
    }

    public void ApplyUiScale()
    {
        var settings = _settingsService.CurrentSettings;

        // Кастомный масштаб не должен ломать интерфейс.
        // Всё, что выше 110%, уже лучше решать системным масштабом Windows.
        var scalePercent = settings.UiScalePercent;
        if (scalePercent < 85) scalePercent = 85;
        if (scalePercent > 110) scalePercent = 110;

        var scale = scalePercent / 100.0;

        RootFrameControl.RenderTransformOrigin = new Windows.Foundation.Point(0, 0);
        RootFrameControl.RenderTransform = new ScaleTransform
        {
            ScaleX = scale,
            ScaleY = scale
        };

        // Если пользователь НЕ включил авто-подбор размера окна,
        // подстраиваем размер под масштаб, чтобы верхняя панель не обрезалась.
        if (!settings.UseAutoWindowSizing)
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            if (appWindow is not null)
            {
                var baseWidth = settings.DefaultWindowWidth < 1000 ? 1400 : settings.DefaultWindowWidth;
                var baseHeight = settings.DefaultWindowHeight < 700 ? 900 : settings.DefaultWindowHeight;

                var scaledWidth = (int)(baseWidth * scale);
                var scaledHeight = (int)(baseHeight * scale);

                appWindow.Resize(new Windows.Graphics.SizeInt32(scaledWidth, scaledHeight));
            }
        }
    }

    private void ApplySavedTheme()
    {
        var savedTheme = ApplicationData.Current.LocalSettings.Values["UiThemeMode"]?.ToString() ?? "Primary";
        ApplyVisualTheme(savedTheme, persist: false);
    }

    private static void SetBrush(string key, byte a, byte r, byte g, byte b)
    {
        Application.Current.Resources[key] =
            new SolidColorBrush(Windows.UI.Color.FromArgb(a, r, g, b));
    }

    private void ApplyPalette(string themeKey)
    {
        switch (themeKey)
        {
            case "Black":
                SetBrush("AppPageBackgroundBrush", 0, 0, 0, 0);
                SetBrush("AppCardBrush", 212, 20, 20, 22);
                SetBrush("AppCardSecondaryBrush", 180, 34, 34, 38);
                SetBrush("AppPanelBrush", 150, 18, 18, 20);
                SetBrush("AppBorderBrush", 42, 255, 255, 255);

                SetBrush("AppAccentBrush", 255, 110, 88, 150);
                SetBrush("AppAccentSecondaryBrush", 255, 150, 128, 190);
                SetBrush("AppAccentForegroundBrush", 255, 255, 255, 255);

                SetBrush("AppMutedTextBrush", 255, 186, 186, 190);
                SetBrush("AppBannerBrush", 190, 36, 36, 40);
                SetBrush("AppStatusChipBrush", 24, 255, 255, 255);
                break;

            case "Light":
                SetBrush("AppPageBackgroundBrush", 0, 0, 0, 0);
                SetBrush("AppCardBrush", 225, 250, 246, 255);
                SetBrush("AppCardSecondaryBrush", 210, 241, 235, 249);
                SetBrush("AppPanelBrush", 195, 235, 228, 247);
                SetBrush("AppBorderBrush", 60, 140, 110, 180);

                SetBrush("AppAccentBrush", 255, 173, 118, 199);
                SetBrush("AppAccentSecondaryBrush", 255, 224, 162, 204);
                SetBrush("AppAccentForegroundBrush", 255, 255, 255, 255);

                SetBrush("AppMutedTextBrush", 255, 96, 84, 120);
                SetBrush("AppBannerBrush", 220, 244, 236, 255);
                SetBrush("AppStatusChipBrush", 24, 90, 64, 128);
                break;

            default:
                SetBrush("AppPageBackgroundBrush", 0, 0, 0, 0);
                SetBrush("AppCardBrush", 212, 36, 26, 54);
                SetBrush("AppCardSecondaryBrush", 178, 45, 32, 70);
                SetBrush("AppPanelBrush", 145, 27, 20, 43);
                SetBrush("AppBorderBrush", 48, 244, 192, 255);

                SetBrush("AppAccentBrush", 255, 152, 94, 186);
                SetBrush("AppAccentSecondaryBrush", 255, 214, 145, 197);
                SetBrush("AppAccentForegroundBrush", 255, 255, 255, 255);

                SetBrush("AppMutedTextBrush", 255, 214, 203, 232);
                SetBrush("AppBannerBrush", 190, 41, 32, 62);
                SetBrush("AppStatusChipBrush", 28, 255, 255, 255);
                break;
        }
    }

    private void ConfigureBackdrop()
    {
        try
        {
            SystemBackdrop = new MicaBackdrop();
        }
        catch
        {
            // Если Mica недоступна, окно всё равно должно запускаться.
        }
    }

    private void ConfigureTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        if (appWindow is null)
        {
            return;
        }

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var titleBar = appWindow.TitleBar;

            titleBar.BackgroundColor = Colors.Transparent;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.InactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

            titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(30, 255, 255, 255);
            titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(45, 255, 255, 255);
        }

        SetWindowIcon(appWindow);
        UpdateWindowStateText();
    }

    private void ConfigureWindow()
    {
        var settings = _settingsService.CurrentSettings;

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        if (appWindow is null)
        {
            return;
        }

        var width = settings.DefaultWindowWidth < 800 ? 1400 : settings.DefaultWindowWidth;
        var height = settings.DefaultWindowHeight < 600 ? 900 : settings.DefaultWindowHeight;

        if (settings.UseAutoWindowSizing)
        {
            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;

            width = (int)(workArea.Width * 0.82);
            height = (int)(workArea.Height * 0.85);

            if (width < 1000) width = 1000;
            if (height < 700) height = 700;
        }

        appWindow.Resize(new Windows.Graphics.SizeInt32(width, height));

        if (settings.UseFixedWindowSize && appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
        }
    }

    private void UpdateWindowStateText()
    {
        WindowStateTextBlock.Text = "Готово к работе";
    }

    private static void SetWindowIcon(AppWindow appWindow)
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(iconPath))
            {
                appWindow.SetIcon(iconPath);
            }
        }
        catch
        {
            // не роняем окно из-за иконки
        }
    }
}
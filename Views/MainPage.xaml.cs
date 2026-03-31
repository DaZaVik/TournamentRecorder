using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using TournamentRecorder.Models;

using TournamentRecorder.Services.Interfaces;
using Windows.UI.ApplicationSettings;

namespace TournamentRecorder.Views;

public sealed partial class MainPage : Page
{
    private const int MaxRecoveryAttempts = 3;

    private readonly ISessionService _sessionService;
    private readonly ICameraDiscoveryService _cameraDiscoveryService;
    private readonly ICameraCheckService _cameraCheckService;
    private readonly ICameraPreviewService _cameraPreviewService;
    private readonly ILoggingService _loggingService;
    private readonly ISessionMetadataService _sessionMetadataService;

    private readonly Dictionary<int, CancellationTokenSource> _selectionDebounce = new();
    private readonly Dictionary<int, MediaPlayerElement> _previewElements = new();
    private readonly Dictionary<int, CancellationTokenSource> _recoveryCts = new();

    private readonly DispatcherTimer _segmentUiTimer;
    private readonly DispatcherTimer _healthCheckTimer;

    public ObservableCollection<CameraSlotModel> CameraSlots { get; } = new();

    public MainPage()
    {
        InitializeComponent();

        _sessionService = App.Services.GetRequiredService<ISessionService>();
        _cameraDiscoveryService = App.Services.GetRequiredService<ICameraDiscoveryService>();
        _cameraCheckService = App.Services.GetRequiredService<ICameraCheckService>();
        _cameraPreviewService = App.Services.GetRequiredService<ICameraPreviewService>();
        _loggingService = App.Services.GetRequiredService<ILoggingService>();
        _sessionMetadataService = App.Services.GetRequiredService<ISessionMetadataService>();

        _cameraPreviewService.CameraFaulted += CameraPreviewService_CameraFaulted;

        Loaded += MainPage_Loaded;
        Unloaded += MainPage_Unloaded;

        _segmentUiTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _segmentUiTimer.Tick += SegmentUiTimer_Tick;

        _healthCheckTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _healthCheckTimer.Tick += HealthCheckTimer_Tick;

        CameraSlotsItemsControl.ItemsSource = CameraSlots;
    }

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        await InitializePageAsync();
        _segmentUiTimer.Start();
        _healthCheckTimer.Start();

        await _loggingService.LogInfoAsync(
            "MainPage",
            "Открыто рабочее окно");
    }

    private async void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _segmentUiTimer.Stop();
        _healthCheckTimer.Stop();

        _cameraPreviewService.CameraFaulted -= CameraPreviewService_CameraFaulted;

        foreach (var debounce in _selectionDebounce.Values)
        {
            try
            {
                debounce.Cancel();
                debounce.Dispose();
            }
            catch
            {
            }
        }

        _selectionDebounce.Clear();

        foreach (var recovery in _recoveryCts.Values)
        {
            try
            {
                recovery.Cancel();
                recovery.Dispose();
            }
            catch
            {
            }
        }

        _recoveryCts.Clear();

        await _cameraPreviewService.StopAllPreviewsAsync();

        await PersistSessionMetadataAsync();

        await _loggingService.LogInfoAsync(
            "MainPage",
            "Рабочее окно закрыто");
    }

    private async Task InitializePageAsync()
    {
        UpdateSessionHeader();

        var availableCameras = await _cameraDiscoveryService.GetAvailableCamerasAsync();

        BuildCameraSlots(availableCameras);
        RefreshAllUiStates();
        UpdateSystemStateBanner();

        await PersistSessionMetadataAsync();

        await _loggingService.LogInfoAsync(
            "MainPage",
            $"Рабочее окно инициализировано. Слотов камер: {_sessionService.CurrentSession.CameraCount}");
    }

    private void BuildCameraSlots(IReadOnlyList<CameraDeviceInfo> availableCameras)
    {
        CameraSlots.Clear();

        var session = _sessionService.CurrentSession;

        for (var i = 1; i <= session.CameraCount; i++)
        {
            var slot = new CameraSlotModel
            {
                SlotNumber = i,
                Status = "Камера не выбрана",
                SegmentTimerText = "00:00",
                Comment = string.Empty,
                IsPreviewActive = false,
                IsRecording = false,
                CanRecord = false,
                CanSave = false,
                CanStop = false,
                CanRecover = false
            };

            foreach (var camera in availableCameras)
            {
                slot.AvailableCameras.Add(new CameraDeviceInfo
                {
                    DeviceId = camera.DeviceId,
                    DisplayName = camera.DisplayName
                });
            }

            CameraSlots.Add(slot);
        }
    }

    private void UpdateSessionHeader()
    {
        var session = _sessionService.CurrentSession;
        SessionInfoTextBlock.Text =
            $"Выбрано камер: {session.CameraCount}. Время создания сессии: {session.CreatedAt:G}";
    }

    private async void CameraSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox)
        {
            return;
        }

        if (comboBox.Tag is not int slotNumber)
        {
            return;
        }

        var currentSlot = CameraSlots.FirstOrDefault(slot => slot.SlotNumber == slotNumber);
        if (currentSlot is null)
        {
            return;
        }

        if (_cameraPreviewService.IsRecovering(slotNumber))
        {
            return;
        }

        if (_selectionDebounce.TryGetValue(slotNumber, out var previousDebounce))
        {
            try
            {
                previousDebounce.Cancel();
                previousDebounce.Dispose();
            }
            catch
            {
            }
        }

        var debounceCts = new CancellationTokenSource();
        _selectionDebounce[slotNumber] = debounceCts;
        var token = debounceCts.Token;

        if (comboBox.SelectedItem is not CameraDeviceInfo selectedCamera)
        {
            currentSlot.Status = "Камера не выбрана";
            currentSlot.IsPreviewActive = false;
            currentSlot.IsRecording = false;
            currentSlot.SegmentTimerText = "00:00";

            await _cameraPreviewService.StopPreviewAsync(slotNumber);

            UpdateSlotActionState(currentSlot);
            UpdateGlobalActionState();
            UpdateSystemStateBanner();

            await PersistSessionMetadataAsync();

            await _loggingService.LogInfoAsync(
                "MainPage",
                "Камера снята со слота",
                slotNumber);

            return;
        }

        var duplicateSlot = CameraSlots.FirstOrDefault(slot =>
            slot.SlotNumber != slotNumber &&
            slot.SelectedCamera is not null &&
            slot.SelectedCamera.DeviceId == selectedCamera.DeviceId);

        if (duplicateSlot is not null)
        {
            currentSlot.SelectedCamera = null;
            currentSlot.Status = "Камера не выбрана";
            currentSlot.IsPreviewActive = false;
            currentSlot.IsRecording = false;
            currentSlot.SegmentTimerText = "00:00";

            await _cameraPreviewService.StopPreviewAsync(slotNumber);

            UpdateSlotActionState(currentSlot);
            UpdateGlobalActionState();
            UpdateSystemStateBanner();

            await PersistSessionMetadataAsync();

            await _loggingService.LogWarningAsync(
                "MainPage",
                "Попытка выбрать одну и ту же камеру в два слота",
                slotNumber,
                selectedCamera.DisplayName,
                $"Дубликат со слотом {duplicateSlot.SlotNumber}");

            await ShowInfoDialogAsync(
                "Камера уже выбрана",
                $"Камера \"{selectedCamera.DisplayName}\" уже назначена в слот #{duplicateSlot.SlotNumber}. Выберите другое устройство.");

            return;
        }

        currentSlot.Status = "Камера выбрана";
        UpdateSlotActionState(currentSlot);
        UpdateGlobalActionState();
        UpdateSystemStateBanner();

        await _loggingService.LogInfoAsync(
            "MainPage",
            "Камера выбрана",
            slotNumber,
            selectedCamera.DisplayName);

        await PersistSessionMetadataAsync();

        try
        {
            await Task.Delay(300, token);

            if (token.IsCancellationRequested)
            {
                return;
            }

            await StartPreviewForSlotAsync(currentSlot, token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task StartPreviewForSlotAsync(CameraSlotModel slot, CancellationToken cancellationToken = default)
    {
        if (slot.SelectedCamera is null)
        {
            slot.Status = "Камера не выбрана";
            slot.IsPreviewActive = false;
            slot.IsRecording = false;
            slot.SegmentTimerText = "00:00";

            await _cameraPreviewService.StopPreviewAsync(slot.SlotNumber);

            UpdateSlotActionState(slot);
            UpdateGlobalActionState();
            UpdateSystemStateBanner();

            await PersistSessionMetadataAsync();
            return;
        }

        if (slot.IsRecording)
        {
            return;
        }

        if (!_previewElements.TryGetValue(slot.SlotNumber, out var previewElement))
        {
            slot.Status = "Элемент превью не готов";
            slot.IsPreviewActive = false;

            UpdateSlotActionState(slot);
            UpdateGlobalActionState();
            UpdateSystemStateBanner();

            await PersistSessionMetadataAsync();

            await _loggingService.LogWarningAsync(
                "MainPage",
                "Элемент превью ещё не готов",
                slot.SlotNumber,
                slot.SelectedCamera.DisplayName);

            return;
        }

        slot.Status = "Подготовка превью...";

        var result = await _cameraPreviewService.StartPreviewAsync(
            slot.SlotNumber,
            slot.SelectedCamera,
            previewElement,
            cancellationToken);

        slot.Status = result.Message;
        slot.IsPreviewActive = result.IsSuccess;
        slot.IsRecording = _cameraPreviewService.IsRecording(slot.SlotNumber);

        UpdateSlotActionState(slot);
        UpdateGlobalActionState();
        UpdateSystemStateBanner();

        await PersistSessionMetadataAsync();
    }

    private void PreviewElement_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MediaPlayerElement previewElement)
        {
            return;
        }

        if (previewElement.Tag is not int slotNumber)
        {
            return;
        }

        _previewElements[slotNumber] = previewElement;

        var slot = CameraSlots.FirstOrDefault(x => x.SlotNumber == slotNumber);
        if (slot?.SelectedCamera is not null)
        {
            _ = StartPreviewForSlotAsync(slot);
        }
    }

    private async void PreviewElement_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MediaPlayerElement previewElement)
        {
            return;
        }

        if (previewElement.Tag is not int slotNumber)
        {
            return;
        }

        _previewElements.Remove(slotNumber);
        await _cameraPreviewService.StopPreviewAsync(slotNumber);

        var slot = CameraSlots.FirstOrDefault(x => x.SlotNumber == slotNumber);
        if (slot is not null)
        {
            slot.IsPreviewActive = false;
            slot.IsRecording = false;
            slot.SegmentTimerText = "00:00";
            slot.Status = slot.SelectedCamera is null ? "Камера не выбрана" : "Превью остановлено";

            UpdateSlotActionState(slot);
            UpdateGlobalActionState();
            UpdateSystemStateBanner();
        }

        await PersistSessionMetadataAsync();

        await _loggingService.LogInfoAsync(
            "MainPage",
            "Элемент превью выгружен",
            slotNumber);
    }

    private async void CheckCamerasButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedSlots = CameraSlots
            .Where(slot => slot.SelectedCamera is not null)
            .ToList();

        if (selectedSlots.Count == 0)
        {
            await ShowInfoDialogAsync("Проверка камер", "Сначала выберите хотя бы одну камеру.");
            return;
        }

        await _loggingService.LogInfoAsync(
            "MainPage",
            "Запущена проверка камер");

        foreach (var slot in selectedSlots)
        {
            slot.Status = "Проверяется...";
        }

        foreach (var slot in selectedSlots)
        {
            var camera = slot.SelectedCamera;
            if (camera is null)
            {
                slot.Status = "Камера не выбрана";
                slot.IsPreviewActive = false;
                slot.IsRecording = false;
                UpdateSlotActionState(slot);
                continue;
            }

            var isPhysicallyAvailable = await _cameraDiscoveryService.IsCameraAvailableAsync(camera.DeviceId);

            if (!isPhysicallyAvailable)
            {
                _cameraPreviewService.ForceDeviceLost(slot.SlotNumber, "Устройство не найдено в системе");

                slot.Status = "Потеря связи";
                slot.IsPreviewActive = false;
                slot.IsRecording = false;
                slot.SegmentTimerText = "00:00";
                UpdateSlotActionState(slot);

                await _loggingService.LogWarningAsync(
                    "MainPage",
                    "Проверка камеры показала физическое отсутствие устройства",
                    slot.SlotNumber,
                    camera.DisplayName,
                    camera.DeviceId);

                continue;
            }

            if (_cameraPreviewService.IsRecovering(slot.SlotNumber))
            {
                slot.Status = "Попытка восстановления";
                slot.IsPreviewActive = false;
                slot.IsRecording = false;
                slot.SegmentTimerText = "00:00";
                UpdateSlotActionState(slot);
                continue;
            }

            if (_cameraPreviewService.IsDeviceLost(slot.SlotNumber))
            {
                var attempts = _cameraPreviewService.GetRecoveryAttempts(slot.SlotNumber);

                slot.Status = attempts >= MaxRecoveryAttempts
                    ? "Не удалось восстановить камеру"
                    : "Потеря связи";

                slot.IsPreviewActive = false;
                slot.IsRecording = false;
                slot.SegmentTimerText = "00:00";
                UpdateSlotActionState(slot);

                await _loggingService.LogWarningAsync(
                    "MainPage",
                    "Проверка камеры показала потерю связи",
                    slot.SlotNumber,
                    camera.DisplayName,
                    _cameraPreviewService.GetLastDeviceError(slot.SlotNumber));

                continue;
            }

            if (slot.IsPreviewActive)
            {
                slot.Status = "Связь есть";
                UpdateSlotActionState(slot);

                await _loggingService.LogInfoAsync(
                    "MainPage",
                    "Проверка камеры: связь есть",
                    slot.SlotNumber,
                    camera.DisplayName);

                continue;
            }

            var result = await _cameraCheckService.CheckCameraAsync(camera);
            slot.Status = result.StatusMessage;
            UpdateSlotActionState(slot);

            if (result.IsSuccess)
            {
                await _loggingService.LogInfoAsync(
                    "MainPage",
                    "Проверка камеры успешна",
                    slot.SlotNumber,
                    camera.DisplayName,
                    result.StatusMessage);
            }
            else
            {
                await _loggingService.LogWarningAsync(
                    "MainPage",
                    "Проверка камеры завершилась с ошибкой",
                    slot.SlotNumber,
                    camera.DisplayName,
                    result.StatusMessage);
            }
        }

        UpdateGlobalActionState();
        UpdateSystemStateBanner();
        await PersistSessionMetadataAsync();

        var successCount = selectedSlots.Count(slot => slot.Status == "Связь есть");
        var failedCount = selectedSlots.Count - successCount;

        await ShowInfoDialogAsync(
            "Проверка камер завершена",
            $"Проверено камер: {selectedSlots.Count}\nДоступны: {successCount}\nС ошибками: {failedCount}");
    }

    private async void RecoverProblematicButton_Click(object sender, RoutedEventArgs e)
    {
        var problematicSlots = CameraSlots
            .Where(slot =>
                slot.SelectedCamera is not null &&
                _cameraPreviewService.IsDeviceLost(slot.SlotNumber) &&
                !_cameraPreviewService.IsRecovering(slot.SlotNumber))
            .ToList();

        if (problematicSlots.Count == 0)
        {
            await ShowInfoDialogAsync("Восстановление", "Сейчас нет проблемных камер для восстановления.");
            return;
        }

        foreach (var slot in problematicSlots)
        {
            if (!_previewElements.TryGetValue(slot.SlotNumber, out var previewElement))
            {
                continue;
            }

            if (_recoveryCts.ContainsKey(slot.SlotNumber))
            {
                continue;
            }

            slot.Status = "Попытка восстановления";
            slot.IsPreviewActive = false;
            slot.IsRecording = false;
            slot.SegmentTimerText = "00:00";

            var cts = new CancellationTokenSource();
            _recoveryCts[slot.SlotNumber] = cts;

            _ = RunRecoveryAsync(slot, previewElement, cts.Token);
        }

        UpdateGlobalActionState();
        UpdateSystemStateBanner();
        await PersistSessionMetadataAsync();

        await _loggingService.LogWarningAsync(
            "MainPage",
            "Оператор запустил массовое восстановление проблемных камер",
            details: $"Количество слотов: {problematicSlots.Count}");
    }

    private async void RecordAllButton_Click(object sender, RoutedEventArgs e)
    {
        var sessionFolderName = _sessionService.CurrentSession.SessionFolderName;

        await _loggingService.LogInfoAsync(
            "MainPage",
            "Запущена массовая запись всех готовых камер");

        var tasks = CameraSlots
            .Where(slot => slot.SelectedCamera is not null &&
                           slot.IsPreviewActive &&
                           !slot.IsRecording &&
                           !_cameraPreviewService.IsDeviceLost(slot.SlotNumber) &&
                           !_cameraPreviewService.IsRecovering(slot.SlotNumber))
            .Select(async slot =>
            {
                var result = await _cameraPreviewService.StartRecordingAsync(
                    slot.SlotNumber,
                    slot.Comment,
                    sessionFolderName);

                slot.IsRecording = result.IsSuccess;
                slot.Status = result.Message;
                UpdateSlotActionState(slot);
            });

        await Task.WhenAll(tasks);
        UpdateGlobalActionState();
        UpdateSystemStateBanner();
        await PersistSessionMetadataAsync();
    }

    private async void SaveAllButton_Click(object sender, RoutedEventArgs e)
    {
        var sessionFolderName = _sessionService.CurrentSession.SessionFolderName;

        await _loggingService.LogInfoAsync(
            "MainPage",
            "Запущено массовое сохранение сегментов");

        var tasks = CameraSlots
            .Where(slot => _cameraPreviewService.IsRecording(slot.SlotNumber) &&
                           !_cameraPreviewService.IsDeviceLost(slot.SlotNumber) &&
                           !_cameraPreviewService.IsRecovering(slot.SlotNumber))
            .Select(async slot =>
            {
                var result = await _cameraPreviewService.SaveSegmentAsync(
                    slot.SlotNumber,
                    slot.Comment,
                    sessionFolderName);

                slot.Status = result.Message;
                slot.IsRecording = _cameraPreviewService.IsRecording(slot.SlotNumber);
                UpdateSlotActionState(slot);
            });

        await Task.WhenAll(tasks);
        UpdateGlobalActionState();
        UpdateSystemStateBanner();
        await PersistSessionMetadataAsync();
    }

    private async void StopAllButton_Click(object sender, RoutedEventArgs e)
    {
        await _loggingService.LogInfoAsync(
            "MainPage",
            "Запущена массовая остановка записи");

        var tasks = CameraSlots
            .Where(slot => _cameraPreviewService.IsRecording(slot.SlotNumber))
            .Select(async slot =>
            {
                var result = await _cameraPreviewService.StopRecordingAsync(slot.SlotNumber);

                if (result.IsSuccess)
                {
                    slot.IsRecording = false;
                    slot.SegmentTimerText = "00:00";
                }

                slot.Status = result.Message;
                UpdateSlotActionState(slot);
            });

        await Task.WhenAll(tasks);
        UpdateGlobalActionState();
        UpdateSystemStateBanner();
        await PersistSessionMetadataAsync();
    }

    private async void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not int slotNumber)
        {
            return;
        }

        var slot = CameraSlots.FirstOrDefault(x => x.SlotNumber == slotNumber);
        if (slot is null)
        {
            return;
        }

        if (slot.SelectedCamera is null)
        {
            await ShowInfoDialogAsync("Запись камеры", "Сначала выберите камеру.");
            return;
        }

        if (_cameraPreviewService.IsDeviceLost(slotNumber))
        {
            await ShowInfoDialogAsync("Запись камеры", "Камера потеряна. Восстановите подключение.");
            return;
        }

        if (_cameraPreviewService.IsRecovering(slotNumber))
        {
            await ShowInfoDialogAsync("Запись камеры", "Сейчас выполняется восстановление камеры.");
            return;
        }

        var sessionFolderName = _sessionService.CurrentSession.SessionFolderName;
        var result = await _cameraPreviewService.StartRecordingAsync(slotNumber, slot.Comment, sessionFolderName);

        slot.IsRecording = result.IsSuccess;
        slot.Status = result.Message;

        UpdateSlotActionState(slot);
        UpdateGlobalActionState();
        UpdateSystemStateBanner();
        await PersistSessionMetadataAsync();

        if (!result.IsSuccess)
        {
            await ShowInfoDialogAsync("Ошибка записи", result.Message);
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not int slotNumber)
        {
            return;
        }

        var slot = CameraSlots.FirstOrDefault(x => x.SlotNumber == slotNumber);
        if (slot is null)
        {
            return;
        }

        if (_cameraPreviewService.IsDeviceLost(slotNumber))
        {
            await ShowInfoDialogAsync("Ошибка сохранения сегмента", "Камера потеряна. Сегмент не может быть сохранён корректно.");
            return;
        }

        if (_cameraPreviewService.IsRecovering(slotNumber))
        {
            await ShowInfoDialogAsync("Ошибка сохранения сегмента", "Сейчас выполняется восстановление камеры.");
            return;
        }

        var sessionFolderName = _sessionService.CurrentSession.SessionFolderName;
        var result = await _cameraPreviewService.SaveSegmentAsync(slotNumber, slot.Comment, sessionFolderName);

        slot.Status = result.Message;
        slot.IsRecording = _cameraPreviewService.IsRecording(slot.SlotNumber);

        UpdateSlotActionState(slot);
        UpdateGlobalActionState();
        UpdateSystemStateBanner();
        await PersistSessionMetadataAsync();

        if (!result.IsSuccess)
        {
            await ShowInfoDialogAsync("Ошибка сохранения сегмента", result.Message);
        }
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not int slotNumber)
        {
            return;
        }

        var slot = CameraSlots.FirstOrDefault(x => x.SlotNumber == slotNumber);
        if (slot is null)
        {
            return;
        }

        var result = await _cameraPreviewService.StopRecordingAsync(slotNumber);

        if (result.IsSuccess)
        {
            slot.IsRecording = false;
            slot.SegmentTimerText = "00:00";
        }

        slot.Status = result.Message;

        UpdateSlotActionState(slot);
        UpdateGlobalActionState();
        UpdateSystemStateBanner();
        await PersistSessionMetadataAsync();

        if (!result.IsSuccess)
        {
            await ShowInfoDialogAsync("Ошибка остановки записи", result.Message);
        }
    }

    private async void RecoverButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not int slotNumber)
        {
            return;
        }

        var slot = CameraSlots.FirstOrDefault(x => x.SlotNumber == slotNumber);
        if (slot is null)
        {
            return;
        }

        if (slot.SelectedCamera is null)
        {
            return;
        }

        if (!_previewElements.TryGetValue(slotNumber, out var previewElement))
        {
            await ShowInfoDialogAsync("Восстановление камеры", "Элемент превью не готов.");
            return;
        }

        if (_cameraPreviewService.IsRecovering(slotNumber))
        {
            await ShowInfoDialogAsync("Восстановление камеры", "Восстановление уже выполняется.");
            return;
        }

        if (_recoveryCts.ContainsKey(slotNumber))
        {
            await ShowInfoDialogAsync("Восстановление камеры", "Pipeline восстановления уже запущен.");
            return;
        }

        slot.Status = "Попытка восстановления";
        slot.IsPreviewActive = false;
        slot.IsRecording = false;
        slot.SegmentTimerText = "00:00";

        UpdateSlotActionState(slot);
        UpdateGlobalActionState();
        UpdateSystemStateBanner();
        await PersistSessionMetadataAsync();

        await _loggingService.LogWarningAsync(
            "MainPage",
            "Оператор вручную запустил восстановление камеры",
            slotNumber,
            slot.SelectedCamera.DisplayName);

        var cts = new CancellationTokenSource();
        _recoveryCts[slotNumber] = cts;

        _ = RunRecoveryAsync(slot, previewElement, cts.Token);
    }

    private async void HealthCheckTimer_Tick(object? sender, object e)
    {
        foreach (var slot in CameraSlots)
        {
            if (slot.SelectedCamera is null)
            {
                continue;
            }

            if (!_previewElements.TryGetValue(slot.SlotNumber, out var previewElement))
            {
                continue;
            }

            var cameraStillExists = await _cameraDiscoveryService.IsCameraAvailableAsync(slot.SelectedCamera.DeviceId);

            if (!cameraStillExists)
            {
                _cameraPreviewService.ForceDeviceLost(slot.SlotNumber, "Устройство исчезло из списка камер");
            }

            if (_cameraPreviewService.IsRecovering(slot.SlotNumber))
            {
                slot.Status = "Попытка восстановления";
                slot.IsPreviewActive = false;
                slot.IsRecording = false;
                slot.SegmentTimerText = "00:00";
                UpdateSlotActionState(slot);
                continue;
            }

            if (!_cameraPreviewService.IsDeviceLost(slot.SlotNumber))
            {
                continue;
            }

            if (_cameraPreviewService.GetRecoveryAttempts(slot.SlotNumber) >= MaxRecoveryAttempts)
            {
                slot.Status = "Не удалось восстановить камеру";
                slot.IsPreviewActive = false;
                slot.IsRecording = false;
                slot.SegmentTimerText = "00:00";
                UpdateSlotActionState(slot);
                continue;
            }

            if (_recoveryCts.ContainsKey(slot.SlotNumber))
            {
                continue;
            }

            slot.Status = "Попытка восстановления";
            slot.IsPreviewActive = false;
            slot.IsRecording = false;
            slot.SegmentTimerText = "00:00";
            UpdateSlotActionState(slot);

            var cts = new CancellationTokenSource();
            _recoveryCts[slot.SlotNumber] = cts;

            _ = RunRecoveryAsync(slot, previewElement, cts.Token);
        }

        UpdateGlobalActionState();
        UpdateSystemStateBanner();
        await PersistSessionMetadataAsync();
    }

    private async Task RunRecoveryAsync(CameraSlotModel slot, MediaPlayerElement previewElement, CancellationToken token)
    {
        try
        {
            await _loggingService.LogWarningAsync(
                "MainPage",
                "Запущен pipeline восстановления камеры",
                slot.SlotNumber,
                slot.SelectedCamera?.DisplayName,
                $"Попытка #{_cameraPreviewService.GetRecoveryAttempts(slot.SlotNumber) + 1}");

            var previewResult = await _cameraPreviewService.TryRecoverCameraAsync(
                slot.SlotNumber,
                previewElement,
                token);

            if (!previewResult.IsSuccess)
            {
                if (_cameraPreviewService.GetRecoveryAttempts(slot.SlotNumber) >= MaxRecoveryAttempts)
                {
                    slot.Status = "Не удалось восстановить камеру";
                }
                else
                {
                    slot.Status = "Потеря связи";
                }

                slot.IsPreviewActive = false;
                slot.IsRecording = false;
                slot.SegmentTimerText = "00:00";

                UpdateSlotActionState(slot);
                UpdateGlobalActionState();
                UpdateSystemStateBanner();
                await PersistSessionMetadataAsync();

                return;
            }

            slot.Status = "Восстановлено";
            slot.IsPreviewActive = true;
            slot.IsRecording = false;
            UpdateSlotActionState(slot);
            UpdateGlobalActionState();
            UpdateSystemStateBanner();
            await PersistSessionMetadataAsync();

            var resumeResult = await _cameraPreviewService.TryResumeRecordingAfterRecoveryAsync(slot.SlotNumber, token);

            if (resumeResult is not null)
            {
                if (resumeResult.IsSuccess)
                {
                    slot.Status = "Идёт запись";
                    slot.IsRecording = true;
                }
                else
                {
                    slot.Status = $"Восстановлено, но запись не возобновлена: {resumeResult.Message}";
                    slot.IsRecording = false;
                }
            }
            else
            {
                slot.Status = "Превью активно";
                slot.IsRecording = false;
            }

            slot.IsPreviewActive = true;

            await _loggingService.LogInfoAsync(
                "MainPage",
                "Восстановление камеры завершено",
                slot.SlotNumber,
                slot.SelectedCamera?.DisplayName,
                slot.Status);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (_recoveryCts.TryGetValue(slot.SlotNumber, out var cts))
            {
                try
                {
                    cts.Dispose();
                }
                catch
                {
                }

                _recoveryCts.Remove(slot.SlotNumber);
            }

            UpdateSlotActionState(slot);
            UpdateGlobalActionState();
            UpdateSystemStateBanner();
            await PersistSessionMetadataAsync();
        }
    }

    private void SegmentUiTimer_Tick(object? sender, object e)
    {
        foreach (var slot in CameraSlots)
        {
            if (_cameraPreviewService.IsRecovering(slot.SlotNumber))
            {
                slot.Status = "Попытка восстановления";
                slot.IsPreviewActive = false;
                slot.IsRecording = false;
                slot.SegmentTimerText = "00:00";
                UpdateSlotActionState(slot);
                continue;
            }

            if (_cameraPreviewService.IsDeviceLost(slot.SlotNumber))
            {
                slot.Status = _cameraPreviewService.GetRecoveryAttempts(slot.SlotNumber) >= MaxRecoveryAttempts
                    ? "Не удалось восстановить камеру"
                    : "Потеря связи";

                slot.IsPreviewActive = false;
                slot.IsRecording = false;
                slot.SegmentTimerText = "00:00";
                UpdateSlotActionState(slot);
                continue;
            }

            var startedAt = _cameraPreviewService.GetCurrentSegmentStartedAt(slot.SlotNumber);

            if (slot.IsRecording && startedAt.HasValue)
            {
                var elapsed = DateTimeOffset.Now - startedAt.Value;
                if (elapsed < TimeSpan.Zero)
                {
                    elapsed = TimeSpan.Zero;
                }

                slot.SegmentTimerText = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
            }
            else
            {
                slot.SegmentTimerText = "00:00";
            }

            slot.IsRecording = _cameraPreviewService.IsRecording(slot.SlotNumber);
            UpdateSlotActionState(slot);
        }

        UpdateGlobalActionState();
        UpdateSystemStateBanner();
    }

    private void UpdateSlotActionState(CameraSlotModel slot)
    {
        var isDeviceLost = _cameraPreviewService.IsDeviceLost(slot.SlotNumber);
        var isRecovering = _cameraPreviewService.IsRecovering(slot.SlotNumber);

        slot.CanRecord = slot.SelectedCamera is not null &&
                         slot.IsPreviewActive &&
                         !slot.IsRecording &&
                         !isDeviceLost &&
                         !isRecovering;

        slot.CanSave = slot.IsRecording && !isDeviceLost && !isRecovering;
        slot.CanStop = slot.IsRecording;
        slot.CanRecover = slot.SelectedCamera is not null && isDeviceLost && !isRecovering;
    }

    private void UpdateGlobalActionState()
    {
        var hasReadyToRecord = CameraSlots.Any(slot =>
            slot.SelectedCamera is not null &&
            slot.IsPreviewActive &&
            !slot.IsRecording &&
            !_cameraPreviewService.IsDeviceLost(slot.SlotNumber) &&
            !_cameraPreviewService.IsRecovering(slot.SlotNumber));

        var hasAnyRecording = CameraSlots.Any(slot => slot.IsRecording);

        var hasProblematicSlots = CameraSlots.Any(slot =>
            slot.SelectedCamera is not null &&
            _cameraPreviewService.IsDeviceLost(slot.SlotNumber) &&
            !_cameraPreviewService.IsRecovering(slot.SlotNumber));

        RecordAllButton.IsEnabled = hasReadyToRecord;
        SaveAllButton.IsEnabled = hasAnyRecording;
        StopAllButton.IsEnabled = hasAnyRecording;
        RecoverProblematicButton.IsEnabled = hasProblematicSlots;
    }

    private void UpdateSystemStateBanner()
    {
        var problematicCount = CameraSlots.Count(slot =>
            slot.SelectedCamera is not null &&
            _cameraPreviewService.IsDeviceLost(slot.SlotNumber));

        var recoveringCount = CameraSlots.Count(slot =>
            slot.SelectedCamera is not null &&
            _cameraPreviewService.IsRecovering(slot.SlotNumber));

        var recordingCount = CameraSlots.Count(slot =>
            _cameraPreviewService.IsRecording(slot.SlotNumber));

        ProblematicCountTextBlock.Text = $"Проблемных камер: {problematicCount}";
        RecoveringCountTextBlock.Text = $"Восстанавливаются: {recoveringCount}";
        RecordingCountTextBlock.Text = $"Идёт запись: {recordingCount}";

        if (problematicCount > 0 || recoveringCount > 0)
        {
            SystemStateBanner.Visibility = Visibility.Visible;

            if (recoveringCount > 0)
            {
                SystemStateTitleTextBlock.Text = "Обнаружены проблемы с камерами. Идёт восстановление.";
            }
            else
            {
                SystemStateTitleTextBlock.Text = "Обнаружены проблемы с камерами.";
            }
        }
        else
        {
            SystemStateBanner.Visibility = Visibility.Visible;
            SystemStateTitleTextBlock.Text = "Система работает штатно.";
        }
    }

    private void RefreshAllUiStates()
    {
        foreach (var slot in CameraSlots)
        {
            UpdateSlotActionState(slot);
        }

        UpdateGlobalActionState();
        UpdateSystemStateBanner();
    }

    private async Task PersistSessionMetadataAsync()
    {
        await _sessionMetadataService.UpdateAsync(_sessionService.CurrentSession, CameraSlots);
    }

    private async Task ShowInfoDialogAsync(string title, string content)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            CloseButtonText = "OK",
            XamlRoot = this.XamlRoot
        };

        await dialog.ShowAsync();
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Frame.Navigate(typeof(PreferencesPage));
        }
        catch (Exception ex)
        {
            await ShowInfoDialogAsync(
                "Ошибка открытия настроек",
                ex.ToString());
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    private void CameraPreviewService_CameraFaulted(object? sender, CameraFaultedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            var slot = CameraSlots.FirstOrDefault(x => x.SlotNumber == e.SlotNumber);
            if (slot is null)
            {
                return;
            }

            slot.Status = "Потеря связи";
            slot.IsPreviewActive = false;
            slot.IsRecording = false;
            slot.SegmentTimerText = "00:00";

            UpdateSlotActionState(slot);
            UpdateGlobalActionState();
            UpdateSystemStateBanner();

            await PersistSessionMetadataAsync();

            if (e.WasRecording)
            {
                await ShowInfoDialogAsync(
                    "Потеря связи с камерой",
                    $"Камера \"{e.CameraDisplayName}\" была потеряна во время записи.\n\nОшибка: {e.ErrorMessage}");
            }
        });
    }
}
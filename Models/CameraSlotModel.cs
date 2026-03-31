using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace TournamentRecorder.Models;

public partial class CameraSlotModel : ObservableObject
{
    [ObservableProperty]
    private int slotNumber;

    [ObservableProperty]
    private string comment = string.Empty;

    [ObservableProperty]
    private string status = "Камера не выбрана";

    [ObservableProperty]
    private bool isRecording;

    [ObservableProperty]
    private bool isPreviewActive;

    [ObservableProperty]
    private string segmentTimerText = "00:00";

    [ObservableProperty]
    private CameraDeviceInfo? selectedCamera;

    [ObservableProperty]
    private bool canRecord;

    [ObservableProperty]
    private bool canSave;

    [ObservableProperty]
    private bool canStop;

    [ObservableProperty]
    private bool canRecover;

    public string DisplayTitle => $"Камера {SlotNumber}";

    public ObservableCollection<CameraDeviceInfo> AvailableCameras { get; set; } = new();
}
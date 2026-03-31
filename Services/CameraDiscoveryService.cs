using TournamentRecorder.Models;
using TournamentRecorder.Services.Interfaces;
using Windows.Devices.Enumeration;

namespace TournamentRecorder.Services;

public class CameraDiscoveryService : ICameraDiscoveryService
{
    public async Task<IReadOnlyList<CameraDeviceInfo>> GetAvailableCamerasAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture).AsTask(cancellationToken);

        var grouped = devices
            .GroupBy(d => d.Name)
            .ToList();

        var result = new List<CameraDeviceInfo>();

        foreach (var group in grouped)
        {
            if (group.Count() == 1)
            {
                var device = group.First();

                result.Add(new CameraDeviceInfo
                {
                    DeviceId = device.Id,
                    DisplayName = device.Name
                });
            }
            else
            {
                var index = 1;

                foreach (var device in group)
                {
                    result.Add(new CameraDeviceInfo
                    {
                        DeviceId = device.Id,
                        DisplayName = $"{device.Name} #{index}"
                    });

                    index++;
                }
            }
        }

        return result;
    }

    public async Task<bool> IsCameraAvailableAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture).AsTask(cancellationToken);

        return devices.Any(device => string.Equals(device.Id, deviceId, StringComparison.OrdinalIgnoreCase));
    }
}
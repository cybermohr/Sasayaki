using NAudio.CoreAudioApi;
using Sasayaki.Core;

namespace Sasayaki.App.Platform;

/// <summary>Owns temporary muting of the playback devices active when recording starts.</summary>
internal sealed class SystemAudioMute : IDisposable
{
    private readonly List<MutedDevice> devices = new();

    public static SystemAudioMute Acquire()
    {
        var session = new SystemAudioMute();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                var muted = new MutedDevice(device);
                session.devices.Add(muted);
                muted.Mute();
            }
            return session;
        }
        catch
        {
            session.Dispose();
            throw new ServiceException("Could not mute system audio. Check your Windows playback devices before recording again.");
        }
    }

    public void Dispose()
    {
        // A disconnected endpoint must not prevent restoring the remaining devices.
        foreach (var device in devices) device.Dispose();
        devices.Clear();
    }

    private sealed class MutedDevice(MMDevice device) : IDisposable
    {
        private AudioEndpointVolume? volume;
        private bool changed;
        private volatile bool manuallyUnmuted;

        public void Mute()
        {
            volume = device.AudioEndpointVolume;
            if (volume.Mute) return;
            volume.OnVolumeNotification += OnVolume;
            changed = true;
            volume.Mute = true;
        }

        private void OnVolume(AudioVolumeNotificationData data)
        {
            // Respect a user's unmute (and any subsequent re-mute) during recording.
            if (!data.Muted) manuallyUnmuted = true;
        }

        public void Dispose()
        {
            try
            {
                if (volume != null && changed)
                {
                    volume.OnVolumeNotification -= OnVolume;
                    if (!manuallyUnmuted && volume.Mute) volume.Mute = false;
                }
            }
            catch (System.Runtime.InteropServices.COMException) { /* Device may have been unplugged. */ }
            finally { device.Dispose(); }
        }
    }
}

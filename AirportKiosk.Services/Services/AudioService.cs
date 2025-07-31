using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace AirportKiosk.Services
{
    public interface IAudioService
    {
        event EventHandler<AudioLevelEventArgs> AudioLevelChanged;
        event EventHandler<string> AudioDeviceError;

        Task<bool> InitializeAsync();
        Task StartRecordingAsync();
        Task<byte[]> StopRecordingAsync();
        Task PlayAudioAsync(byte[] audioData);
        Task<string> SaveAudioToFileAsync(byte[] audioData, string format = "wav");
        List<string> GetAvailableInputDevices();
        List<string> GetAvailableOutputDevices();
        bool SetInputDevice(string deviceName);
        bool SetOutputDevice(string deviceName);
        void Dispose();
    }

    public class AudioLevelEventArgs : EventArgs
    {
        public float Level { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class AudioService : IAudioService, IDisposable
    {
        private WaveInEvent _waveIn;
        private WaveOutEvent _waveOut;
        private MemoryStream _recordingStream;
        private WaveFileWriter _waveFileWriter;
        private bool _isRecording;
        private bool _isInitialized;
        private int _inputDeviceIndex = -1;
        private int _outputDeviceIndex = -1;
        private readonly object _lockObject = new object();

        // Audio settings
        private const int SampleRate = 44100;
        private const int Channels = 1; // Mono for speech
        private const int BitsPerSample = 16;

        public event EventHandler<AudioLevelEventArgs> AudioLevelChanged;
        public event EventHandler<string> AudioDeviceError;

        public async Task<bool> InitializeAsync()
        {
            try
            {
                await Task.Run(() =>
                {
                    // Simple check - try to create a WaveInEvent to see if input devices exist
                    try
                    {
                        using (var testWaveIn = new WaveInEvent())
                        {
                            // If we can create it, input devices exist
                        }
                    }
                    catch
                    {
                        throw new InvalidOperationException("No input audio devices found");
                    }

                    // Simple check for output devices
                    try
                    {
                        using (var testWaveOut = new WaveOutEvent())
                        {
                            // If we can create it, output devices exist
                        }
                    }
                    catch
                    {
                        throw new InvalidOperationException("No output audio devices found");
                    }

                    _isInitialized = true;
                });

                return true;
            }
            catch (Exception ex)
            {
                AudioDeviceError?.Invoke(this, $"Audio initialization failed: {ex.Message}");
                return false;
            }
        }

        public async Task StartRecordingAsync()
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException("Audio service not initialized");
            }

            if (_isRecording)
            {
                throw new InvalidOperationException("Recording already in progress");
            }

            await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    try
                    {
                        // Initialize recording stream
                        _recordingStream = new MemoryStream();

                        // Setup wave input
                        _waveIn = new WaveInEvent();
                        if (_inputDeviceIndex >= 0)
                        {
                            _waveIn.DeviceNumber = _inputDeviceIndex;
                        }
                        _waveIn.WaveFormat = new WaveFormat(SampleRate, BitsPerSample, Channels);
                        _waveIn.BufferMilliseconds = 100;

                        // Setup wave file writer
                        _waveFileWriter = new WaveFileWriter(_recordingStream, _waveIn.WaveFormat);

                        // Event handlers
                        _waveIn.DataAvailable += OnDataAvailable;
                        _waveIn.RecordingStopped += OnRecordingStopped;

                        // Start recording
                        _waveIn.StartRecording();
                        _isRecording = true;
                    }
                    catch (Exception ex)
                    {
                        AudioDeviceError?.Invoke(this, $"Failed to start recording: {ex.Message}");
                        throw;
                    }
                }
            });
        }

        public async Task<byte[]> StopRecordingAsync()
        {
            if (!_isRecording)
            {
                return new byte[0];
            }

            return await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    try
                    {
                        _isRecording = false;

                        // Stop recording
                        _waveIn?.StopRecording();
                        _waveIn?.Dispose();

                        // Finalize wave file
                        _waveFileWriter?.Dispose();

                        // Get recorded data
                        var audioData = _recordingStream?.ToArray() ?? new byte[0];

                        // Cleanup
                        _recordingStream?.Dispose();
                        _recordingStream = null;
                        _waveFileWriter = null;
                        _waveIn = null;

                        return audioData;
                    }
                    catch (Exception ex)
                    {
                        AudioDeviceError?.Invoke(this, $"Failed to stop recording: {ex.Message}");
                        return new byte[0];
                    }
                }
            });
        }

        public async Task PlayAudioAsync(byte[] audioData)
        {
            if (audioData == null || audioData.Length == 0)
            {
                throw new ArgumentException("No audio data provided");
            }

            await Task.Run(() =>
            {
                try
                {
                    using (var audioStream = new MemoryStream(audioData))
                    using (var waveFileReader = new WaveFileReader(audioStream))
                    {
                        _waveOut = new WaveOutEvent();
                        if (_outputDeviceIndex >= 0)
                        {
                            _waveOut.DeviceNumber = _outputDeviceIndex;
                        }

                        _waveOut.Init(waveFileReader);
                        _waveOut.Play();

                        // Wait for playback to complete
                        while (_waveOut.PlaybackState == PlaybackState.Playing)
                        {
                            System.Threading.Thread.Sleep(100);
                        }

                        _waveOut.Dispose();
                        _waveOut = null;
                    }
                }
                catch (Exception ex)
                {
                    AudioDeviceError?.Invoke(this, $"Audio playback failed: {ex.Message}");
                    throw;
                }
            });
        }

        public async Task<string> SaveAudioToFileAsync(byte[] audioData, string format = "wav")
        {
            if (audioData == null || audioData.Length == 0)
            {
                throw new ArgumentException("No audio data provided");
            }

            return await Task.Run(() =>
            {
                try
                {
                    var tempPath = Path.GetTempPath();
                    var fileName = $"audio_{DateTime.Now:yyyyMMdd_HHmmss}.{format.ToLower()}";
                    var filePath = Path.Combine(tempPath, fileName);

                    File.WriteAllBytes(filePath, audioData);
                    return filePath;
                }
                catch (Exception ex)
                {
                    AudioDeviceError?.Invoke(this, $"Failed to save audio file: {ex.Message}");
                    throw;
                }
            });
        }

        public List<string> GetAvailableInputDevices()
        {
            var devices = new List<string>();

            try
            {
                // Use CoreAudio API to enumerate devices (more reliable in NAudio 2.x)
                var enumerator = new MMDeviceEnumerator();
                var inputDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);

                for (int i = 0; i < inputDevices.Count; i++)
                {
                    devices.Add($"{i}: {inputDevices[i].FriendlyName}");
                }
            }
            catch (Exception ex)
            {
                AudioDeviceError?.Invoke(this, $"Failed to enumerate input devices: {ex.Message}");

                // Fallback: just return default device
                devices.Add("0: Default Input Device");
            }

            return devices;
        }

        public List<string> GetAvailableOutputDevices()
        {
            var devices = new List<string>();

            try
            {
                // Use CoreAudio API to enumerate devices
                var enumerator = new MMDeviceEnumerator();
                var outputDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

                for (int i = 0; i < outputDevices.Count; i++)
                {
                    devices.Add($"{i}: {outputDevices[i].FriendlyName}");
                }
            }
            catch (Exception ex)
            {
                AudioDeviceError?.Invoke(this, $"Failed to enumerate output devices: {ex.Message}");

                // Fallback: just return default device
                devices.Add("0: Default Output Device");
            }

            return devices;
        }

        public bool SetInputDevice(string deviceName)
        {
            try
            {
                if (string.IsNullOrEmpty(deviceName))
                {
                    _inputDeviceIndex = -1;
                    return true;
                }

                // Extract device index from device name format "0: Device Name"
                var parts = deviceName.Split(':');
                if (parts.Length >= 2 && int.TryParse(parts[0], out int deviceIndex))
                {
                    _inputDeviceIndex = deviceIndex;
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                AudioDeviceError?.Invoke(this, $"Failed to set input device: {ex.Message}");
                return false;
            }
        }

        public bool SetOutputDevice(string deviceName)
        {
            try
            {
                if (string.IsNullOrEmpty(deviceName))
                {
                    _outputDeviceIndex = -1;
                    return true;
                }

                // Extract device index from device name format "0: Device Name"
                var parts = deviceName.Split(':');
                if (parts.Length >= 2 && int.TryParse(parts[0], out int deviceIndex))
                {
                    _outputDeviceIndex = deviceIndex;
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                AudioDeviceError?.Invoke(this, $"Failed to set output device: {ex.Message}");
                return false;
            }
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            try
            {
                if (_isRecording && _waveFileWriter != null)
                {
                    _waveFileWriter.Write(e.Buffer, 0, e.BytesRecorded);

                    // Calculate audio level for visualization
                    var audioLevel = CalculateAudioLevel(e.Buffer, e.BytesRecorded);
                    AudioLevelChanged?.Invoke(this, new AudioLevelEventArgs
                    {
                        Level = audioLevel,
                        Timestamp = DateTime.Now
                    });
                }
            }
            catch (Exception ex)
            {
                AudioDeviceError?.Invoke(this, $"Error during recording: {ex.Message}");
            }
        }

        private void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                AudioDeviceError?.Invoke(this, $"Recording stopped with error: {e.Exception.Message}");
            }
        }

        private float CalculateAudioLevel(byte[] buffer, int bytesRecorded)
        {
            try
            {
                float sum = 0;
                int sampleCount = bytesRecorded / 2; // 16-bit samples

                for (int i = 0; i < bytesRecorded - 1; i += 2)
                {
                    short sample = (short)((buffer[i + 1] << 8) | buffer[i]);
                    sum += sample * sample;
                }

                float rms = (float)Math.Sqrt(sum / sampleCount);
                return Math.Min(1.0f, rms / 32768.0f); // Normalize to 0-1
            }
            catch
            {
                return 0.0f;
            }
        }

        public void Dispose()
        {
            try
            {
                if (_isRecording)
                {
                    _waveIn?.StopRecording();
                }

                _waveIn?.Dispose();
                _waveOut?.Dispose();
                _waveFileWriter?.Dispose();
                _recordingStream?.Dispose();
            }
            catch (Exception ex)
            {
                AudioDeviceError?.Invoke(this, $"Error during disposal: {ex.Message}");
            }
        }
    }
}
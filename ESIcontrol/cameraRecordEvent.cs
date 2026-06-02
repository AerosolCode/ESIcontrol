using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ESIcontrol
{
    public class CameraRecordEvent
    {
        private readonly string _commandsDir;
        private readonly int _intervalMs;

        private bool _running;
        private CancellationTokenSource _cts;
        private Task _workerTask;

        public event Action<string> OnLog;

        public bool IsRunning => _running;

        public CameraRecordEvent(string commandsDir, int intervalMs = 60000)
        {
            _commandsDir = commandsDir;
            _intervalMs = intervalMs;

            Directory.CreateDirectory(_commandsDir);
        }

        public void Start()
        {
            if (_running) return;

            _cts = new CancellationTokenSource();
            _running = true;

            OnLog?.Invoke("Camera periodic recording started.");

            _workerTask = Task.Run(() => PeriodicCameraAsync(_cts.Token));
        }

        public async Task StopAsync()
        {
            if (!_running) return;

            _running = false;

            try
            {
                _cts?.Cancel();
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"Camera stop cancel failed: {ex.Message}");
            }

            if (_workerTask != null)
            {
                try
                {
                    await _workerTask;
                }
                catch (TaskCanceledException)
                {
                }
                catch (Exception ex)
                {
                    OnLog?.Invoke($"Camera worker stopped with error: {ex.Message}");
                }
            }

            _cts?.Dispose();
            _cts = null;
            _workerTask = null;

            OnLog?.Invoke("Camera periodic recording stopped.");
        }

        public void EnqueueRecordNow()
        {
            try
            {
                EnqueueCameraRecordCommand();
                OnLog?.Invoke("Camera record command queued.");
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"Failed to queue camera record command: {ex.Message}");
            }
        }

        private void EnqueueCameraRecordCommand()
        {
            string id = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");

            string tmp = Path.Combine(_commandsDir, $"tmp_{id}.json");
            string dst = Path.Combine(_commandsDir, $"{id}.json");

            var cmd = new
            {
                id = id,
                type = "record",
                priority = 0
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            File.WriteAllText(tmp, JsonSerializer.Serialize(cmd, options));
            File.Move(tmp, dst);
        }

        private async Task PeriodicCameraAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    EnqueueCameraRecordCommand();
                    OnLog?.Invoke("Periodic camera record command queued.");
                }
                catch (Exception ex)
                {
                    OnLog?.Invoke($"Failed to queue periodic camera command: {ex.Message}");
                }

                try
                {
                    await Task.Delay(_intervalMs, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }
    }
}
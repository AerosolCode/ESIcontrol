using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ESIcontrol
{
    public class CameraRecordEvent
    {
        private readonly string _commandsDir;
        private readonly string _doneDir;
        private readonly string _errorDir;
        private readonly object _commandLock = new object();
        private readonly List<string> _publishedCommandIds = new List<string>();
        private string _publicationError;

        private readonly object _lifecycleLock = new object();
        private bool _running;
        private CancellationTokenSource _cts;
        private Task _workerTask;

        public event Action<string> OnLog;

        public bool IsRunning => _running;

        /// <summary>記録間隔[ms]。Start() 前に設定すれば次回開始から反映される。</summary>
        public int IntervalMs { get; set; }
        public string ExperimentId { get; set; }
        public string ConditionId { get; set; }

        public CameraRecordEvent(string commandsDir, int intervalMs = 60000)
        {
            _commandsDir = commandsDir;
            string baseDir = Directory.GetParent(_commandsDir)?.FullName
                ?? throw new ArgumentException("commandsDir must have a parent directory.", nameof(commandsDir));
            _doneDir = Path.Combine(baseDir, "done");
            _errorDir = Path.Combine(baseDir, "error");
            IntervalMs = intervalMs;

            Directory.CreateDirectory(_commandsDir);
            Directory.CreateDirectory(_doneDir);
            Directory.CreateDirectory(_errorDir);
        }

        public void Start()
        {
            lock (_lifecycleLock)
            {
                if (_running) return;
                if (IntervalMs <= 0)
                    throw new InvalidOperationException("Camera interval must be positive.");

                _cts = new CancellationTokenSource();
                _running = true;
                lock (_commandLock)
                {
                    _publishedCommandIds.Clear();
                    _publicationError = null;
                }
                _workerTask = Task.Run(() => PeriodicCameraAsync(_cts.Token));
            }

            OnLog?.Invoke("Camera periodic recording started.");
        }

        public async Task StopAsync()
        {
            CancellationTokenSource cts;
            Task worker;

            lock (_lifecycleLock)
            {
                if (!_running) return;
                _running = false;

                cts = _cts;
                worker = _workerTask;
                _cts = null;
                _workerTask = null;
            }

            try
            {
                cts?.Cancel();
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"Camera stop cancel failed: {ex.Message}");
            }

            if (worker != null)
            {
                try
                {
                    await worker.ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                }
                catch (Exception ex)
                {
                    OnLog?.Invoke($"Camera worker stopped with error: {ex.Message}");
                }
            }

            cts?.Dispose();

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
                lock (_commandLock) _publicationError = ex.Message;
                OnLog?.Invoke($"Failed to queue camera record command: {ex.Message}");
            }
        }

        public async Task WaitForPublishedCommandsAsync(TimeSpan timeout, CancellationToken token)
        {
            string[] ids;
            string publicationError;
            lock (_commandLock)
            {
                ids = _publishedCommandIds.ToArray();
                publicationError = _publicationError;
            }
            if (publicationError != null)
                throw new InvalidOperationException("Camera command publication failed: " + publicationError);

            DateTime deadline = DateTime.UtcNow + timeout;
            foreach (string id in ids)
            {
                string done = Path.Combine(_doneDir, id + ".json");
                string error = Path.Combine(_errorDir, id + ".json");

                while (!File.Exists(done))
                {
                    token.ThrowIfCancellationRequested();
                    if (File.Exists(error))
                        throw new InvalidOperationException("Camera command failed: " + id);
                    if (DateTime.UtcNow >= deadline)
                        throw new TimeoutException("Timed out waiting for camera command: " + id);
                    await Task.Delay(200, token).ConfigureAwait(false);
                }
            }
        }

        private void EnqueueCameraRecordCommand()
        {
            string id = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" +
                Guid.NewGuid().ToString("N").Substring(0, 8);

            string tmp = Path.Combine(_commandsDir, $"tmp_{id}.json");
            string dst = Path.Combine(_commandsDir, $"{id}.json");

            var cmd = new
            {
                id = id,
                type = "record",
                priority = 0,
                experimentId = ExperimentId,
                conditionId = ConditionId,
                requestedAt = DateTimeOffset.Now.ToString("O")
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            File.WriteAllText(tmp, JsonSerializer.Serialize(cmd, options));
            File.Move(tmp, dst);
            lock (_commandLock) _publishedCommandIds.Add(id);
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
                    lock (_commandLock) _publicationError = ex.Message;
                    OnLog?.Invoke($"Failed to queue periodic camera command: {ex.Message}");
                }

                try
                {
                    await Task.Delay(IntervalMs, token).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }
    }
}

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ESIcontrol
{
    /// <summary>
    /// 一定間隔で測定条件JSONを incoming フォルダへ投下するジェネレータ。
    /// </summary>
    public class SmpsRecordEvent
    {
        private readonly string _incomingDir;
        private readonly string _stagingDir;
        private readonly string _doneDir;
        private readonly string _errorDir;
        private readonly object _commandLock = new object();
        private readonly List<string> _publishedCommandIds = new List<string>();
        private string _publicationError;

        private readonly MeasurementConditionTemplate _template;
        private readonly object _templateLock = new object();

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
        public DateTimeOffset? MustCompleteBy { get; set; }

        /// <summary>
        /// true の場合、sampleName にタイムスタンプIDを付与して各ファイルを区別する。
        /// </summary>
        public bool AppendTimestampToSampleName { get; set; } = false;

        public SmpsRecordEvent(
            string incomingDir,
            MeasurementConditionTemplate template,
            int intervalMs = 60000)
        {
            _incomingDir = incomingDir;
            _template = template ?? throw new ArgumentNullException(nameof(template));
            IntervalMs = intervalMs;

            // 監視対象外のサブフォルダ（書き込み途中を拾わせないため）
            _stagingDir = Path.Combine(_incomingDir, ".staging");
            string baseDir = Directory.GetParent(_incomingDir)?.FullName
                ?? throw new ArgumentException("incomingDir must have a parent directory.", nameof(incomingDir));
            _doneDir = Path.Combine(baseDir, "done");
            _errorDir = Path.Combine(baseDir, "error");

            Directory.CreateDirectory(_incomingDir);
            Directory.CreateDirectory(_stagingDir);
            Directory.CreateDirectory(_doneDir);
            Directory.CreateDirectory(_errorDir);
        }

        /// <summary>
        /// SMPS 測定条件（スキャン範囲・点数・滞在/整定時間）を更新する。
        /// 次に投下するファイルから反映される。
        /// </summary>
        public void UpdateScanCondition(
            double startHv,
            double stopHv,
            int pointsPerDecade,
            double dwellTimeSec,
            double settlingTimeSec)
        {
            lock (_templateLock)
            {
                _template.StartHv = startHv;
                _template.StopHv = stopHv;
                _template.PointsPerDecade = pointsPerDecade;
                _template.DwellTimeSec = dwellTimeSec;
                _template.SettlingTimeSec = settlingTimeSec;
            }
        }

        public void Start()
        {
            lock (_lifecycleLock)
            {
                if (_running) return;
                if (IntervalMs <= 0)
                    throw new InvalidOperationException("SMPS interval must be positive.");

                _cts = new CancellationTokenSource();
                _running = true;
                lock (_commandLock)
                {
                    _publishedCommandIds.Clear();
                    _publicationError = null;
                }
                _workerTask = Task.Run(() => PeriodicAsync(_cts.Token));
            }

            OnLog?.Invoke("Condition file generation started.");
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
                OnLog?.Invoke($"Generator stop cancel failed: {ex.Message}");
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
                    OnLog?.Invoke($"Generator worker stopped with error: {ex.Message}");
                }
            }

            cts?.Dispose();

            OnLog?.Invoke("Condition file generation stopped.");
        }

        /// <summary>
        /// 今すぐ1件の条件ファイルを投下する。
        /// </summary>
        public void BeginBatch()
        {
            lock (_commandLock)
            {
                _publishedCommandIds.Clear();
                _publicationError = null;
            }
        }

        public string EnqueueNow()
        {
            try
            {
                string file = WriteConditionFile();
                OnLog?.Invoke("Condition file queued: " + Path.GetFileName(file));
                return Path.GetFileNameWithoutExtension(file);
            }
            catch (Exception ex)
            {
                lock (_commandLock) _publicationError = ex.Message;
                OnLog?.Invoke($"Failed to queue condition file: {ex.Message}");
                throw;
            }
        }

        public async Task WaitForCommandAsync(string id, TimeSpan timeout, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Command ID is empty.", nameof(id));

            string done = Path.Combine(_doneDir, id + ".json");
            string error = Path.Combine(_errorDir, id + ".json");
            DateTime deadline = DateTime.UtcNow + timeout;

            while (!File.Exists(done))
            {
                token.ThrowIfCancellationRequested();
                if (File.Exists(error))
                    throw new InvalidOperationException("SMPS command failed: " + id);
                if (DateTime.UtcNow >= deadline)
                    throw new TimeoutException("Timed out waiting for SMPS command: " + id);
                await Task.Delay(200, token).ConfigureAwait(false);
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
                throw new InvalidOperationException("SMPS command publication failed: " + publicationError);

            DateTime deadline = DateTime.UtcNow + timeout;
            foreach (string id in ids)
            {
                string done = Path.Combine(_doneDir, id + ".json");
                string error = Path.Combine(_errorDir, id + ".json");

                while (!File.Exists(done))
                {
                    token.ThrowIfCancellationRequested();
                    if (File.Exists(error))
                        throw new InvalidOperationException("SMPS command failed: " + id);
                    if (DateTime.UtcNow >= deadline)
                        throw new TimeoutException("Timed out waiting for SMPS command: " + id);
                    await Task.Delay(200, token).ConfigureAwait(false);
                }
            }
        }

        private string WriteConditionFile()
        {
            string id = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" +
                Guid.NewGuid().ToString("N").Substring(0, 8);

            string tmp = Path.Combine(_stagingDir, $"{id}.json.tmp");
            string dst = Path.Combine(_incomingDir, $"{id}.json");

            string json;

            lock (_templateLock)
            {
                MeasurementConditionTemplate snapshot = _template.Clone();
                snapshot.ExperimentId = ExperimentId;
                snapshot.ConditionId = ConditionId;
                snapshot.CommandId = id;
                snapshot.RequestedAt = DateTimeOffset.Now.ToString("O");
                snapshot.MustCompleteBy = MustCompleteBy;

                if (AppendTimestampToSampleName)
                    snapshot.SampleName = $"{_template.SampleName}_{id}";

                json = JsonSerializer.Serialize(snapshot, SerializerOptions);
            }

            File.WriteAllText(tmp, json);
            File.Move(tmp, dst);
            lock (_commandLock) _publishedCommandIds.Add(id);

            return dst;
        }

        private async Task PeriodicAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    string file = WriteConditionFile();
                    OnLog?.Invoke("Periodic condition file queued: " + Path.GetFileName(file));
                }
                catch (Exception ex)
                {
                    lock (_commandLock) _publicationError = ex.Message;
                    OnLog?.Invoke($"Failed to queue periodic condition file: {ex.Message}");
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

        private static readonly JsonSerializerOptions SerializerOptions =
            new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
    }

    /// <summary>
    /// 生成する測定条件のテンプレート。プロパティ名は camelCase で出力される。
    /// </summary>
    public class MeasurementConditionTemplate
    {
        public string ExperimentId { get; set; }
        public string ConditionId { get; set; }
        public string CommandId { get; set; }
        public string RequestedAt { get; set; }
        public DateTimeOffset? MustCompleteBy { get; set; }
        public string SampleName { get; set; } = "NaCl_log_test_001";

        public string ScanMode { get; set; } = "log";
        public double StartHv { get; set; } = 10.0;
        public double StopHv { get; set; } = 1000.0;
        public int PointsPerDecade { get; set; } = 5;

        public double? StepHv { get; set; } = null;

        public double DwellTimeSec { get; set; } = 5.0;
        public double SettlingTimeSec { get; set; } = 5.0;

        public string AoPhysicalChannel { get; set; } = "Dev3/ao0";
        public string DoPhysicalChannel { get; set; } = "Dev3/port0/line0";
        public double AoMinVoltage { get; set; } = 0.0;
        public double AoMaxVoltage { get; set; } = 6.0;
        public double HvAtAoMin { get; set; } = 0.0;
        public double HvAtAoMax { get; set; } = 10000.0;

        public string CpcComPort { get; set; } = "COM3";
        public int CpcBaudRate { get; set; } = 115200;
        public int ConcentrationColumnIndex { get; set; } = 1;

        public MeasurementConditionTemplate Clone()
        {
            return (MeasurementConditionTemplate)MemberwiseClone();
        }
    }
}

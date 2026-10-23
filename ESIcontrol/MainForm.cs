using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Text.Json;
using static ESIcontrol.PumpController;
using System.Threading;
using OpenCvSharp;
using System.Xml.Linq;

namespace ESIcontrol
{
    public partial class MainForm : Form
    {
        private PumpController _pump;
        private HvController _hv;
        private ValveController _valve;
        private BufferedAiLogger _aiLogger;
        private CameraRecordEvent _camera;
        private SmpsRecordEvent _smps;

        private readonly string _logFilePath;
        //private DinoRecorder _dino;
        private MfcController _mfc;
        private bool _stopRequested = false;
        private bool _sequenceRunning = false;
        private CancellationTokenSource _sequenceCts;
        private Task _sequenceTask;
        private bool _initialized;
        private bool _closing;
        private bool _closeReady;
        private readonly object _logFileLock = new object();
        private double _diameterMM = 5.0;
        private int _infusePosition = 5;
        private int _cameraIntervalMs = 60000;    // X: infuse区間中のカメラ記録間隔[ms]
        private int _smpsIntervalMs = 300000;     // Y: infuse区間中のSMPS記録間隔[ms]
        private const int DmaSafetyMarginMs = 30000;

        public MainForm()
        {
            InitializeComponent();
            btnStartSequence.Enabled = false;

            AppPaths.EnsureDirectories();

            _logFilePath = Path.Combine(
                AppPaths.EsiLogDirectory,
                "ESIcontrol_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log"
            );

            try
            {
                // 起動時に自動接続
                _pump = new PumpController("COM7");
                _hv = new HvController("Dev4/ao1", "Dev4/port0/line0");
                _hv.Initialize();
                AppendLog("HV controller initialized.");

                _mfc = new MfcController("Dev4/ao0");
                _mfc.Initialize();
                AppendLog("MFC controller initialized.");

                _valve = new ValveController("COM5");
                _aiLogger = new BufferedAiLogger(
                    new string[] { "Dev4/ai0", "Dev4/ai1", "Dev4/ai2", "Dev4/ai3" },
                    100.0,
                    100,
                    AppPaths.AiLogCsv
                );
                _aiLogger.OnLog += AppendLog;
                AppendLog("AI logger prepared.");
                //_dino = new DinoRecorder();
                //_dino.OnLog += AppendLog;
                _camera = new CameraRecordEvent(AppPaths.CameraCommands, _cameraIntervalMs);
                _camera.OnLog += AppendLog;

                var template = new MeasurementConditionTemplate();
                _smps = new SmpsRecordEvent(AppPaths.DmaIncoming, template, _smpsIntervalMs);
                _smps.OnLog += AppendLog;

                _pump.Diameter(_diameterMM);

                string ver = _pump.GetVersion();
                AppendLog("Pump connected: " + ver);
                _initialized = true;
                btnStartSequence.Enabled = true;
                AppendLog("Shared data root: " + AppPaths.SharedRoot);

            }
            catch (Exception ex)
            {
                AppendLog("Initialization error: " + ex.Message);
                try { SafeStop().GetAwaiter().GetResult(); } catch { }
                DisposeControllers();
            }

            SetPhase("Idle");
        }

        private void AppendLog(string msg)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff ") + msg;

            try
            {
                lock (_logFileLock)
                    File.AppendAllText(_logFilePath, line + Environment.NewLine);
            }
            catch { }

            try
            {
                if (txtLog == null || txtLog.IsDisposed || Disposing) return;
                if (txtLog.InvokeRequired)
                {
                    if (txtLog.IsHandleCreated)
                        txtLog.BeginInvoke(new Action(() =>
                        {
                            if (!txtLog.IsDisposed)
                                txtLog.AppendText(line + Environment.NewLine);
                        }));
                    return;
                }
                txtLog.AppendText(line + Environment.NewLine);
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        /// <summary>
        /// 任意のコントロールの Text をスレッドセーフに設定する。
        /// コントロールが未配置(null)の場合は何もしない。
        /// </summary>
        private void SetControlText(Control ctrl, string text)
        {
            if (ctrl == null) return;

            if (ctrl.InvokeRequired)
            {
                if (ctrl.IsHandleCreated && !ctrl.IsDisposed)
                    ctrl.BeginInvoke(new Action(() =>
                    {
                        if (!ctrl.IsDisposed) ctrl.Text = text;
                    }));
                return;
            }

            ctrl.Text = text;
        }

        /// <summary>
        /// 現在の計測条件を txtCurrentCondition に整形表示する。
        /// index は 1始まり、total は全条件数。
        /// </summary>
        private void ShowCurrentCondition(ConditionPoint c, int index, int total)
        {
            ShowOperatingCondition(c, index, total, null, null, "Not started", 0.0,
                c.TimeInfuseMs * 0.001, "Not started", 0, null);
        }

        private void ShowOperatingCondition(
            ConditionPoint c,
            int index,
            int total,
            string experimentId,
            string conditionId,
            string operationPhase,
            double infuseElapsedSec,
            double infuseRemainingSec,
            string dmaStatus,
            int dmaCompletedCount,
            string dmaCommandId)
        {
            string text =
                $"Condition {index} / {total}\r\n" +
                $"Experiment     : {experimentId ?? "--"}\r\n" +
                $"Condition ID   : {conditionId ?? "--"}\r\n" +
                $"Operation      : {operationPhase}\r\n" +
                $"Infuse elapsed : {infuseElapsedSec:F1} s\r\n" +
                $"Infuse remain  : {Math.Max(0.0, infuseRemainingSec):F1} s\r\n" +
                $"DMA status     : {dmaStatus}\r\n" +
                $"DMA completed  : {dmaCompletedCount}\r\n" +
                $"DMA planned max: {c.GetMaxDmaScans(DmaSafetyMarginMs)}\r\n" +
                $"DMA command    : {dmaCommandId ?? "--"}\r\n" +
                $"------------------------------\r\n" +
                $"HV            : {c.HvKv:F3} kV\r\n" +
                $"Gas (MFC)     : {c.GasFlowLpm:F2} LPM\r\n" +
                $"Withdraw pos  : {c.WithdrawPosition}\r\n" +
                $"--- Pump ---\r\n" +
                $"Q infuse      : {c.FlowRateInfuseMlMin:F3} mL/min\r\n" +
                $"Q withdraw    : {c.FlowRateWithdrawMlMin:F3} mL/min\r\n" +
                $"t infuse      : {c.TimeInfuseMs * 0.001:F1} s\r\n" +
                $"t offset      : {c.TimeInfuseOffsetMs * 0.001:F1} s\r\n" +
                $"t withdraw    : {c.TimeWithdrawMs * 0.001:F1} s\r\n" +
                $"V infuse total: {c.InfuseVolumeMl:F3} mL\r\n" +
                $"V withdraw    : {c.WithdrawVolumeMl:F3} mL\r\n" +
                $"--- Recording interval ---\r\n" +
                $"Camera        : {c.CameraIntervalMs * 0.001:F0} s\r\n" +
                $"SMPS          : {c.SmpsIntervalMs * 0.001:F0} s\r\n" +
                $"--- SMPS scan ---\r\n" +
                $"startHv       : {c.SmpsStartHv:G}\r\n" +
                $"stopHv        : {c.SmpsStopHv:G}\r\n" +
                $"pts/decade    : {c.SmpsPointsPerDecade}\r\n" +
                $"dwell         : {c.SmpsDwellSec:F1} s\r\n" +
                $"settling      : {c.SmpsSettlingSec:F1} s";

            SetControlText(txtCurrentCondition, text);
        }

        private async Task RunInfuseWindowAsync(
            ConditionPoint c,
            int conditionIndex,
            int conditionCount,
            string experimentId,
            string conditionId,
            CancellationToken token)
        {
            _camera.IntervalMs = c.CameraIntervalMs;
            _camera.ExperimentId = experimentId;
            _camera.ConditionId = conditionId;
            _smps.ExperimentId = experimentId;
            _smps.ConditionId = conditionId;
            _smps.UpdateScanCondition(
                c.SmpsStartHv,
                c.SmpsStopHv,
                c.SmpsPointsPerDecade,
                c.SmpsDwellSec,
                c.SmpsSettlingSec);
            _smps.BeginBatch();

            var dmaState = new DmaScheduleState();
            _pump.SetRate(c.FlowRateInfuseMlMin, "MM");

            var infuseClock = Stopwatch.StartNew();
            _pump.Run();
            _smps.MustCompleteBy = DateTimeOffset.UtcNow.AddMilliseconds(
                Math.Max(0.0, c.TimeInfuseMs - infuseClock.Elapsed.TotalMilliseconds));
            _camera.Start();
            SetPhase("Infuse + recording");
            AppendLog($"Infuse started with an absolute deadline of {c.TimeInfuseMs * 0.001:F3} s.");

            Task dmaTask = RunDmaScheduleAsync(c, infuseClock, dmaState, token);

            try
            {
                while (infuseClock.ElapsedMilliseconds < c.TimeInfuseMs)
                {
                    token.ThrowIfCancellationRequested();
                    if (dmaTask.IsFaulted) await dmaTask;

                    double elapsed = infuseClock.Elapsed.TotalSeconds;
                    double remaining = (c.TimeInfuseMs - infuseClock.Elapsed.TotalMilliseconds) / 1000.0;
                    dmaState.Snapshot(out string dmaStatus, out int completed, out string commandId);
                    ShowOperatingCondition(
                        c, conditionIndex, conditionCount, experimentId, conditionId,
                        "Infuse + recording", elapsed, remaining,
                        dmaStatus, completed, commandId);

                    int delayMs = (int)Math.Min(200.0,
                        Math.Max(1.0, c.TimeInfuseMs - infuseClock.Elapsed.TotalMilliseconds));
                    await Task.Delay(delayMs, token);
                }
            }
            finally
            {
                // DMAやCameraの完了待ちより先に、絶対期限でpumpを停止する。
                infuseClock.Stop();
                try { _pump.Stop(); } catch { }
            }

            AppendLog($"Infuse stopped at {infuseClock.Elapsed.TotalSeconds:F3} s.");
            SetPhase("Infuse deadline reached");
            await _camera.StopAsync();

            if (!dmaTask.IsCompleted)
            {
                SetPhase("Pump stopped; waiting for DMA");
                dmaState.Set("Overrun: waiting with pump stopped", null);
                dmaState.Snapshot(out string dmaStatus, out int completed, out string commandId);
                ShowOperatingCondition(
                    c, conditionIndex, conditionCount, experimentId, conditionId,
                    "Pump stopped; waiting for DMA", infuseClock.Elapsed.TotalSeconds, 0.0,
                    dmaStatus, completed, commandId);
            }

            // 次条件はDMAのHVがOFFになり、done/errorへ移動するまで開始しない。
            await dmaTask;
            await _camera.WaitForPublishedCommandsAsync(TimeSpan.FromSeconds(60), token);
            SetPhase("Infuse complete");

            dmaState.Snapshot(out string finalDmaStatus, out int finalCompleted, out string finalCommandId);
            ShowOperatingCondition(
                c, conditionIndex, conditionCount, experimentId, conditionId,
                "Infuse complete", infuseClock.Elapsed.TotalSeconds, 0.0,
                finalDmaStatus, finalCompleted, finalCommandId);
        }

        private async Task RunDmaScheduleAsync(
            ConditionPoint c,
            Stopwatch infuseClock,
            DmaScheduleState state,
            CancellationToken token)
        {
            double nextStartMs = 0.0;
            double requiredMs = c.EstimatedSmpsDurationMs + DmaSafetyMarginMs;

            while (true)
            {
                if (nextStartMs + requiredMs > c.TimeInfuseMs)
                {
                    state.Set(
                        $"No room for next scan (need {requiredMs * 0.001:F1} s)", null);
                    return;
                }

                while (infuseClock.Elapsed.TotalMilliseconds < nextStartMs)
                {
                    token.ThrowIfCancellationRequested();
                    int waitMs = (int)Math.Min(200.0,
                        Math.Max(1.0, nextStartMs - infuseClock.Elapsed.TotalMilliseconds));
                    await Task.Delay(waitMs, token).ConfigureAwait(false);
                }

                double remainingMs = c.TimeInfuseMs - infuseClock.Elapsed.TotalMilliseconds;
                if (remainingMs < requiredMs)
                {
                    state.Set(
                        $"No room for next scan (need {requiredMs * 0.001:F1} s)", null);
                    return;
                }

                string commandId = _smps.EnqueueNow();
                state.Set("DMA scan running", commandId);
                AppendLog(
                    $"DMA command {commandId} queued with {remainingMs * 0.001:F1} s remaining.");

                await _smps.WaitForCommandAsync(
                    commandId,
                    TimeSpan.FromMilliseconds(requiredMs),
                    token).ConfigureAwait(false);

                state.MarkCompleted(commandId);
                nextStartMs += c.SmpsIntervalMs;
                while (nextStartMs <= infuseClock.Elapsed.TotalMilliseconds)
                    nextStartMs += c.SmpsIntervalMs;
            }
        }

        /// <summary>現在のフェーズ（Withdraw / Infuse など）を lblPhase に表示する。</summary>
        private void SetPhase(string phase)
        {
            SetControlText(lblPhase, "Phase: " + phase);
        }

        private async void btnStartSequence_Click(object sender, EventArgs e)
        {
            if (!_initialized)
            {
                AppendLog("Sequence cannot start because initialization did not complete.");
                return;
            }

            if (_sequenceRunning)
            {
                AppendLog("Sequence already running.");
                return;
            }

            _stopRequested = false;
            _sequenceCts?.Dispose();
            _sequenceCts = new CancellationTokenSource();
            CancellationToken token = _sequenceCts.Token;

            _sequenceRunning = true;
            btnStartSequence.Enabled = false;
            string experimentId = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" +
                Guid.NewGuid().ToString("N").Substring(0, 8);

            // シーケンス本体はバックグラウンドで実行し、UIスレッドをブロックしない。
            // 後始末は finally の1経路に集約する（STOP/正常終了/例外いずれでもここを通る）。
            _sequenceTask = Task.Run(async () =>
            {
                try
                {
                    string conditionsCsvPath = AppPaths.ConditionsCsv;

                    List<ConditionPoint> conditions = LoadConditionsFromCsv(conditionsCsvPath);

                    AppendLog($"Loaded {conditions.Count} condition(s) from {Path.GetFullPath(conditionsCsvPath)}");

                    if (conditions.Count == 0)
                    {
                        AppendLog("No conditions to run. Aborting.");
                        return;
                    }

                    if (!_aiLogger.IsRunning)
                    {
                        _aiLogger.Start();
                    }

                    for (int condIndex = 0; condIndex < conditions.Count; condIndex++)
                    {
                        token.ThrowIfCancellationRequested();
                        if (_stopRequested) break;

                        ConditionPoint c = conditions[condIndex];
                        string conditionId = $"{experimentId}_condition_{condIndex + 1:D3}";

                        // 現在の計測条件をGUIへ反映
                        ShowOperatingCondition(
                            c, condIndex + 1, conditions.Count,
                            experimentId, conditionId, "Withdraw", 0.0,
                            c.TimeInfuseMs * 0.001, "Not started", 0, null);
                        SetPhase("Withdraw");

                        AppendLog
                        (
                            $"Start condition: " +
                            $"HV={c.HvKv:F3} kV, " +
                            $"twith={c.TimeWithdrawMs * 0.001:F3} s, " +
                            $"tinf={c.TimeInfuseMs * 0.001:F3} s, " +
                            $"Qwith={c.FlowRateWithdrawMlMin:F3} mL/min, " +
                            $"Qinf={c.FlowRateInfuseMlMin:F3} mL/min," +
                            $"Vwith={c.WithdrawVolumeMl:F3} mL, " +
                            $"Vinf={c.InfuseVolumeMl:F3} mL"
                        );

                        // 初期化
                        _pump.Stop();
                        _pump.ClearWithdrawDispensed();
                        _pump.SetVolumeMl(10.0);

                        // Withdraw
                        _pump.SetDirectionWithdraw();
                        _valve.Change(c.WithdrawPosition);
                        _pump.SetRate(c.FlowRateWithdrawMlMin, "MM");
                        _pump.Run();
                        AppendLog("Withdraw started.");
                        await Task.Delay(c.TimeWithdrawMs, token);
                        _pump.Stop();
                        _pump.Stop();
                        AppendLog("Withdraw stopped.");

                        _mfc.SetFlowLpm(c.GasFlowLpm);
                        AppendLog("MFC set to " + c.GasFlowLpm.ToString("F2") + " LPM");

                        _hv.SetVoltageKv(c.HvKv);
                        _hv.EnableOutput();
                        AppendLog($"HV enabled at {c.HvKv:F3} kV");

                        // Infuse（オフセット送り）
                        _pump.ClearInfuseDispensed();
                        _pump.SetDirectionInfuse();
                        _valve.Change(_infusePosition);
                        SetPhase("Infuse offset");
                        _pump.SetRate(c.FlowRateWithdrawMlMin, "MM");
                        _pump.Run();
                        await Task.Delay(c.TimeInfuseOffsetMs, token);
                        _pump.Stop();
                        _pump.Stop();

                        await RunInfuseWindowAsync(
                            c,
                            condIndex + 1,
                            conditions.Count,
                            experimentId,
                            conditionId,
                            token);

                        // 条件終了
                        _hv.OutputOff();
                        AppendLog("HV off");

                        _mfc.OutputOff();
                        AppendLog("MFC off");

                        SetPhase("Condition done");

                        await Task.Delay(1000, token);
                    }
                }
                catch (OperationCanceledException)
                {
                    AppendLog("Sequence canceled.");
                }
                catch (Exception ex)
                {
                    AppendLog("Sequence error: " + ex.Message);
                }
                finally
                {
                    await SafeStop();
                }
            });

            await _sequenceTask;

            _sequenceRunning = false;
            _sequenceTask = null;
            if (!_closing)
            {
                btnStartSequence.Enabled = _initialized;
                SetPhase("Idle");
                AppendLog("Sequence finished.");
            }
        }

        public async Task BalanceWithdrawToZeroAsync(double correctionRateMlMin, double toleranceMl, Action<string> log)
        {
            for (int n = 0; n < 10; n++)
            {
                DispensedVolume d = _pump.GetDispensed();
                double w = d.WithdrawMl;
                double i = d.InfuseMl;

                log("Current WDR = " + w.ToString("F4") + " mL");
                log("Current INF = " + i.ToString("F4") + " mL");

                if (w - i <= toleranceMl)
                {
                    log("WDR balanced.");
                    return;
                }

                _pump.Stop();
                _pump.SetDirectionInfuse();
                _pump.SetRate(correctionRateMlMin, "MM");
                _pump.SetVolumeMl(w);
                _pump.Run();

                double runMs = w / correctionRateMlMin * 60.0 * 1000.0;
                await Task.Delay((int)Math.Ceiling(runMs) + 300);

                _pump.Stop();
            }

            log("Balance loop reached max iteration.");
        }

        /// camera



        /// camera

        /// <summary>
        /// ヘッダー付きCSVから測定条件を読み込む。
        /// 列順は問わず、ヘッダー名で対応付ける（大文字小文字は区別しない）。
        /// 空行と '#' で始まる行は無視する。
        /// 必要な列: flowInfuseMlMin, flowWithdrawMlMin, timeInfuseMs,
        ///           hvKv, withdrawPosition, flowRateGas, timeInfuseOffset
        /// </summary>
        private List<ConditionPoint> LoadConditionsFromCsv(string path)
        {
            var list = new List<ConditionPoint>();

            if (!File.Exists(path))
                throw new FileNotFoundException("Condition CSV not found: " + Path.GetFullPath(path));

            string[] lines = File.ReadAllLines(path);

            // ヘッダー行を探す
            string[] header = null;
            int dataStart = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i].Trim();
                if (t.Length == 0 || t.StartsWith("#")) continue;
                header = SplitCsvLine(t);
                dataStart = i + 1;
                break;
            }

            if (header == null)
                throw new Exception("CSV header row not found.");

            var col = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < header.Length; i++)
            {
                string name = header[i].Trim();
                if (name.Length > 0 && !col.ContainsKey(name))
                    col[name] = i;
            }

            string[] required =
            {
                "flowInfuseMlMin", "flowWithdrawMlMin", "timeInfuseMs",
                "hvKv", "withdrawPosition", "flowRateGas", "timeInfuseOffset"
            };

            foreach (string r in required)
            {
                if (!col.ContainsKey(r))
                    throw new Exception("Required column missing in CSV: " + r);
            }

            for (int i = dataStart; i < lines.Length; i++)
            {
                string t = lines[i].Trim();
                if (t.Length == 0 || t.StartsWith("#")) continue;

                string[] f = SplitCsvLine(t);

                try
                {
                    double flowInfuse = ParseDouble(f, col, "flowInfuseMlMin");
                    double flowWithdraw = ParseDouble(f, col, "flowWithdrawMlMin");
                    int timeInfuse = ParseInt(f, col, "timeInfuseMs");
                    double hvKv = ParseDouble(f, col, "hvKv");
                    int withdrawPos = ParseInt(f, col, "withdrawPosition");
                    double gas = ParseDouble(f, col, "flowRateGas");
                    int offset = ParseInt(f, col, "timeInfuseOffset");

                    var cp = new ConditionPoint(
                        flowInfuse, flowWithdraw, timeInfuse, hvKv, withdrawPos, gas, offset);

                    // 任意列（列が無い/空セルならデフォルト値のまま）
                    cp.CameraIntervalMs = (int)Math.Round(
                        ParseDoubleOpt(f, col, "cameraIntervalSec", cp.CameraIntervalMs / 1000.0) * 1000.0);
                    cp.SmpsIntervalMs = (int)Math.Round(
                        ParseDoubleOpt(f, col, "smpsIntervalSec", cp.SmpsIntervalMs / 1000.0) * 1000.0);

                    cp.SmpsStartHv = ParseDoubleOpt(f, col, "startHv", cp.SmpsStartHv);
                    cp.SmpsStopHv = ParseDoubleOpt(f, col, "stopHv", cp.SmpsStopHv);
                    cp.SmpsPointsPerDecade = ParseIntOpt(f, col, "pointsPerDecade", cp.SmpsPointsPerDecade);
                    cp.SmpsDwellSec = ParseDoubleOpt(f, col, "dwellTimeSec", cp.SmpsDwellSec);
                    cp.SmpsSettlingSec = ParseDoubleOpt(f, col, "settlingTimeSec", cp.SmpsSettlingSec);

                    cp.Validate();

                    list.Add(cp);
                }
                catch (Exception ex)
                {
                    // 行番号(1始まり)付きで通知して、その行はスキップ
                    AppendLog($"CSV parse error at line {i + 1}: {ex.Message}");
                }
            }

            return list;
        }

        private static string[] SplitCsvLine(string line)
        {
            // 条件CSVは数値のみを想定。カンマ区切りで分割し前後空白を除去する。
            string[] parts = line.Split(',');
            for (int i = 0; i < parts.Length; i++)
                parts[i] = parts[i].Trim();
            return parts;
        }

        private static double ParseDouble(string[] fields, Dictionary<string, int> col, string name)
        {
            int idx = col[name];
            if (idx >= fields.Length)
                throw new Exception("Missing value for column: " + name);
            return double.Parse(fields[idx], NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static int ParseInt(string[] fields, Dictionary<string, int> col, string name)
        {
            int idx = col[name];
            if (idx >= fields.Length)
                throw new Exception("Missing value for column: " + name);
            // 300000 のような整数のほか 3e5 等も許容
            return (int)Math.Round(
                double.Parse(fields[idx], NumberStyles.Float, CultureInfo.InvariantCulture));
        }

        private static double ParseDoubleOpt(string[] fields, Dictionary<string, int> col, string name, double def)
        {
            if (!col.ContainsKey(name)) return def;
            int idx = col[name];
            if (idx >= fields.Length) return def;
            string v = fields[idx];
            if (string.IsNullOrWhiteSpace(v)) return def;
            return double.Parse(v, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static int ParseIntOpt(string[] fields, Dictionary<string, int> col, string name, int def)
        {
            if (!col.ContainsKey(name)) return def;
            int idx = col[name];
            if (idx >= fields.Length) return def;
            string v = fields[idx];
            if (string.IsNullOrWhiteSpace(v)) return def;
            return (int)Math.Round(double.Parse(v, NumberStyles.Float, CultureInfo.InvariantCulture));
        }

        private async void btnStop_Click(object sender, EventArgs e)
        {
            _stopRequested = true;
            try { _sequenceCts?.Cancel(); } catch { }

            AppendLog("Manual stop requested.");

            // 実行中はシーケンス側 finally の SafeStop が後始末する。
            // 非実行時のみここで安全停止（UIスレッドをブロックしないようバックグラウンドで）。
            if (!_sequenceRunning)
            {
                await Task.Run(() => SafeStop());
            }

            AppendLog("Manual stop.");
        }

        private async Task SafeStop()
        {
            try
            {
                if (_hv != null) _hv.OutputOff();
            }
            catch { }

            try
            {
                if (_mfc != null) _mfc.OutputOff();
            }
            catch { }

            try
            {
                if (_pump != null) _pump.Stop();
            }
            catch { }

            try
            {
                if (_aiLogger != null) _aiLogger.Stop();
            }
            catch { }

            /*try
            {
                if (_dino != null) _dino.Stop();
            }
            catch { }*/

            try
            {
                if (_camera != null)
                {
                    await _camera.StopAsync().ConfigureAwait(false);
                }
            }
            catch { }

            try
            {
                if (_smps != null)
                {
                    await _smps.StopAsync().ConfigureAwait(false);
                }
            }
            catch { }
        }

        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            if (_closeReady)
            {
                base.OnFormClosing(e);
                return;
            }

            e.Cancel = true;
            if (_closing) return;
            _closing = true;
            btnStartSequence.Enabled = false;
            btnStop.Enabled = false;

            try { _sequenceCts?.Cancel(); } catch { }

            try
            {
                Task running = _sequenceTask;
                if (running != null)
                    await running;
                else
                    await Task.Run(() => SafeStop());
            }
            catch (Exception ex)
            {
                AppendLog($"Error: {ex.Message}");
            }

            DisposeControllers();
            _closeReady = true;
            Close();
        }

        private void DisposeControllers()
        {
            _pump?.Dispose();
            _hv?.Dispose();
            _mfc?.Dispose();
            _valve?.Dispose();
            _aiLogger?.Dispose();
            _sequenceCts?.Dispose();

            _pump = null;
            _hv = null;
            _mfc = null;
            _valve = null;
            _aiLogger = null;
            _camera = null;
            _smps = null;
            _sequenceCts = null;
        }

        private sealed class DmaScheduleState
        {
            private readonly object _lock = new object();
            private string _status = "Waiting for first scan";
            private int _completedCount;
            private string _commandId;

            public void Set(string status, string commandId)
            {
                lock (_lock)
                {
                    _status = status;
                    _commandId = commandId;
                }
            }

            public void MarkCompleted(string commandId)
            {
                lock (_lock)
                {
                    _completedCount++;
                    _status = "Last DMA scan completed";
                    _commandId = commandId;
                }
            }

            public void Snapshot(out string status, out int completedCount, out string commandId)
            {
                lock (_lock)
                {
                    status = _status;
                    completedCount = _completedCount;
                    commandId = _commandId;
                }
            }
        }
    }

    public class ConditionPoint
    {
        public double FlowRateInfuseMlMin;
        public double FlowRateWithdrawMlMin;
        public int TimeInfuseMs;
        public int TimeInfuseOffsetMs;
        public int TimeWithdrawMs;
        public double InfuseVolumeMl;
        public double WithdrawVolumeMl;

        public double HvKv;
        public int WithdrawPosition;
        public double GasFlowLpm;

        // 記録間隔（infuse区間中）
        public int CameraIntervalMs = 60000;
        public int SmpsIntervalMs = 300000;

        // SMPS スキャン条件（条件ファイルに反映）
        public double SmpsStartHv = 10.0;
        public double SmpsStopHv = 1000.0;
        public int SmpsPointsPerDecade = 5;
        public double SmpsDwellSec = 5.0;
        public double SmpsSettlingSec = 5.0;

        public ConditionPoint
        (
            double flowInfuseMlMin,
            double flowWithdrawMlMin,
            int timeInfuseMs,
            double hvKv,
            int withdrawPosition,
            double flowRateGas,
            int timeInfuseOffset
        )
        {
            FlowRateInfuseMlMin = flowInfuseMlMin;
            FlowRateWithdrawMlMin = flowWithdrawMlMin;
            HvKv = hvKv;
            GasFlowLpm = flowRateGas;
            TimeInfuseMs = timeInfuseMs;
            TimeInfuseOffsetMs = timeInfuseOffset;
            WithdrawPosition = withdrawPosition;
            TimeWithdrawMs = (int)(timeInfuseMs * flowInfuseMlMin / flowWithdrawMlMin * 1.00) + timeInfuseOffset;

            InfuseVolumeMl =
                FlowRateInfuseMlMin * TimeInfuseMs / 60000.0 +
                FlowRateWithdrawMlMin * TimeInfuseOffsetMs / 60000.0;
            WithdrawVolumeMl = FlowRateWithdrawMlMin * (double)(TimeWithdrawMs) / 60000.0;
        }

        public double EstimatedSmpsDurationMs
        {
            get
            {
                double decades = Math.Abs(Math.Log10(SmpsStopHv) - Math.Log10(SmpsStartHv));
                int points = (int)Math.Floor(decades * SmpsPointsPerDecade + 1e-10) + 1;
                double lastLogPoint = Math.Log10(SmpsStartHv) +
                    Math.Sign(Math.Log10(SmpsStopHv) - Math.Log10(SmpsStartHv)) *
                    (points - 1) / (double)SmpsPointsPerDecade;
                if (Math.Abs(Math.Pow(10.0, lastLogPoint) - SmpsStopHv) / SmpsStopHv > 1e-9)
                    points++;
                return points * (SmpsDwellSec + SmpsSettlingSec) * 1000.0;
            }
        }

        public int GetMaxDmaScans(double safetyMarginMs)
        {
            double requiredMs = EstimatedSmpsDurationMs + safetyMarginMs;
            if (TimeInfuseMs < requiredMs) return 0;
            return (int)Math.Floor((TimeInfuseMs - requiredMs) / SmpsIntervalMs) + 1;
        }

        public void Validate()
        {
            RequireFinitePositive(FlowRateInfuseMlMin, nameof(FlowRateInfuseMlMin));
            RequireFinitePositive(FlowRateWithdrawMlMin, nameof(FlowRateWithdrawMlMin));
            RequireFinite(HvKv, nameof(HvKv));
            RequireFinite(GasFlowLpm, nameof(GasFlowLpm));
            RequireFinitePositive(SmpsStartHv, nameof(SmpsStartHv));
            RequireFinitePositive(SmpsStopHv, nameof(SmpsStopHv));
            RequireFinitePositive(SmpsDwellSec, nameof(SmpsDwellSec));
            RequireFinite(SmpsSettlingSec, nameof(SmpsSettlingSec));

            if (TimeInfuseMs <= 0 || TimeInfuseOffsetMs < 0 || TimeWithdrawMs <= 0)
                throw new InvalidOperationException("Pump times must be positive.");
            if (HvKv < 0.0 || HvKv > 20.0)
                throw new InvalidOperationException("HV must be between 0 and 20 kV.");
            if (GasFlowLpm < 0.0 || GasFlowLpm > 5.0)
                throw new InvalidOperationException("Gas flow must be between 0 and 5 LPM.");
            if (WithdrawPosition < 1 || WithdrawPosition > 10)
                throw new InvalidOperationException("Withdraw position must be between 1 and 10.");
            if (CameraIntervalMs <= 0 || SmpsIntervalMs <= 0)
                throw new InvalidOperationException("Recording intervals must be positive.");
            if (SmpsPointsPerDecade <= 0 || SmpsSettlingSec < 0.0)
                throw new InvalidOperationException("SMPS scan settings are invalid.");
            if (SmpsIntervalMs < EstimatedSmpsDurationMs)
                throw new InvalidOperationException(
                    "SMPS interval must not be shorter than the estimated scan duration.");
            if (InfuseVolumeMl > 10.0 + 1e-9 || WithdrawVolumeMl > 10.0 + 1e-9)
                throw new InvalidOperationException(
                    "Calculated pump volume exceeds the configured 10 mL limit.");
        }

        private static void RequireFinite(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new InvalidOperationException(name + " must be finite.");
        }

        private static void RequireFinitePositive(double value, string name)
        {
            RequireFinite(value, name);
            if (value <= 0.0)
                throw new InvalidOperationException(name + " must be positive.");
        }
    }
}

using System;
using System.Collections.Generic;
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
        private HvController _hvDMA;
        private ValveController _valve;
        private BufferedAiLogger _aiLogger;
        private BufferedAiLogger _aiLoggerMFC;
        private CameraRecordEvent _camera;

        private readonly string _logFilePath;
        //private DinoRecorder _dino;
        private MfcController _mfc;
        private bool _stopRequested = false;
        private double _diameterMM = 5.0;
        private int _infusePosition = 5;
        private double _FlowWithdrawMlMin = 1.0;
        private int _timeInfuseOffset = 500;

        public MainForm()
        {
            InitializeComponent();

            string logDir = "./logs";

            Directory.CreateDirectory(logDir);

            _logFilePath = Path.Combine(
                logDir,
                "ESIcontrol_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log"
            );

            try
            {
                // 起動時に自動接続
                _pump = new PumpController("COM7");
                _hv = new HvController("Dev4/ao1", "Dev4/port0/line0");
                _hv.Initialize();
                AppendLog("HV controller initialized.");

                _mfc = new MfcController("Dev3/ao0");
                _mfc.Initialize();
                AppendLog("MFC controller initialized.");

                _valve = new ValveController("COM5");
                _aiLogger = new BufferedAiLogger(
                    new string[] { "Dev4/ai0", "Dev4/ai1", "Dev4/ai3" },
                    100.0,
                    100,
                    "ai_mean_log.csv"
                );
                _aiLogger.OnLog += AppendLog;
                _aiLoggerMFC = new BufferedAiLogger(
                    new string[] { "Dev3/ai0", "Dev3/ai2" },
                    100.0,
                    100,
                    "ai_mean_log_MFC.csv"
                );
                _aiLoggerMFC.OnLog += AppendLog;
                AppendLog("AI logger prepared.");
                //_dino = new DinoRecorder();
                //_dino.OnLog += AppendLog;
                _camera = new CameraRecordEvent("C:/Users/tamadate/Documents/cameraControl/commands");
                _camera.OnLog += AppendLog;

                _pump.Diameter(_diameterMM);

                string ver = _pump.GetVersion();
                AppendLog("Pump connected: " + ver);
                
            }
            catch (Exception ex)
            {
                AppendLog("Initialization error: " + ex.Message);
            }
        }

        private void AppendLog(string msg)
        {
            if (txtLog.InvokeRequired)
            {
                txtLog.Invoke(new Action<string>(AppendLog), msg);
                return;
            }

            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff ") + msg;

            // 画面に表示
            txtLog.AppendText(line + Environment.NewLine);

            // ファイルに保存
            try
            {
                File.AppendAllText(_logFilePath, line + Environment.NewLine);
            }
            catch
            {
                // ログ保存失敗で実験シーケンスを止めない
            }
        }

        private async void btnStartSequence_Click(object sender, EventArgs e)
        {
            _stopRequested = false;

            int TimeForSample = 300000;
            try
            {
                List<ConditionPoint> conditions = new List<ConditionPoint>();
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 1.0, 1, 0.5, _timeInfuseOffset));
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 1.1, 1, 0.5, _timeInfuseOffset));
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 1.2, 1, 0.5, _timeInfuseOffset));
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 1.3, 1, 0.5, _timeInfuseOffset));
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 1.4, 1, 0.5, _timeInfuseOffset));
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 1.5, 1, 0.5, _timeInfuseOffset));
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 1.6, 1, 0.5, _timeInfuseOffset));
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 1.7, 1, 0.5, _timeInfuseOffset));
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 1.8, 1, 0.5, _timeInfuseOffset));
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 1.9, 1, 0.5, _timeInfuseOffset));

                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 2.0, 1, 0.5, _timeInfuseOffset));
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 2.1, 1, 0.5, _timeInfuseOffset));
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 2.2, 1, 0.5, _timeInfuseOffset));
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 2.3, 1, 0.5, _timeInfuseOffset));
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 2.4, 1, 0.5, _timeInfuseOffset));
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 2.5, 1, 0.5, _timeInfuseOffset));
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 2.6, 1, 0.5, _timeInfuseOffset));
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 2.7, 1, 0.5, _timeInfuseOffset));
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 2.8, 1, 0.5, _timeInfuseOffset));
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 2.9, 1, 0.5, _timeInfuseOffset));

                /*conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 3.0, 1, 0.5, _timeInfuseOffset));
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 3.1, 1, 0.5, _timeInfuseOffset));
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 3.2, 1, 0.5, _timeInfuseOffset));
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 3.3, 1, 0.5, _timeInfuseOffset));
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 3.4, 1, 0.5, _timeInfuseOffset));
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 3.5, 1, 0.5, _timeInfuseOffset));
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 3.6, 1, 0.5, _timeInfuseOffset));
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 3.7, 1, 0.5, _timeInfuseOffset));
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 3.8, 1, 0.5, _timeInfuseOffset));
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 3.9, 1, 0.5, _timeInfuseOffset));*/

                /*conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 4.0, 1, 1, _timeInfuseOffset));
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 4.1, 1, 1, _timeInfuseOffset));
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 4.2, 1, 1, _timeInfuseOffset));
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 4.3, 1, 1, _timeInfuseOffset));
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 4.4, 1, 1, _timeInfuseOffset));
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 4.5, 1, 1, _timeInfuseOffset));
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 4.6, 1, 1, _timeInfuseOffset));
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 4.7, 1, 1, _timeInfuseOffset));
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 4.8, 1, 1, _timeInfuseOffset));
                conditions.Add(new ConditionPoint(0.001, _FlowWithdrawMlMin, TimeForSample, 4.9, 1, 1, _timeInfuseOffset));*/
                //conditions.Add(new ConditionPoint(0.05, 3.0, 3000, 30000, 2, _timeWithdraw));
                //conditions.Add(new ConditionPoint(0.1, 2.0, 3000, 30000, 3, _timeWithdraw));
                //                double flowInfuseMlMin, double flowWithdrawMlMin, int timeInfuseMs, double hvKv,
                //                    int withdrawPosition, double flowRateGas, int timeInfuseOffset

                if (!_aiLogger.IsRunning)
                {
                    _aiLogger.Start();
                }
                if (!_aiLoggerMFC.IsRunning)
                {
                    _aiLoggerMFC.Start();
                }
                /*if (!_dino.IsRunning)
                {
                    _dino.Start();
                }*/

                foreach (ConditionPoint c in conditions)
                {
                    if (_stopRequested) break;

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
                    await Task.Delay(c.TimeWithdrawMs);
                    _pump.Stop();
                    _pump.Stop();
                    AppendLog("Withdraw stopped.");


                    _mfc.SetFlowLpm(c.GasFlowLpm);
                    AppendLog("MFC set to " + c.GasFlowLpm.ToString("F2") + " LPM");

                    _hv.SetVoltageKv(c.HvKv);           // AO = 2.5 V
                    _hv.EnableOutput();                 // DO0 = Low
                    AppendLog($"HV enabled at {c.HvKv:F3} kV");

                    // 高圧印加
                    //_hv.SetVoltageKv(c.HvKv);
                    //AppendLog("HV applied.");

                    // Infuse
                    _pump.ClearInfuseDispensed();
                    _pump.SetDirectionInfuse();
                    _valve.Change(_infusePosition);
                    _pump.SetRate(c.FlowRateWithdrawMlMin, "MM");
                    _pump.Run();
                    await Task.Delay(_timeInfuseOffset);
                    _pump.Stop();
                    _pump.Stop();

                    _camera.Start();

                    _pump.SetRate(c.FlowRateInfuseMlMin, "MM");
                    _pump.Run();
                    AppendLog("Infuse started.");
                    await Task.Delay(c.TimeInfuseMs);
                    _pump.Stop();
                    _pump.Stop();
                    AppendLog("Infuse stopped.");

                    // 条件終了
                    _hv.OutputOff();
                    AppendLog("HV off");

                    _mfc.OutputOff();
                    AppendLog("MFC off");


                    await Task.Delay(1000);

                    //await BalanceWithdrawToZeroAsync(0.2, 0.005, AppendLog);
                }


                /*if (_dino != null)
                {
                    _dino.Stop();
                }*/
                if (_camera != null)
                {
                    await _camera.StopAsync();
                }
                if (_aiLogger != null)
                {
                    _aiLogger.Stop();
                }
                if (_aiLoggerMFC != null)
                {
                    _aiLoggerMFC.Stop();
                }

                AppendLog("Sequence finished.");
            }
            catch (Exception ex)
            {
                AppendLog("Sequence error: " + ex.Message);
                await SafeStop();
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

        private async void btnStop_Click(object sender, EventArgs e)
        {
            _stopRequested = true;
            await SafeStop();
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
                if (_pump != null) _pump.Stop();
            }
            catch { }

            try
            {
                if (_aiLogger != null) _aiLogger.Stop();
            }
            catch { }

            try
            {
                if (_aiLoggerMFC != null) _aiLoggerMFC.Stop();
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
                    await _camera.StopAsync();
                }
            }
            catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                SafeStop().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                AppendLog($"Error: {ex.Message}");
            }

            _pump?.Dispose();
            _hv?.Dispose();

            base.OnFormClosing(e);
        }
    }

    public class ConditionPoint
    {
        public double   FlowRateInfuseMlMin;
        public double   FlowRateWithdrawMlMin;
        public int      TimeInfuseMs;
        public int      TimeWithdrawMs;
        public double   InfuseVolumeMl;
        public double   WithdrawVolumeMl;

        public double   HvKv;
        public int      WithdrawPosition;
        public double   GasFlowLpm;

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
            WithdrawPosition = withdrawPosition;
            TimeWithdrawMs = (int)(timeInfuseMs * flowInfuseMlMin / flowWithdrawMlMin * 1.00) + timeInfuseOffset;
                        
            InfuseVolumeMl = FlowRateInfuseMlMin * (double)(TimeInfuseMs) / 60000.0;
            WithdrawVolumeMl = FlowRateWithdrawMlMin * (double)(TimeWithdrawMs) / 60000.0;
        }
    }
}
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using NationalInstruments.DAQmx;

namespace ESIcontrol
{
    public class BufferedAiLogger : IDisposable
    {
        private readonly string[] _physicalChannels;
        private readonly double _sampleRateHz;
        private readonly int _samplesPerRead;
        private readonly string _csvPath;

        private System.Threading.Tasks.Task _worker;
        private CancellationTokenSource _cts;
        private bool _isRunning;

        public event Action<string> OnLog;

        public BufferedAiLogger(string[] physicalChannels, double sampleRateHz, int samplesPerRead, string csvPath)
        {
            _physicalChannels = physicalChannels;
            _sampleRateHz = sampleRateHz;
            _samplesPerRead = samplesPerRead;
            _csvPath = csvPath;
        }

        public bool IsRunning
        {
            get { return _isRunning; }
        }

        public void Start()
        {
            if (_isRunning) return;

            _cts = new CancellationTokenSource();
            _worker = System.Threading.Tasks.Task.Run(() => WorkerLoop(_cts.Token));
            _isRunning = true;
        }

        public void Stop()
        {
            if (!_isRunning) return;

            _cts.Cancel();

            try
            {
                _worker.Wait();
            }
            catch
            {
            }

            _cts.Dispose();
            _cts = null;
            _worker = null;
            _isRunning = false;
        }

        private void WorkerLoop(CancellationToken token)
        {
            using (NationalInstruments.DAQmx.Task aiTask = new NationalInstruments.DAQmx.Task())
            {
                foreach (string ch in _physicalChannels)
                {
                    aiTask.AIChannels.CreateVoltageChannel(
                        ch,
                        "",
                        AITerminalConfiguration.Differential,   // 必要なら Differential に変更
                        -10.0,
                        10.0,
                        AIVoltageUnits.Volts
                    );
                }

                aiTask.Timing.ConfigureSampleClock(
                    "",
                    _sampleRateHz,
                    SampleClockActiveEdge.Rising,
                    SampleQuantityMode.ContinuousSamples,
                    _samplesPerRead 
                );

                AnalogMultiChannelReader reader = new AnalogMultiChannelReader(aiTask.Stream);

                // CSVヘッダ作成
                if (!File.Exists(_csvPath))
                {
                    string header = "Timestamp";
                    for (int i = 0; i < _physicalChannels.Length; i++)
                    {
                        header += ",Mean_Ch" + i + "[V]";
                    }
                    header += ",N\r\n";

                    File.WriteAllText(_csvPath, header);
                }

                aiTask.Start();
                OnLog?.Invoke("Multi-channel AI task started.");

                while (!token.IsCancellationRequested)
                {
                    // data[channelIndex, sampleIndex]
                    double[,] data = reader.ReadMultiSample(_samplesPerRead);

                    double[] means = new double[_physicalChannels.Length];

                    for (int ch = 0; ch < _physicalChannels.Length; ch++)
                    {
                        double sum = 0.0;
                        for (int i = 0; i < _samplesPerRead; i++)
                        {
                            sum += data[ch, i];
                        }
                        means[ch] = sum / _samplesPerRead;
                    }

                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

                    for (int ch = 0; ch < means.Length; ch++)
                    {
                        line += "," + means[ch].ToString("F6", CultureInfo.InvariantCulture);
                    }

                    line += "," + _samplesPerRead.ToString();

                    File.AppendAllText(_csvPath, line + "\r\n");

                    /*string logMsg = "AI mean:";
                    for (int ch = 0; ch < means.Length; ch++)
                    {
                        logMsg += " ch" + ch + "=" + means[ch].ToString("F4", CultureInfo.InvariantCulture) + " V";
                    }
                    OnLog?.Invoke(logMsg);*/
                }

                aiTask.Stop();
                OnLog?.Invoke("Multi-channel AI task stopped.");
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
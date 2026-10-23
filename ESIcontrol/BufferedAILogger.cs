using System;
using System.Globalization;
using System.IO;
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
        private readonly object _lifecycleLock = new object();

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
            get { lock (_lifecycleLock) return _isRunning; }
        }

        public void Start()
        {
            lock (_lifecycleLock)
            {
                if (_isRunning) return;

                _cts = new CancellationTokenSource();
                _worker = System.Threading.Tasks.Task.Run(() => WorkerLoop(_cts.Token));
                _isRunning = true;
            }
        }

        public void Stop()
        {
            CancellationTokenSource cts;
            System.Threading.Tasks.Task worker;

            lock (_lifecycleLock)
            {
                if (!_isRunning) return;
                _isRunning = false;

                cts = _cts;
                worker = _worker;
                _cts = null;
                _worker = null;
            }

            try
            {
                cts.Cancel();
            }
            catch
            {
            }

            try
            {
                worker?.Wait();
            }
            catch
            {
            }

            cts.Dispose();
        }

        private void WorkerLoop(CancellationToken token)
        {
            try
            {
                using (NationalInstruments.DAQmx.Task aiTask = new NationalInstruments.DAQmx.Task())
                {
                    foreach (string ch in _physicalChannels)
                    {
                        aiTask.AIChannels.CreateVoltageChannel(
                            ch,
                            "",
                            AITerminalConfiguration.Rse,   // 必要なら Differential に変更
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

                    bool needsHeader = !File.Exists(_csvPath) || new FileInfo(_csvPath).Length == 0;
                    using (var csvWriter = new StreamWriter(_csvPath, true))
                    {
                        csvWriter.AutoFlush = true;
                        if (needsHeader)
                        {
                            string header = "Timestamp";
                            for (int i = 0; i < _physicalChannels.Length; i++)
                                header += ",Mean_Ch" + i + "[V]";
                            csvWriter.WriteLine(header + ",N");
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
                                    sum += data[ch, i];
                                means[ch] = sum / _samplesPerRead;
                            }

                            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

                            for (int ch = 0; ch < means.Length; ch++)
                                line += "," + means[ch].ToString("F6", CultureInfo.InvariantCulture);

                            line += "," + _samplesPerRead.ToString();

                            csvWriter.WriteLine(line);
                        }

                        aiTask.Stop();
                        OnLog?.Invoke("Multi-channel AI task stopped.");
                    }
                }
            }
            catch (Exception ex)
            {
                // 取得スレッドの例外を握りつぶさず通知する
                OnLog?.Invoke("AI logger worker error: " + ex.Message);
            }
            finally
            {
                lock (_lifecycleLock)
                {
                    // Stop() 後に別ワーカーが開始されている場合は、その状態を壊さない。
                    if (_worker == null || _worker.Id == System.Threading.Tasks.Task.CurrentId)
                        _isRunning = false;
                }
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}

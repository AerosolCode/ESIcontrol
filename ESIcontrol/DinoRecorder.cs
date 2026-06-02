using OpenCvSharp;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ESIcontrol
{
    public class DinoRecorder
    {
        private VideoCapture _cap;
        private bool _running;

        public event Action<string> OnLog;

        public bool IsRunning
        {
            get { return _running; }
        }

        public void Start()
        {
            if (_running) return;

            _running = true;

            Task.Run(() => Worker());
        }

        public void Stop()
        {
            _running = false;
        }

        private void Worker()
        {
            _cap = new VideoCapture(0);

            if (!_cap.IsOpened())
            {
                OnLog?.Invoke("Dino camera open failed.");
                return;
            }

            _cap.Set(VideoCaptureProperties.FrameWidth, 1280);
            _cap.Set(VideoCaptureProperties.FrameHeight, 720);
            _cap.Set(VideoCaptureProperties.Fps, 30);

            int width = (int)_cap.FrameWidth;
            int height = (int)_cap.FrameHeight;

            double fps = _cap.Fps;
            if (fps <= 0) fps = 30;

            Directory.CreateDirectory("videos");

            OnLog?.Invoke("Dino recorder started.");

            while (_running)
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string filename = "videos\\dino_" + timestamp + ".avi";

                // FOURCC作成
                int fourcc = FourCC.XVID;

                VideoWriter writer = new VideoWriter(
                    filename,
                    fourcc,
                    fps,
                    new OpenCvSharp.Size(width, height)
                );

                if (!writer.IsOpened())
                {
                    OnLog?.Invoke("VideoWriter open failed.");
                    return;
                }

                OnLog?.Invoke("Recording: " + filename);

                Stopwatch sw = Stopwatch.StartNew();

                Mat frame = new Mat();

                while (sw.Elapsed.TotalSeconds < 10 && _running)
                {
                    if (_cap.Read(frame))
                    {
                        writer.Write(frame);
                    }
                }

                frame.Dispose();
                writer.Release();
                writer.Dispose();

                OnLog?.Invoke("Recording finished.");

                // 110秒待機
                for (int i = 0; i < 50 && _running; i++)
                {
                    Thread.Sleep(1000);
                }
            }

            _cap.Release();
            _cap.Dispose();

            OnLog?.Invoke("Dino recorder stopped.");
        }
    }
}
using System;
using System.Collections.Specialized;
using System.IO.Ports;
using System.Threading;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ESIcontrol
{
    public class PumpController : IDisposable
    {
        private SerialPort _port;

        public class DispensedVolume
        {
            public double InfuseMl { get; set; }
            public double WithdrawMl { get; set; }
            public string Units { get; set; }
            public string Raw { get; set; }
        }

        public DispensedVolume GetDispensed()
        {
            string s = SendAndRead("DIS");

            if (string.IsNullOrWhiteSpace(s))
                throw new Exception("DIS returned empty response.");

            // 例:
            // 00PI0.050W0.050ML
            // 00SI0.000W0.120UL
            // 先頭の 00P / 00S などが付いていても拾えるようにする
            Match m = Regex.Match(
                s,
                @"I([+-]?\d+(?:\.\d+)?)W([+-]?\d+(?:\.\d+)?)(ML|UL)",
                RegexOptions.IgnoreCase);

            if (!m.Success)
                throw new Exception("Failed to parse DIS response: [" + s + "]");

            DispensedVolume d = new DispensedVolume();
            d.InfuseMl = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            d.WithdrawMl = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            d.Units = m.Groups[3].Value.ToUpperInvariant();
            d.Raw = s;

            if (d.Units == "UL")
            {
                d.InfuseMl /= 1000.0;
                d.WithdrawMl /= 1000.0;
                d.Units = "ML";
            }

            return d;
        }

        public PumpController(string comPort)
        {
            _port = new SerialPort(comPort, 19200, Parity.None, 8, StopBits.One);
            _port.Handshake = Handshake.None;
            _port.ReadTimeout = 1000;
            _port.WriteTimeout = 1000;
            _port.Open();
            //Send("CLD WDR");
        }

        public void SetRate(double valueMlMin, string Unit)
        {
            if(Unit == "UM")
            {
                Send(string.Format("RAT {0:0.###} UM", valueMlMin * 1000));
            }
            else
            {
                Send(string.Format("RAT {0:0.###} MM", valueMlMin));
            }
        }

        public void Run()
        {
            Send("RUN");
        }

        public void SetVolumeMl(double volumeMl)
        {
            Send(string.Format("VOL {0:0.###}", volumeMl));
        }

        public void ClearWithdrawDispensed()
        {
            Send("CLD WDR");
        }

        public void ClearInfuseDispensed()
        {
            Send("CLD INF");
        }

        public void Stop()
        {
            Send("STP");
        }

        public void Diameter(double valueMM)
        {
            Send(string.Format("DIA {0:0.###}", valueMM));
        }

        public void SetDirectionWithdraw()
        {
            Send("DIR WDR");
        }

        public void SetDirectionInfuse()
        {
            Send("DIR INF");
        }

        public string GetVersion()
        {
            String s = SendAndRead("DIS");
            return s;
        }

        private void Send(string command)
        {
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();
            _port.Write(command + "\r");
            Thread.Sleep(100);
        }

        private string SendAndRead(string command)
        {
            if (_port == null || !_port.IsOpen)
                throw new InvalidOperationException("Pump is not connected.");

            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();

            _port.Write(command + "\r");
            Thread.Sleep(300);

            return _port.ReadExisting().Trim();
        }

        public void Dispose()
        {
            if (_port != null)
            {
                if (_port.IsOpen) _port.Close();
                _port.Dispose();
                _port = null;
            }
        }
    }
}
using System;
using System.IO.Ports;
using System.Threading;

namespace ESIcontrol
{
    public class ValveController : IDisposable
    {
        private SerialPort _port;

        public ValveController(string comPort)
        {
            _port = new SerialPort(comPort, 9600, Parity.None, 8, StopBits.One);
            _port.Handshake = Handshake.None;
            _port.ReadTimeout = 1000;
            _port.WriteTimeout = 1000;
            _port.Open();
            Initialize();
        }

        public void Initialize()
        {
            Send(";01,I");
        }

        public void Change(int position)
        {
            Send($";01,C0,{position:00}");
        }

        private void Send(string command)
        {
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();
            _port.Write(command + "\r\n");
            Thread.Sleep(3000);
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
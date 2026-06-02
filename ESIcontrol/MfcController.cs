using System;
using NationalInstruments.DAQmx;

namespace ESIcontrol
{
    public class MfcController : IDisposable
    {
        private readonly string _aoChannel;

        private Task _aoTask;
        private AnalogSingleChannelWriter _aoWriter;

        // 0-5 V -> 0-5 LPM
        private const double MaxFlowLpm = 5.0;
        private const double MaxAoVolt = 5.0;
        private const double VoltPerLpm = 1.0;   // 1 V / LPM

        public MfcController(string aoChannel)
        {
            _aoChannel = aoChannel;
        }

        public void Initialize()
        {
            _aoTask = new Task();
            _aoTask.AOChannels.CreateVoltageChannel(
                _aoChannel,
                "",
                0.0,
                5.0,
                AOVoltageUnits.Volts
            );

            _aoWriter = new AnalogSingleChannelWriter(_aoTask.Stream);

            // 初期値 0 LPM
            _aoWriter.WriteSingleSample(true, 0.0);
        }

        public void SetFlowLpm(double flowLpm)
        {
            if (_aoWriter == null)
                throw new InvalidOperationException("MFC controller is not initialized.");

            if (flowLpm < 0.0) flowLpm = 0.0;
            if (flowLpm > MaxFlowLpm) flowLpm = MaxFlowLpm;

            double aoVolt = flowLpm * VoltPerLpm;

            if (aoVolt < 0.0) aoVolt = 0.0;
            if (aoVolt > MaxAoVolt) aoVolt = MaxAoVolt;

            _aoWriter.WriteSingleSample(true, aoVolt);
        }

        public double FlowToVoltage(double flowLpm)
        {
            if (flowLpm < 0.0) flowLpm = 0.0;
            if (flowLpm > MaxFlowLpm) flowLpm = MaxFlowLpm;

            return flowLpm * VoltPerLpm;
        }

        public double VoltageToFlow(double voltage)
        {
            if (voltage < 0.0) voltage = 0.0;
            if (voltage > MaxAoVolt) voltage = MaxAoVolt;

            return voltage / VoltPerLpm;
        }

        public void OutputOff()
        {
            if (_aoWriter != null)
            {
                _aoWriter.WriteSingleSample(true, 0.0);
            }
        }

        public void Dispose()
        {
            try
            {
                OutputOff();
            }
            catch
            {
            }

            if (_aoTask != null)
            {
                _aoTask.Dispose();
                _aoTask = null;
            }
        }
    }
}
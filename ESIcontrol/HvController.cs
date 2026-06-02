using System;
using NationalInstruments.DAQmx;
using DaqTask = NationalInstruments.DAQmx.Task;

namespace ESIcontrol
{
    public class HvController : IDisposable
    {
        private readonly string _aoChannel;
        private readonly string _doLine;

        private DaqTask _aoTask;
        private DaqTask _doTask;
        private AnalogSingleChannelWriter _aoWriter;
        private DigitalSingleChannelWriter _doWriter;

        // 10 V -> 20 kV
        private const double KvPerVolt = 2.0;
        private const double MaxKv = 20.0;
        private const double MaxAoVolt = 10.0;

        public HvController(string aoChannel, string doLine)
        {
            _aoChannel = aoChannel;
            _doLine = doLine;
        }

        public void Initialize()
        {
            _aoTask = new DaqTask();
            _aoTask.AOChannels.CreateVoltageChannel(
                _aoChannel,
                "",
                0.0,
                10.0,
                AOVoltageUnits.Volts
            );
            _aoWriter = new AnalogSingleChannelWriter(_aoTask.Stream);

            _doTask = new DaqTask();
            _doTask.DOChannels.CreateChannel(
                _doLine,
                "",
                ChannelLineGrouping.OneChannelForEachLine
            );
            _doWriter = new DigitalSingleChannelWriter(_doTask.Stream);

            // 初期状態: 安全側
            // AO = 0V, DO = High (disable)
            _aoWriter.WriteSingleSample(true, 0.0);
            _doWriter.WriteSingleSampleSingleLine(true, true);
        }

        public void SetVoltageKv(double kv)
        {
            if (_aoWriter == null)
                throw new InvalidOperationException("HV controller is not initialized.");

            if (kv < 0.0) kv = 0.0;
            if (kv > MaxKv) kv = MaxKv;

            double aoVolt = kv / KvPerVolt;

            if (aoVolt < 0.0) aoVolt = 0.0;
            if (aoVolt > MaxAoVolt) aoVolt = MaxAoVolt;

            _aoWriter.WriteSingleSample(true, aoVolt);
        }

        // Active Low enable
        public void EnableOutput()
        {
            if (_doWriter == null)
                throw new InvalidOperationException("HV controller is not initialized.");

            _doWriter.WriteSingleSampleSingleLine(true, false); // Low = ON
        }

        // Active Low disable
        public void DisableOutput()
        {
            if (_doWriter == null)
                throw new InvalidOperationException("HV controller is not initialized.");

            _doWriter.WriteSingleSampleSingleLine(true, true); // High = OFF
        }

        public void OutputOff()
        {
            if (_aoWriter != null)
            {
                _aoWriter.WriteSingleSample(true, 0.0);
            }

            if (_doWriter != null)
            {
                _doWriter.WriteSingleSampleSingleLine(true, true); // disable
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

            if (_doTask != null)
            {
                _doTask.Dispose();
                _doTask = null;
            }
        }
    }
}
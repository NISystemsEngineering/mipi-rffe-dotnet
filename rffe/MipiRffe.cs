using NationalInstruments.ModularInstruments.NIDigital;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

namespace NationalInstruments.ApplicationsEngineering.Mipi
{
    public class RffeException : Exception
    {
        public RffeException(string message) : base(message) { }
    }

    public class RffeRequiredOverrideException : RffeException
    {
        public RffeRequiredOverrideException(object sender, object func) : base($"Developer error. Override required for {func} by {sender}.") { }
    }

    public class RffeOutOfRangeException : RffeException
    {
        public RffeOutOfRangeException(string parameter, string lowerLimit, string upperLimit, string asFound) :
            base($"{parameter} out of range. Expected [{lowerLimit}, {upperLimit}] but found {asFound}.") { }
    }

    public class MipiRffe
    {
        private readonly NIDigital session;
        private readonly int busNumber = 0;

        public MipiRffe(NIDigital session)
        {
            this.session = session;
        }

        public MipiRffe(NIDigital session, int busNumber) : this(session)
        {
            this.busNumber = busNumber;
        }

        public static void LoadDigitalProject(NIDigital session)
        {
            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            string assemblyDirectory = Path.GetDirectoryName(assemblyPath);
            string digitalProjectDirectory = Path.Combine(assemblyDirectory, "digiproj");
            string pinMapPath = Path.Combine(digitalProjectDirectory, "PinMap.pinmap");
            session.LoadPinMap(pinMapPath);
            session.LoadSpecifications(Path.Combine(digitalProjectDirectory, "Specifications.specs"));
            string levelsPath = Path.Combine(digitalProjectDirectory, "PinLevels.digilevels");
            session.LoadLevels(levelsPath);
            string timingPath = Path.Combine(digitalProjectDirectory, "Timing.digitiming");
            session.LoadTiming(timingPath);
            session.ApplyLevelsAndTiming("", levelsPath, timingPath);
            string[] digitalPatternPaths = Directory.GetFiles(digitalProjectDirectory, "*.digipat");
            foreach (string path in digitalPatternPaths)
                session.LoadPattern(path);
        }

        public void EnableVIO()
        {
            string channelList = busNumber < 0 ? "RFFEVIO" : $"site{busNumber}/RFFEVIO";
            var pinSet = session.PinAndChannelMap.GetPinSet(channelList);
            pinSet.SelectedFunction = SelectedFunction.Ppmu;
            pinSet.Ppmu.OutputFunction = PpmuOutputFunction.DCVoltage;
            pinSet.Ppmu.DCVoltage.VoltageLevel = 1.8;
            pinSet.Ppmu.DCVoltage.CurrentLimitRange = 0.032;
            pinSet.Ppmu.Source();
        }

        public void DisableVIO()
        {
            string channelList = busNumber < 0 ? "RFFEVIO" : $"site{busNumber}/RFFEVIO";
            session.PinAndChannelMap.GetPinSet(channelList).SelectedFunction = SelectedFunction.Off;
        }

        public void ExtendedRegisterWrite(byte slaveAddress, ushort registerAddress, byte[] registerData)
        {
            RffeCommand command = new RffeExtendedRegisterWriteCommand(slaveAddress, registerAddress, registerData);
            command.Burst(session, busNumber);
        }

        public byte[] ExtendedRegisterRead(byte slaveAddress, ushort registerAddress, int byteCount)
        {
            RffeExtendedCommand command = new RffeExtendedRegisterReadCommand(slaveAddress, registerAddress, byteCount);
            command.Burst(session, busNumber);
            return command.RegisterData;
        }

        public void Burst(RffeCommand command)
        {
            command.Burst(session, busNumber);
        }

        public void Burst(IEnumerable<RffeCommand> commands)
        {
            foreach (var command in commands)
                Burst(command);
        }
    }
    
    public abstract class RffeCommand
    {
        public virtual string Name {
            get { throw new RffeRequiredOverrideException(this, typeof(RffeCommand).GetProperty("Name")); }
        }
        public readonly string alias;
        protected const string pin = "RFFEDATA";

        public readonly byte slaveAddress;
        public const int slaveAddressFieldWidth = 4;

        public readonly ushort registerAddress;
        public virtual int RegisterAddressFieldWidth { 
            get { throw new RffeRequiredOverrideException(this, typeof(RffeCommand).GetProperty("RegisterAddressFieldWidth")); }
        }
        public int RegisterAddressLimit
        {
            get { return (1 << RegisterAddressFieldWidth) - 1; }
        }

        public virtual byte Command { 
            get { throw new RffeRequiredOverrideException(this, typeof(RffeCommand).GetProperty("Command")); }
        }

        public virtual int CommandFieldWidth { 
            get { throw new RffeRequiredOverrideException(this, typeof(RffeCommand).GetProperty("CommandFieldWidth")); }
        }

        public byte[] CommandBits
        {
            get { return Command.ToBits(CommandFieldWidth); }
        }

        public RffeCommand(byte slaveAddress, ushort registerAddress, string alias = "")
        {
            this.alias = alias;
            this.slaveAddress = slaveAddress;
            this.registerAddress = registerAddress;
            DataCheck();
        }

        public virtual void Burst(NIDigital session, int busNumber, double timeout = 10.0)
        {
            CreateWaveforms(session);
            uint[] sourceWaveform = BuildSourceWaveform();
            WriteSourceWaveform(session, sourceWaveform);
            string siteList = FormatSiteList(busNumber);
            session.PatternControl.BurstPattern(siteList, Name, true, TimeSpan.FromSeconds(timeout));
        }

        protected static string FormatSiteList(int busNumber)
        {
            return busNumber < 0 ? "" : "site" + busNumber;
        }

        protected virtual void DataCheck()
        {
            if (slaveAddress > 0xF)
                throw new RffeOutOfRangeException("Slave address", "0x0", "0xF", string.Format("0x{0:X2}", slaveAddress));
            if (registerAddress > RegisterAddressLimit)
                throw new RffeOutOfRangeException("Register address", "0x00", string.Format("0x{0:X2}", RegisterAddressLimit),
                    string.Format("0x{0:X2}", registerAddress));
        }

        protected virtual void CreateWaveforms(NIDigital session)
        {
            var pinSet = session.PinAndChannelMap.GetPinSet(pin);
            session.SourceWaveforms.CreateSerial(pinSet, Name, SourceDataMapping.Broadcast, 1, BitOrder.MostSignificantBitFirst);
        }

        protected virtual uint[] BuildSourceWaveform()
        {
            byte[] commandFrame = BuildCommandFrame();
            byte[] addressFrame = BuildAddressFrame();
            byte[] dataFrame = BuildDataFrame();
            var sourceWaveform = commandFrame.Concat(addressFrame).Concat(dataFrame);
            return sourceWaveform.Select(num => { return (uint)num; }).ToArray();
        }

        protected virtual void WriteSourceWaveform(NIDigital session, uint[] waveformData)
        {
            session.SourceWaveforms.WriteBroadcast(Name, waveformData);
        }

        protected virtual byte[] BuildCommandFrame()
        {
            return new byte[] { };
        }

        protected virtual byte[] BuildAddressFrame()
        {
            return new byte[] { };
        }

        protected virtual byte[] BuildDataFrame()
        {
            return new byte[] { };
        }
    }

    public abstract class RffeExtendedCommand : RffeCommand
    {
        public override int RegisterAddressFieldWidth => 8;
        public override int CommandFieldWidth => 4;
        protected byte[] _registerData;
        public byte[] RegisterData
        {
            get { return (byte[])_registerData.Clone(); }
        }
        public virtual int ByteCount {
            get { throw new RffeRequiredOverrideException(this, typeof(RffeExtendedCommand).GetProperty("ByteCount")); }
        }
        public int ByteCountFieldWidth
        {
            get { return 8 - CommandFieldWidth; }
        }
        public int ByteCountLimit
        {
            get { return 1 << ByteCountFieldWidth; }
        }

        public RffeExtendedCommand(byte slaveAddress, ushort registerAddress, byte[] registerData, string alias = "") :
            base(slaveAddress, registerAddress, alias)
        {
            _registerData = (byte[])registerData.Clone();
        }

        protected override void DataCheck()
        {
            base.DataCheck();
            if (!(ByteCount > 0 && ByteCount <= ByteCountLimit))
                throw new RffeOutOfRangeException("Byte count", "1", ByteCountLimit.ToString(), ByteCount.ToString());
        }

        protected override byte[] BuildCommandFrame()
        {
            byte[] slaveAddressBits = slaveAddress.ToBits(4);
            byte[] byteCountBits = ((byte)(ByteCount - 1)).ToBits(ByteCountFieldWidth);
            byte parityBit = Parity.CalculateOddParityBit(slaveAddressBits.Concat(CommandBits).Concat(byteCountBits));
            return slaveAddressBits.Concat(byteCountBits).Concat(new byte[] { parityBit }).ToArray();
        }

        protected override byte[] BuildAddressFrame()
        {
            int numBytes = RegisterAddressFieldWidth >> 3;
            byte[] addressFrame = new byte[RegisterAddressFieldWidth + numBytes];
            for (int i = 0; i < numBytes; i++)
            {
                int shiftAmount = (numBytes - 1 - i) << 3;
                byte shiftedRegisterAddress = (byte)(registerAddress >> shiftAmount);
                byte[] shiftedRegisterAddressBits = shiftedRegisterAddress.ToBits(8);
                int offset = i << 3 + i;
                Array.Copy(shiftedRegisterAddressBits, 0, addressFrame, offset, 8);
                addressFrame[offset + 8] = Parity.CalculateOddParityBit(shiftedRegisterAddressBits);
            }
            return addressFrame;
        }

        protected override void WriteSourceWaveform(NIDigital session, uint[] waveformData)
        {
            base.WriteSourceWaveform(session, waveformData);
            session.PatternControl.WriteSequencerRegister("reg0", ByteCount);
        }
    }

    public class RffeExtendedRegisterWriteCommand : RffeExtendedCommand
    {
        public override string Name => "RegWriteExt";
        public override byte Command => 0b0000;
        public override int ByteCount => RegisterData.Length;

        public RffeExtendedRegisterWriteCommand(byte slaveAddress, ushort registerAddress, byte[] writeData, string alias = "") :
            base(slaveAddress, registerAddress, writeData, alias) { }

        protected override byte[] BuildDataFrame()
        {
            byte[] dataFrame = new byte[ByteCount * 9];
            for (int i = 0; i < RegisterData.Length; i++)
            {
                byte[] dataBits = RegisterData[i].ToBits(8);
                int offset = i << 3 + i;
                Array.Copy(dataBits, 0, dataFrame, offset, 8);
                dataFrame[offset + 8] = Parity.CalculateOddParityBit(dataBits);
            }
            return dataFrame;
        }
    }

    public class RffeExtendedRegisterReadCommand : RffeExtendedCommand
    {
        public override string Name => "RegReadExt";
        public override byte Command => 0b0010;
        private readonly int _byteCount;
        public override int ByteCount => _byteCount;

        public RffeExtendedRegisterReadCommand(byte slaveAddress, ushort registerAddress, int byteCount, string alias=""):
            base(slaveAddress, registerAddress, new byte[] { }, alias)
        {
            _byteCount = byteCount;
        }

        protected override void CreateWaveforms(NIDigital session)
        {
            base.CreateWaveforms(session);
            var pinSet = session.PinAndChannelMap.GetPinSet(pin);
            session.CaptureWaveforms.CreateSerial(pinSet, Name, 8, BitOrder.MostSignificantBitFirst);
        }

        public override void Burst(NIDigital session, int busNumber, double timeout = 10.0)
        {
            base.Burst(session, busNumber, timeout);
            string siteList = FormatSiteList(busNumber);
            uint[][] captureData = null;
            captureData = session.CaptureWaveforms.Fetch(siteList, Name, ByteCount, TimeSpan.FromSeconds(timeout), ref captureData);
            _registerData = captureData[0].Select(num => { return (byte)num; }).ToArray();
        }
    }
}

using NationalInstruments.ApplicationsEngineering.Mipi;
using NationalInstruments.ModularInstruments.NIDigital;
using System;
using System.Linq;

namespace Rfmd8090
{
    class Program
    {
        static void Main(string[] args)
        {
            NIDigital session = new NIDigital("PXIe-6570", true, false);
            MipiRffe.LoadDigitalProject(session);

            MipiRffe bus0 = new MipiRffe(session, 0);
            bus0.EnableVIO();

            foreach (var writeParameters in Rfmd8090.Band1Apt)
            {
                byte slaveAddress = writeParameters.Item1;
                ushort registerAddress = writeParameters.Item2;
                byte[] writeData = writeParameters.Item3;
                bus0.ExtendedRegisterWrite(slaveAddress, registerAddress, writeData);
            }

            Console.WriteLine("Slave | Register | Write | Read");
            foreach(var writeParameters in Rfmd8090.Band1Apt)
            {
                byte slaveAddress = writeParameters.Item1;
                ushort registerAddress = writeParameters.Item2;
                byte[] writeData = writeParameters.Item3;
                byte[] readData = bus0.ExtendedRegisterRead(slaveAddress, registerAddress, writeData.Length);
                string formattedWriteData = '[' + string.Join(",", writeData.Select(val => { return string.Format("0x{0:X2}", val); })) + ']';
                string formattedReadData = '[' + string.Join(",", readData.Select(val => { return string.Format("0x{0:X2}", val); })) + ']';
                Console.WriteLine(string.Format("0x{0:X2} | 0x{1:X2} | {2:s} | {3:s}", slaveAddress, registerAddress, formattedWriteData, formattedReadData));
            }

            Console.WriteLine("Press any key to exit..");
            Console.ReadKey();

            bus0.DisableVIO();
            session.Close();
        }
    }
}

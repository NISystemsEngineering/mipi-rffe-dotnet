using NationalInstruments.ApplicationsEngineering.Mipi;
using NationalInstruments.ModularInstruments.NIDigital;
using System;
using System.Linq;
using System.Collections.Generic;

namespace Rfmd8090
{
    class Program
    {
        static void Main(string[] args)
        {
            // open session and load digital project
            NIDigital session = new NIDigital("PXIe-6570", true, false);
            MipiRffe.LoadDigitalProject(session);

            // create and enable mipi bus
            MipiRffe bus0 = new MipiRffe(session, 0);
            bus0.EnableVIO();

            // build command lists
            List<RffeCommand> writeCommands = new List<RffeCommand>();
            List<RffeCommand> readCommands = new List<RffeCommand>();
            foreach (var commandParameters in Rfmd8090.Band1Apt)
            {
                var (slaveAddress, registerAddress, writeData) = commandParameters;

                RffeCommand writeCommand = new RffeExtendedRegisterWriteCommand(slaveAddress, registerAddress, writeData);
                writeCommands.Add(writeCommand);

                RffeCommand readCommand = new RffeExtendedRegisterReadCommand(slaveAddress, registerAddress, writeData.Length);
                readCommands.Add(readCommand);
            }

            // burst commands
            bus0.Burst(writeCommands);
            bus0.Burst(readCommands);

            // print results to console
            Console.WriteLine("Slave | Register | Write | Read");
            foreach (var commands in Enumerable.Zip(writeCommands, readCommands, (writeCommand, readCommand) => (writeCommand, readCommand)))
            {
                var writeCommand = (RffeExtendedRegisterWriteCommand)commands.writeCommand;
                var readCommand = (RffeExtendedRegisterReadCommand)commands.readCommand;
                string formattedWriteData = '[' + string.Join(",", writeCommand.RegisterData.Select(val => string.Format("0x{0:X2}", val))) + ']';
                string formattedReadData = '[' + string.Join(",", readCommand.RegisterData.Select(val => string.Format("0x{0:X2}", val))) + ']';
                Console.WriteLine(string.Format("0x{0:X2} | 0x{1:X2} | {2:s} | {3:s}", writeCommand.slaveAddress, writeCommand.registerAddress, formattedWriteData, formattedReadData));
            }

            // wait on user
            Console.WriteLine("Burst complete. Press any key to exit..");
            Console.ReadKey();

            // disable bus and close session
            bus0.DisableVIO();
            session.Close();
        }
    }
}

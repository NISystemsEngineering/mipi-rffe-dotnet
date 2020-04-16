using System.Collections.Generic;
using System.Linq;

namespace NationalInstruments.ApplicationsEngineering.Mipi
{
    internal static class Parity
    {
        public static byte CalculateOddParityBit(byte num)
        {
            return (byte)(1 - num.ToBits(8).Sum() % 2);
        }

        public static byte CalculateOddParityBit(IEnumerable<byte> bytes)
        {
            var bits = bytes.Select(CalculateOddParityBit);
            return (byte)(1 - bits.Sum() % 2);
        }
    }
}

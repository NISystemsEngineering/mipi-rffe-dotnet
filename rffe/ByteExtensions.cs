using System.Collections.Generic;
using System.Linq;

namespace NationalInstruments.ApplicationsEngineering.Mipi
{
    internal static class ByteExtensions
    {
        public static byte[] ToBits(this byte self, int width)
        {
            byte[] bits = new byte[width];
            for (int i = 0; i < width; i++)
                bits[i] = (byte)((self >> (width - 1 - i)) & 1);
            return bits;
        }

        public static int Sum(this IEnumerable<byte> bytes)
        {
            return bytes.Select(num => { return (int)num; }).Sum(); // cast to int and return sum
        }
    }
}   

using System.Collections;
using System.Diagnostics;

namespace nayuki.qrcodegen
{
    public class BitBuffer
    {
        private BitArray data;
        private int      bitLength;

        public BitBuffer()
        {
            data      = new BitArray(0);
            bitLength = 0;
        }

        public int BitLength()
        {
            Debug.Assert(bitLength >= 0);

            return bitLength;
        }
    }
}

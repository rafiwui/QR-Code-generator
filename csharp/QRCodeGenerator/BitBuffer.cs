using System;
using System.Collections;
using System.Diagnostics;

namespace nayuki.qrcodegen
{
    public class BitBuffer : ICloneable
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

        public int GetBit(int index)
        {
            if (index < 0 || index >= bitLength) 
                throw new IndexOutOfRangeException();

            return data.Get(index) ? 1 : 0;
        }

        public void AppendBits(int val, int len)
        {
            uint uval = (uint)val;

            if (len < 0 || len >= 32 || uval >> len != 0) 
                throw new ArgumentException("Value out of range", nameof(val));
            if (int.MaxValue - bitLength < len)
                throw new ArgumentException("Maximum length reached", nameof(len));

            for (int i = len - 1; i >= 0; i--, bitLength++)
                data.Set(bitLength, QrCode.GetBit(val, i));
        }

        public void AppendData(BitBuffer bb)
        {
            if (bb is null)
                throw new ArgumentNullException(nameof(bb));

            if (int.MaxValue - bitLength < bb.bitLength)
                throw new ArgumentException("Maximum length reached");

            for (int i = 0; i < bb.bitLength; i++, bitLength++)
                data.Set(bitLength, bb.data.Get(i));
        }

        public object Clone()
        {
            BitBuffer result = (BitBuffer)MemberwiseClone();
            result.data = (BitArray)result.data.Clone();

            return result;
        }
    }
}

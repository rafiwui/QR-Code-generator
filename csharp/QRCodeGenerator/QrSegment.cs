using System;

namespace nayuki.qrcodegen
{
    public class QrSegment
    {
        public static QrSegment MakeBytes(byte[] data)
        {
            if (data is null) throw new ArgumentNullException();
            BitBuffer bitBuffer = new BitBuffer();
            foreach (byte b in data)
            {
                bitBuffer.appendBits(b & 0xFF, 8);
            }

            return new QrSegment(Mode.BYTE, data.Length, bitBuffer);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace nayuki.qrcodegen
{
    public class QrSegment
    {
        public static QrSegment MakeBytes(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            BitBuffer bb = new BitBuffer();
            foreach (byte b in data)
            {
                bb.AppendBits(b & 0xFF, 8);
            }

            return new QrSegment(Mode.BYTE, data.Length, bb);
        }

        public static QrSegment MakeNumeric(string digits)
        {
            if (digits is null) throw new ArgumentNullException(nameof(digits));

            if (NUMERIC_REGEX.Matches(digits).Count <= 0) throw new ArgumentException("String contains non-numeric characters", nameof(digits));

            BitBuffer bb = new BitBuffer();
            for (int i = 0; i < digits.Length;)
            {
                int n = Math.Min(digits.Length - i, 3);
                bb.AppendBits(int.Parse(digits.Substring(i, n)), n * 3 + 1);
                i += n;
            }

            return new QrSegment(Mode.NUMERIC, digits.Length, bb);
        }

        public static QrSegment MakeAlphanumeric(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));

            if (ALPHANUMERIC_REGEX.Matches(text).Count <= 0)
                throw new ArgumentException("String contains unencodable characters in alphanumeric mode", nameof(text));

            BitBuffer bb = new BitBuffer();
            int       i;
            for (i = 0; i <= text.Length - 2; i += 2)
            {
                int temp = ALPHANUMERIC_CHARSET.IndexOf(text[i]) * 45;
                temp += ALPHANUMERIC_CHARSET.IndexOf(text[i + 1]);
                bb.AppendBits(temp, 11);
            }

            if (i < text.Length) bb.AppendBits(ALPHANUMERIC_CHARSET.IndexOf(text[i]), 6);

            return new QrSegment(Mode.ALPHANUMERIC, text.Length, bb);
        }

        public static IList<QrSegment> MakeSegments(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));

            IList<QrSegment> result = new List<QrSegment>();
            if (text.Equals(""))
            {
                /* do nothing */
            }
            else if (NUMERIC_REGEX.Matches(text).Count > 0)
                result.Add(MakeNumeric(text));
            else if (ALPHANUMERIC_REGEX.Matches(text).Count > 0)
                result.Add(MakeAlphanumeric(text));
            else
                result.Add(MakeBytes(Encoding.UTF8.GetBytes(text)));

            return result;
        }

        public static QrSegment MakeEci(int assignVal)
        {
            BitBuffer bb = new BitBuffer();

            if (assignVal < 0) throw new ArgumentException("ECI assignment value out of range", nameof(assignVal));

            if (assignVal < (1 << 7))
            {
                bb.AppendBits(assignVal, 8);
            }
            else if (assignVal < (1 << 14))
            {
                bb.AppendBits(2, 2);
                bb.AppendBits(assignVal, 14);
            }
            else if (assignVal < 1_000_000)
            {
                bb.AppendBits(6, 3);
                bb.AppendBits(assignVal, 21);
            }
            else
            {
                throw new ArgumentException("ECI assignment value out of range", nameof(assignVal));
            }

            return new QrSegment(Mode.ECI, 0, bb);
        }

        public readonly Mode      mode;
        public readonly int       numChars;
        public readonly BitBuffer data;

        public QrSegment(Mode md, int numCh, BitBuffer data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));

            mode = md ?? throw new ArgumentNullException(nameof(md));

            if (numCh < 0) throw new ArgumentException("Invalid value", nameof(numCh));
            numChars  = numCh;
            this.data = (BitBuffer)data.Clone();
        }

        public BitBuffer GetData()
        {
            return (BitBuffer)data.Clone();
        }

        public static int GetTotalBits(IList<QrSegment> segs, int version)
        {
            if (segs is null) throw new ArgumentNullException(nameof(segs));

            long result = 0;
            foreach (QrSegment seg in segs)
            {
                if (seg is null) throw new ArgumentNullException(nameof(seg));
                int ccbits = seg.mode.NumCharCountBits(version);

                if (seg.numChars >= (1 << ccbits)) return -1;
                result += 4L + ccbits + seg.data.BitLength();

                if (result > int.MaxValue) return -1;
            }

            return (int)result;
        }

        public static readonly Regex  NUMERIC_REGEX        = new Regex(@"[0-9]*");
        public static readonly Regex  ALPHANUMERIC_REGEX   = new Regex(@"[A-Z0-9 $%*+./:-]*");
        public const           string ALPHANUMERIC_CHARSET = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";

        public class Mode
        {
            public static readonly Mode NUMERIC      = new Mode(0x1, 10, 12, 14);
            public static readonly Mode ALPHANUMERIC = new Mode(0x2, 9, 11, 13);
            public static readonly Mode BYTE         = new Mode(0x4, 8, 16, 16);
            public static readonly Mode KANJI        = new Mode(0x8, 8, 10, 12);
            public static readonly Mode ECI          = new Mode(0x7, 0, 0, 0);

            public readonly int modeBits;

            private readonly int[] numBitsCharCount;

            private Mode(int mode, params int[] ccbits)
            {
                modeBits         = mode;
                numBitsCharCount = ccbits;
            }

            public int NumCharCountBits(int ver)
            {
                Debug.Assert(QrCode.MIN_VERSION <= ver && ver <= QrCode.MAX_VERSION);

                return numBitsCharCount[(ver + 7) / 17];
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Windows.Markup;

namespace nayuki.qrcodegen
{
    public class QrCode
    {
        public static QrCode EncodeText(string text, Ecc ecl)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (ecl is null) throw new ArgumentNullException(nameof(ecl));

            IList<QrSegment> segments = QrSegment.MakeSegments(text);

            return EncodeSegments(segments, ecl);
        }

        public static QrCode EncodeBinary(byte[] data, Ecc ecl)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (ecl is null) throw new ArgumentNullException(nameof(ecl));

            QrSegment seg = QrSegment.MakeBytes(data);

            return EncodeSegments(new List<QrSegment>(new[] {
                seg
            }), ecl);
        }

        public static QrCode EncodeSegments(IList<QrSegment> segs, Ecc ecl)
        {
            return EncodeSegments(segs, ecl, MIN_VERSION, MAX_VERSION, -1, true);
        }

        public static QrCode EncodeSegments(IList<QrSegment> segs, Ecc ecl, int minVersion, int maxVersion, int mask, bool boostEcl)
        {
            if (segs is null) throw new ArgumentNullException(nameof(segs));
            if (ecl is null) throw new ArgumentNullException(nameof(ecl));

            if (!(MIN_VERSION <= minVersion && minVersion <= maxVersion && maxVersion <= MAX_VERSION) || mask < -1 || mask > 7)
                throw new ArgumentException("Invalid value");

            int version, dataUsedBits;
            for (version = minVersion;; version++)
            {
                int dataCapacityBits = GetNumDataCodewords(version, ecl) * 8;
                dataUsedBits = QrSegment.GetTotalBits(segs, version);

                if (dataUsedBits != -1 && dataUsedBits <= dataCapacityBits) break;

                if (version >= maxVersion)
                {
                    string msg                  = "Segment too long";
                    if (dataUsedBits != -1) msg = $"Data length = {dataUsedBits} bits, Max capacity = {dataCapacityBits} bits";

                    throw new DataTooLongException(msg);
                }
            }

            Debug.Assert(dataUsedBits != -1);

            foreach (Ecc newEcl in Ecc.Values())
            {
                if (boostEcl && dataUsedBits <= GetNumDataCodewords(version, newEcl) * 8) ecl = newEcl;
            }

            BitBuffer bb = new BitBuffer();
            foreach (QrSegment seg in segs)
            {
                bb.AppendBits(seg.mode.modeBits, 4);
                bb.AppendBits(seg.numChars, seg.mode.NumCharCountBits(version));
                bb.AppendData(seg.data);
            }

            Debug.Assert(bb.BitLength() == dataUsedBits);

            int dataCapacityBits2 = GetNumDataCodewords(version, ecl) * 8;
            Debug.Assert(bb.BitLength() <= dataCapacityBits2);
            bb.AppendBits(0, Math.Min(4, dataCapacityBits2 - bb.BitLength()));
            bb.AppendBits(0, (8 - bb.BitLength() % 8) % 8);
            Debug.Assert(bb.BitLength() % 8 == 0);

            for (int padByte = 0xEC; bb.BitLength() < dataCapacityBits2; padByte ^= 0xEC ^ 0X11) bb.AppendBits(padByte, 8);

            byte[] dataCodewords                                            = new byte[bb.BitLength() / 8];
            for (uint i = 0; i < bb.BitLength(); i++) dataCodewords[i >> 3] |= Convert.ToByte(bb.GetBit((int)i) << (7 - ((int)i & 7)));

            return new QrCode(version, ecl, dataCodewords, mask);
        }

        public readonly int version;
        public readonly int size;
        public readonly Ecc errorCorrectionLevel;
        public readonly int mask;

        private bool[,] modules;
        private bool[,] isFunction;

        public QrCode(int ver, Ecc ecl, byte[] dataCodewords, int mask)
        {
            if (ver < MIN_VERSION || ver > MAX_VERSION) throw new ArgumentException("Version value out of range", nameof(ver));
            if (mask < -1 || mask > 7) throw new ArgumentException("Mask value out of range", nameof(mask));

            version              = ver;
            size                 = ver * 4 + 17;
            errorCorrectionLevel = ecl ?? throw new ArgumentNullException(nameof(ecl));

            if (dataCodewords is null) throw new ArgumentNullException(nameof(dataCodewords));
            modules    = new bool[size, size];
            isFunction = new bool[size, size];

            DrawFunctionPatterns();
            byte[] allCodewords = AddEccAndInterleave(dataCodewords);
            DrawCodewords(allCodewords);
            this.mask  = HandleConstructorMasking(mask);
            isFunction = null;
        }

        public bool GetModule(int x, int y)
        {
            return 0 <= x && x < size && 0 <= y && y < size && modules[y, x];
        }

        public Bitmap ToImage(int scale, int border)
        {
            if (scale <= 0 || border < 0) throw new ArgumentException("Value out of range");
            if (border > int.MaxValue / 2 || size + border * 2L > int.MaxValue / scale) throw new ArgumentException("Scale or border too large");

            Bitmap result = new Bitmap((size + border * 2) * scale, (size + border * 2) * scale, PixelFormat.Format24bppRgb);
            for (int y = 0; y < result.Height; y++)
            {
                for (int x = 0; x < result.Width; x++)
                {
                    bool color = GetModule(x / scale - border, y / scale - border);
                    result.SetPixel(x, y, color ? Color.Black : Color.White);
                }
            }

            return result;
        }

        public String toSvgString(int border)
        {
            if (border < 0) throw new ArgumentException("Border must be non-negative", nameof(border));

            long brd = border;
            StringBuilder sb = new StringBuilder()
                               .Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n")
                               .Append("<!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" \"http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd\">\n")
                               .AppendFormat("<svg xmlns=\"http://www.w3.org/2000/svg\" version=\"1.1\" viewBox=\"0 0 {0} {0}\" stroke=\"none\">\n",
                                             size + brd * 2)
                               .Append("\t<rect width=\"100%\" height=\"100%\" fill=\"#FFFFFF\"/>\n")
                               .Append("\t<path d=\"");
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (GetModule(x, y))
                    {
                        if (x != 0 || y != 0) sb.Append(" ");
                        sb.AppendFormat("M{0},{1}h1v1h-1z", x + brd, y + brd);
                    }
                }
            }

            return sb.Append("\" fill=\"#000000\"/>\n")
                     .Append("</svg>\n")
                     .ToString();
        }

        private void DrawFunctionPatterns()
        {
            for (int i = 0; i < size; i++)
            {
                SetFunctionModule(6, i, i % 2 == 0);
                SetFunctionModule(i, 6, i % 2 == 0);
            }

            DrawFinderPattern(3, 3);
            DrawFinderPattern(size - 4, 3);
            DrawFinderPattern(3, size - 4);

            int[] alignPatPos = GetAlignmentPatternPositions();
            int   numAlign    = alignPatPos.Length;
            for (int i = 0; i < numAlign; i++)
            {
                for (int j = 0; j < numAlign; j++)
                {
                    if (!(i == 0 && j == 0 || i == 0 && j == numAlign - 1 || i == numAlign - 1 && j == 0)) DrawAlignmentPattern(alignPatPos[i], alignPatPos[j]);
                }
            }

            DrawFormatBits(0);
            DrawVersion();
        }

        private void DrawFormatBits(int mask)
        {
            int  data                        = (errorCorrectionLevel.formatBits << 3) | mask;
            uint rem                         = (uint)data;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int bits                         = ((data << 10) | (int)rem) ^ 0x5412;
            Debug.Assert((uint)bits >> 15 == 0);

            for (int i = 0; i <= 5; i++) SetFunctionModule(8, i, GetBit(bits, i));
            SetFunctionModule(8, 7, GetBit(bits, 6));
            SetFunctionModule(8, 8, GetBit(bits, 7));
            SetFunctionModule(7, 8, GetBit(bits, 8));
            for (int i = 9; i < 15; i++) SetFunctionModule(14 - i, 8, GetBit(bits, i));

            for (int i = 0; i < 8; i++) SetFunctionModule(size - 1 - i, 8, GetBit(bits, i));
            for (int i = 9; i < 15; i++) SetFunctionModule(8, size - 15 + i, GetBit(bits, i));
            SetFunctionModule(8, size - 8, true);
        }

        private void DrawVersion()
        {
            if (version < 7) return;

            uint rem                         = (uint)version;
            for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            int bits                         = (version << 12) | (int)rem;
            Debug.Assert((uint)bits >> 18 == 0);

            for (int i = 0; i < 18; i++)
            {
                bool bit = GetBit(bits, i);
                int  a   = size - 11 + i % 3;
                int  b   = i / 3;
                SetFunctionModule(a, b, bit);
                SetFunctionModule(b, a, bit);
            }
        }

        private void DrawFinderPattern(int x, int y)
        {
            for (int dy = -4; dy <= 4; dy++)
            {
                for (int dx = -4; dx <= 4; dx++)
                {
                    int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    int xx   = x + dx, yy = y + dy;
                    if (0 <= xx && xx < size && 0 <= yy && yy < size) SetFunctionModule(xx, yy, dist != 2 && dist != 4);
                }
            }
        }

        private void DrawAlignmentPattern(int x, int y)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                for (int dx = -2; dx <= 2; dx++) SetFunctionModule(x + dx, y + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }
        }

        private void SetFunctionModule(int x, int y, bool isBlack)
        {
            modules[y, x]    = isBlack;
            isFunction[y, x] = true;
        }

        private byte[] AddEccAndInterleave(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (data.Length != GetNumDataCodewords(version, errorCorrectionLevel)) throw new ArgumentException(nameof(data));

            int numBlocks      = NUM_ERROR_CORRECTION_BLOCKS[errorCorrectionLevel.formatBits, version];
            int blockEccLen    = ECC_CODEWORDS_PER_BLOCK[errorCorrectionLevel.formatBits, version];
            int rawCodewords   = GetNumRawDataModules(version) / 8;
            int numShortBlocks = numBlocks - rawCodewords % numBlocks;
            int shortBlockLen  = rawCodewords / numBlocks;

            byte[][] blocks = new byte[numBlocks][];
            byte[]   rsDiv  = ReedSolomonComputeDivisor(blockEccLen);
        }

        public const int MIN_VERSION = 1;

        /** The maximum version number (40) supported in the QR Code Model 2 standard. */
        public const int MAX_VERSION = 40;


        // For use in getPenaltyScore(), when evaluating which mask is best.
        private static readonly int PENALTY_N1 = 3;
        private static readonly int PENALTY_N2 = 3;
        private static readonly int PENALTY_N3 = 40;
        private static readonly int PENALTY_N4 = 10;

        // Sorted by formatBit
        private static readonly byte[,] ECC_CODEWORDS_PER_BLOCK = {
            // Version: (note that index 0 is for padding, and is set to an value not appearing elsewhere => 0)
            //0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40
            {
                0, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28,
                28, 28, 28, 28, 28
            }, // Medium
            {
                0, 7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30,
                30, 30, 30, 30, 30
            }, // Low
            {
                0, 17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28, 30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30,
                30, 30, 30, 30, 30
            }, // High
            {
                0, 13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30, 28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30,
                30, 30, 30, 30, 30
            }, // Quartile
        };

        // Sorted by formatBit
        private static readonly byte[,] NUM_ERROR_CORRECTION_BLOCKS = {
            // Version: (note that index 0 is for padding, and is set to an value not appearing elsewhere => 0)
            //0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40
            {
                0, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47,
                49
            }, // Medium
            {
                0, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4, 4, 4, 4, 4, 6, 6, 6, 6, 7, 8, 8, 9, 9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25
            }, // Low
            {
                0, 1, 1, 2, 4, 4, 4, 5, 6, 8, 8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25, 25, 34, 30, 32, 35, 37, 40, 42, 45, 48, 51, 54, 57, 60, 63, 66, 70, 74,
                77, 81
            }, // High
            {
                0, 1, 1, 2, 2, 4, 4, 6, 6, 8, 8, 8, 10, 12, 16, 12, 17, 16, 18, 21, 20, 23, 23, 25, 27, 29, 34, 34, 35, 38, 40, 43, 45, 48, 51, 53, 56, 59, 62,
                65, 68
            }, // Quartile
        };

        public class Ecc
        {
            public static readonly Ecc LOW      = new Ecc(1);
            public static readonly Ecc MEDIUM   = new Ecc(0);
            public static readonly Ecc QUARTILE = new Ecc(3);
            public static readonly Ecc HIGH     = new Ecc(2);

            public static IEnumerable<Ecc> Values()
            {
                return new[] {
                    LOW, MEDIUM, QUARTILE, HIGH
                };
            }

            public readonly int formatBits;

            private Ecc(int fb)
            {
                formatBits = fb;
            }
        }
    }
}

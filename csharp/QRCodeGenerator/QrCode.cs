using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;

namespace nayuki.qrcodegen
{
    public class QrCode
    {
        /// <summary>
        /// Returns a QR Code representing the specified Unicode text string at the specified error correction level.
        /// As a conservative upper bound, this function is guaranteed to succeed for strings that have 738 or fewer
        /// Unicode code points (not UTF-16 code units) if the low error correction level is used. The smallest possible
        /// QR Code version is automatically chosen for the output. The ECC level of the result may be higher than the
        /// ecl argument if it can be done without increasing the version.
        /// </summary>
        /// <param name="text">The text to be encoded (not null), which can be any Unicode string.</param>
        /// <param name="ecl">The error correction level to use (not null) (boostable).</param>
        /// <returns>A QR Code (not null) representing the text.</returns>
        /// <exception cref="ArgumentNullException">If <paramref name="text"/> or <paramref name="ecl"/> is null.</exception>
        public static QrCode EncodeText(string text, Ecc ecl)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (ecl is null) throw new ArgumentNullException(nameof(ecl));

            IList<QrSegment> segments = QrSegment.MakeSegments(text);

            return EncodeSegments(segments, ecl);
        }

        /// <summary>
        /// Returns a QR Code representing the specified binary data at the specified error correction level.
        /// This function always encodes using the binary segment mode, not any text mode. The maximum number of
        /// bytes allowed is 2953. The smallest possible QR Code version is automatically chosen for the output.
        /// The ECC level of the result may be higher than the ecl argument if it can be done without increasing the version.
        /// </summary>
        /// <param name="data">The binary data to encode (not null).</param>
        /// <param name="ecl">The error correction level to use (not null) (boostable).</param>
        /// <returns>A QR Code (not null) representing the data.</returns>
        /// <exception cref="ArgumentNullException">If the <paramref name="data"/> or <paramref name="ecl"/> is null.</exception>
        public static QrCode EncodeBinary(byte[] data, Ecc ecl)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (ecl is null) throw new ArgumentNullException(nameof(ecl));

            QrSegment seg = QrSegment.MakeBytes(data);

            return EncodeSegments(new List<QrSegment>(new[] {
                seg
            }), ecl);
        }

        /// <summary>
        /// Returns a QR Code representing the specified segments with the specified encoding parameters.
        /// The smallest possible QR Code version within the specified range is automatically
        /// chosen for the output. Iff boostEcl is true, then the ECC level of the
        /// result may be higher than the ecl argument if it can be done without increasing
        /// the version. The mask number is either between 0 to 7 (inclusive) to force that
        /// mask, or &#x2212;1 to automatically choose an appropriate mask (which may be slow).
        /// This function allows the user to create a custom sequence of segments that switches
        /// between modes (such as alphanumeric and byte) to encode text in less space.
        /// This is a mid-level API; the high-level API is <see cref="EncodeText(String,Ecc)"/>
        /// and <see cref="EncodeBinary(byte[],Ecc)"/>.
        /// </summary>
        /// <param name="segs">The segments to encode.</param>
        /// <param name="ecl">The error correction level to use (not {@code null}) (boostable).</param>
        /// <param name="minVersion">The minimum allowed version of the QR Code (at least 1).</param>
        /// <param name="maxVersion">The maximum allowed version of the QR Code (at most 40).</param>
        /// <param name="mask">The mask number to use (between 0 and 7 (inclusive)), or &#x2212;1 for automatic mask.</param>
        /// <param name="boostEcl">Increases the ECC level as long as it doesn't increase the version number.</param>
        /// <returns>A QR Code (not null) representing the segments.</returns>
        /// <exception cref="ArgumentNullException">If the list of segments, any segment, or the error correction level is null.</exception>
        /// <exception cref="ArgumentException">If 1 &#x2264; minVersion &#x2264; maxVersion &#x2264; 40
        /// or &#x2212;1 &#x2264; mask &#x2264; 7 is violated.</exception>
        /// <exception cref="DataTooLongException">If the segments fail to fit in
        /// the maxVersion QR Code at the ECL, which means they are too long.</exception>
        public static QrCode EncodeSegments(IList<QrSegment> segs, Ecc ecl, int minVersion = MIN_VERSION, int maxVersion = MAX_VERSION, int mask = -1,
                                            bool boostEcl = true)
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

        /// <summary>
        /// The version number of this QR Code, which is between 1 and 40 (inclusive).
        /// This determines the size of this barcode.
        /// </summary>
        public readonly int version;

        /// <summary>
        /// The width and height of this QR Code, measured in modules, between
        /// 21 and 177 (inclusive). This is equal to version &#xD7; 4 + 17.
        /// </summary>
        public readonly int size;

        /// <summary>
        /// The error correction level used in this QR Code, which is not null.
        /// </summary>
        public readonly Ecc errorCorrectionLevel;

        /// <summary>
        /// The index of the mask pattern used in this QR Code, which is between 0 and 7 (inclusive).
        /// Even if a QR Code is created with automatic masking requested (mask = &#x2212;1),
        /// the resulting object still has a mask value between 0 and 7.
        /// </summary>
        public readonly int mask;

        /// <summary>
        /// The modules of this QR Code (false = white, true = black).
        /// Immutable after constructor finishes. Accessed through <see cref="GetModule(int,int)"/>.
        /// </summary>
        private readonly bool[,] modules;

        /// <summary>
        /// Indicates function modules that are not subjected to masking. Discarded when constructor finishes.
        /// </summary>
        private readonly bool[,] isFunction;

        /// <summary>
        /// Constructs a QR Code with the specified version number,
        /// error correction level, data codeword bytes, and mask number.
        /// This is a low-level API that most users should not use directly. A mid-level
        /// API is the <see cref="EncodeSegments(System.Collections.Generic.IList{nayuki.qrcodegen.QrSegment},Ecc,int,int,int,bool)"/> function.
        /// </summary>
        /// <param name="ver">The version number to use, which must be in the range 1 to 40 (inclusive).</param>
        /// <param name="ecl">The error correction level to use.</param>
        /// <param name="dataCodewords">The bytes representing segments to encode (without ECC).</param>
        /// <param name="mask">The mask pattern to use, which is either &#x2212;1 for automatic choice or from 0 to 7 for fixed choice.</param>
        /// <exception cref="ArgumentException">If the version or mask value is out of range,
        /// or if the data is the wrong length for the specified version and error correction level.</exception>
        /// <exception cref="ArgumentNullException">If the byte array or error correction level is null.</exception>
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

        /// <summary>
        /// Returns the color of the module (pixel) at the specified coordinates, which is false
        /// for white or true for black. The top left corner has the coordinates (x=0, y=0)
        /// If the specified coordinates are out of bounds, then false (white) is returned.
        /// </summary>
        /// <param name="x">The x coordinate, where 0 is the left edge and size&#x2212;1 is the right edge.</param>
        /// <param name="y">The y coordinate, where 0 is the top edge and size&#x2212;1 is the bottom edge.</param>
        /// <returns>true if the coordinates are in bounds and the module
        /// at that location is black, or false (white) otherwise.</returns>
        public bool GetModule(int x, int y)
        {
            return 0 <= x && x < size && 0 <= y && y < size && modules[y, x];
        }

        /// <summary>
        /// Returns a raster image depicting this QR Code, with the specified module scale and border modules.
        /// For example, toImage(scale=10, border=4) means to pad the QR Code with 4 white
        /// border modules on all four sides, and use 10&#xD7;10 pixels to represent each module.
        /// The resulting image only contains the hex colors 000000 and FFFFFF.
        /// </summary>
        /// <param name="scale">The side length (measured in pixels, must be positive) of each module.</param>
        /// <param name="border">The number of border modules to add, which must be non-negative.</param>
        /// <returns>A new image representing this QR Code, with padding and scaling</returns>
        /// <exception cref="ArgumentException">If the scale or border is out of range, or if
        /// <paramref name="scale"/>, <paramref name="border"/>, size cause the image dimensions to exceed <see cref="int.MaxValue"/></exception>
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

        /// <summary>
        /// Returns a string of SVG code for an image depicting this QR Code, with the specified number
        /// of border modules. The string always uses Unix newlines (\n), regardless of the platform.
        /// </summary>
        /// <param name="border">The number of border modules to add, which must be non-negative.</param>
        /// <returns>A string representing this QR Code as an SVG XML document.</returns>
        /// <exception cref="ArgumentException">If the border is negative.</exception>
        public string ToSvgString(int border)
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

        /// <summary>
        /// Reads this object's version field, and draws and marks all function modules.
        /// </summary>
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

        /// <summary>
        /// Draws two copies of the format bits (with its own error correction code)
        /// based on the given mask and this object's error correction level field.
        /// </summary>
        /// <param name="mask"></param>
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

        /// <summary>
        /// Draws two copies of the version bits (with its own error correction code),
        /// based on this object's version field, if 7 &lt;= version &lt;= 40.
        /// </summary>
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

        /// <summary>
        /// Draws a 9*9 finder pattern including the border separator,
        /// with the center module at (x, y). Modules can be out of bounds.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
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

        /// <summary>
        /// Draws a 5*5 alignment pattern, with the center module
        /// at (x, y). All modules must be in bounds.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        private void DrawAlignmentPattern(int x, int y)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                for (int dx = -2; dx <= 2; dx++) SetFunctionModule(x + dx, y + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }
        }

        /// <summary>
        /// Sets the color of a module and marks it as a function module.
        /// Only used by the constructor. Coordinates must be in bounds.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="isBlack"></param>
        private void SetFunctionModule(int x, int y, bool isBlack)
        {
            modules[y, x]    = isBlack;
            isFunction[y, x] = true;
        }

        /// <summary>
        /// Returns a new byte string representing the given data with the appropriate error correction
        /// codewords appended to it, based on this object's version and error correction level.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
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
            for (int i = 0, k = 0; i < numBlocks; i++)
            {
                byte[] dat = Utils.CopyArrayRange(data, k, k + shortBlockLen - blockEccLen + (i < numShortBlocks ? 0 : 1));
                k += dat.Length;
                byte[] block = Utils.CopyArrayWithNewLength(dat, shortBlockLen + 1);
                byte[] ecc   = ReedSolomonComputeRemainder(dat, rsDiv);
                Array.Copy(ecc, 0, block, block.Length - blockEccLen, ecc.Length);
                blocks[i] = block;
            }

            byte[] result = new byte[rawCodewords];
            for (int i = 0, k = 0; i < blocks[0].Length; i++)
            {
                for (int j = 0; j < blocks.Length; j++)
                {
                    if (i != shortBlockLen - blockEccLen || j >= numShortBlocks)
                    {
                        result[k] = blocks[j][i];
                        k++;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Draws the given sequence of 8-bit codewords (data and error correction) onto the entire
        /// data area of this QR Code. Function modules need to be marked off before this is called.
        /// </summary>
        /// <param name="data"></param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        private void DrawCodewords(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (data.Length != GetNumRawDataModules(version) / 8) throw new ArgumentException();

            int i = 0;
            for (int right = size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (int vert = 0; vert < size; vert++)
                {
                    for (int j = 0; j < 2; j++)
                    {
                        int  x      = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int  y      = upward ? size - 1 - vert : vert;
                        if (!isFunction[y, x] && i < data.Length * 8)
                        {
                            modules[y, x] = GetBit(data[(uint)i >> 3], 7 - (i & 7));
                            i++;
                        }
                    }
                }
            }

            Debug.Assert(i == data.Length * 8);
        }

        /// <summary>
        /// XORs the codeword modules in this QR Code with the given mask pattern.
        /// The function modules must be marked and the codeword bits must be drawn
        /// before masking. Due to the arithmetic of XOR, calling applyMask() with
        /// the same mask value a second time will undo the mask. A final well-formed
        /// QR Code needs exactly one (not zero, two, etc.) mask applied.
        /// </summary>
        /// <param name="mask"></param>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="Exception"></exception>
        private void ApplyMask(int mask)
        {
            if (mask < 0 || mask > 7) throw new ArgumentException("Mask value out of range", nameof(mask));

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool invert;
                    switch (mask)
                    {
                        case 0:
                            invert = (x + y) % 2 == 0;

                            break;
                        case 1:
                            invert = y % 2 == 0;

                            break;
                        case 2:
                            invert = x % 3 == 0;

                            break;
                        case 3:
                            invert = (x + y) % 3 == 0;

                            break;
                        case 4:
                            invert = (x / 3 + y / 2) % 2 == 0;

                            break;
                        case 5:
                            invert = x * y % 2 + x * y % 3 == 0;

                            break;
                        case 6:
                            invert = (x * y % 2 + x * y % 3) % 2 == 0;

                            break;
                        case 7:
                            invert = ((x + y) % 2 + x * y % 3) % 2 == 0;

                            break;
                        default:
                            throw new Exception();
                    }

                    modules[y, x] ^= invert & !isFunction[y, x];
                }
            }
        }

        /// <summary>
        /// A messy helper function for the constructor. This QR Code must be in an unmasked state when this
        /// method is called. The given argument is the requested mask, which is -1 for auto or 0 to 7 for fixed.
        /// This method applies and returns the actual mask chosen, from 0 to 7.
        /// </summary>
        /// <param name="mask"></param>
        /// <returns></returns>
        private int HandleConstructorMasking(int mask)
        {
            if (mask == -1)
            {
                int minPenalty = int.MaxValue;
                for (int i = 0; i < 8; i++)
                {
                    ApplyMask(i);
                    DrawFormatBits(i);
                    int penalty = GetPenaltyScore();
                    if (penalty < minPenalty)
                    {
                        mask       = i;
                        minPenalty = penalty;
                    }

                    ApplyMask(i);
                }
            }

            Debug.Assert(0 <= mask && mask <= 7);
            ApplyMask(mask);
            DrawFormatBits(mask);

            return mask;
        }

        /// <summary>
        /// Calculates and returns the penalty score based on state of this QR Code's current modules.
        /// This is used by the automatic mask choice algorithm to find the mask pattern that yields the lowest score.
        /// </summary>
        /// <returns></returns>
        private int GetPenaltyScore()
        {
            int   result     = 0;
            int[] runHistory = new int[7];
            for (int y = 0; y < size; y++)
            {
                bool runColor = false;
                int  runX     = 0;
                int  padRun   = size;
                for (int x = 0; x < size; x++)
                {
                    if (modules[y, x] == runColor)
                    {
                        runX++;
                        if (runX == 5)
                            result += PENALTY_N1;
                        else if (runX > 5) result++;
                    }
                    else
                    {
                        FinderPenaltyAddHistory(runX + padRun, runHistory);
                        padRun = 0;
                        if (!runColor) result += FinderPenaltyCountPatterns(runHistory) * PENALTY_N3;
                        runColor = modules[y, x];
                        runX     = 1;
                    }
                }

                result += FinderPenaltyTerminateAndCount(runColor, runX + padRun, runHistory) * PENALTY_N3;
            }

            for (int x = 0; x < size; x++)
            {
                bool runColor = false;
                int  runY     = 0;
                Array.Clear(runHistory, 0, runHistory.Length);
                int padRun = size;
                for (int y = 0; y < size; y++)
                {
                    if (modules[y, x] == runColor)
                    {
                        runY++;
                        if (runY == 5)
                            result += PENALTY_N1;
                        else if (runY > 5) result++;
                    }
                    else
                    {
                        FinderPenaltyAddHistory(runY + padRun, runHistory);
                        padRun = 0;
                        if (!runColor) result += FinderPenaltyCountPatterns(runHistory) * PENALTY_N3;
                        runColor = modules[y, x];
                        runY     = 1;
                    }
                }

                result += FinderPenaltyTerminateAndCount(runColor, runY + padRun, runHistory) * PENALTY_N3;
            }

            for (int y = 0; y < size - 1; y++)
            {
                for (int x = 0; x < size - 1; x++)
                {
                    bool color = modules[y, x];
                    if (color == modules[y, x + 1] &&
                        color == modules[y + 1, x] &&
                        color == modules[y + 1, x + 1])
                        result += PENALTY_N2;
                }
            }

            int black = 0;
            foreach (bool color in modules)
            {
                if (color) black++;
            }

            int total = size * size;
            int k     = (Math.Abs(black * 20 - total * 10) + total - 1) / total - 1;
            result += k * PENALTY_N4;

            return result;
        }

        /// <summary>
        /// Returns an ascending list of positions of alignment patterns for this version number.
        /// Each position is in the range [0,177), and are used on both the x and y axes.
        /// This could be implemented as lookup table of 40 variable-length lists of unsigned bytes.
        /// </summary>
        /// <returns></returns>
        private int[] GetAlignmentPatternPositions()
        {
            if (version == 1) return new int[] { };

            int numAlign = version / 7 + 2;
            int step;
            if (version == 32)
                step = 26;
            else
                step = (version * 4 + numAlign * 2 + 1) / (numAlign * 2 - 2) * 2;
            int[] result = new int[numAlign];
            result[0] = 6;
            for (int i = result.Length - 1, pos = size - 7; i >= 1; i--, pos -= step) result[i] = pos;

            return result;
        }

        /// <summary>
        /// Returns the number of data bits that can be stored in a QR Code of the given version number, after
        /// all function modules are excluded. This includes remainder bits, so it might not be a multiple of 8.
        /// The result is in the range [208, 29648]. This could be implemented as a 40-entry lookup table.
        /// </summary>
        /// <param name="ver"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        private static int GetNumRawDataModules(int ver)
        {
            if (ver < MIN_VERSION || ver > MAX_VERSION) throw new ArgumentException("Version number out of range", nameof(ver));

            int size   = ver * 4 + 17;
            int result = size * size;
            result -= 8 * 8 * 3;
            result -= 15 * 2 + 1;
            result -= (size - 16) * 2;
            if (ver >= 2)
            {
                int numAlign = ver / 7 + 2;
                result -= (numAlign - 1) * (numAlign - 1) * 25;
                result -= (numAlign - 2) * 2 * 20;
                if (ver >= 7) result -= 6 * 3 * 2;
            }

            return result;
        }

        /// <summary>
        /// Returns a Reed-Solomon ECC generator polynomial for the given degree. This could be
        /// implemented as a lookup table over all possible parameter values, instead of as an algorithm.
        /// </summary>
        /// <param name="degree"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        private static byte[] ReedSolomonComputeDivisor(int degree)
        {
            if (degree < 1 || degree > 255) throw new ArgumentException("Degree out of range", nameof(degree));

            byte[] result = new byte[degree];
            result[degree - 1] = 1;

            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < result.Length; j++)
                {
                    result[j] = (byte)ReedSolomonMultiply(result[j] & 0xFF, root);
                    if (j + 1 < result.Length) result[j] ^= result[j + 1];
                }

                root = ReedSolomonMultiply(root, 0x02);
            }

            return result;
        }

        /// <summary>
        /// Returns the Reed-Solomon error correction codeword for the given data and divisor polynomials.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="divisor"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        private static byte[] ReedSolomonComputeRemainder(byte[] data, byte[] divisor)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (divisor is null) throw new ArgumentNullException(nameof(divisor));

            byte[] result = new byte[divisor.Length];
            foreach (byte b in data)
            {
                int factor = (b ^ result[0]) & 0xFF;
                Array.Copy(result, 1, result, 0, result.Length - 1);
                result[result.Length - 1] = 0;
                for (int i = 0; i < result.Length; i++) result[i] ^= Convert.ToByte(ReedSolomonMultiply(divisor[i] & 0xFF, factor));
            }

            return result;
        }

        /// <summary>
        /// Returns the product of the two given field elements modulo GF(2^8/0x11D). The arguments and result
        /// are unsigned 8-bit integers. This could be implemented as a lookup table of 256*256 entries of uint8.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        private static int ReedSolomonMultiply(int x, int y)
        {
            Debug.Assert(x >> 8 == 0 && y >> 8 == 0);
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z =  (int)((z << 1) ^ (((uint)z >> 7) * 0x11D));
                z ^= (int)((((uint)y >> i) & 1) * x);
            }

            Debug.Assert((uint)z >> 8 == 0);

            return z;
        }

        /// <summary>
        /// Returns the number of 8-bit data (i.e. not error correction) codewords contained in any
        /// QR Code of the given version number and error correction level, with remainder bits discarded.
        /// This stateless pure function could be implemented as a (40*4)-cell lookup table.
        /// </summary>
        /// <param name="ver">QR Code version number.</param>
        /// <param name="ecl">Error correction level.</param>
        /// <returns>Number of 8-bit data codewords.</returns>
        public static int GetNumDataCodewords(int ver, Ecc ecl)
        {
            return GetNumRawDataModules(ver) / 8
                   - ECC_CODEWORDS_PER_BLOCK[ecl.formatBits, ver]
                   * NUM_ERROR_CORRECTION_BLOCKS[ecl.formatBits, ver];
        }

        /// <summary>
        /// Can only be called immediately after a white run is added, and
        /// returns either 0, 1, or 2. A helper function for getPenaltyScore().
        /// </summary>
        /// <param name="runHistory"></param>
        /// <returns></returns>
        private int FinderPenaltyCountPatterns(IReadOnlyList<int> runHistory)
        {
            int n = runHistory[1];
            Debug.Assert(n <= size * 3);
            bool core = n > 0 && runHistory[2] == n && runHistory[3] == n * 3 && runHistory[4] == n && runHistory[5] == n;

            return (core && runHistory[0] >= n * 4 && runHistory[6] >= n ? 1 : 0)
                   + (core && runHistory[6] >= n * 4 && runHistory[0] >= n ? 1 : 0);
        }

        /// <summary>
        /// Must be called at the end of a line (row or column) of modules. A helper function for getPenaltyScore().
        /// </summary>
        /// <param name="currentRunColor"></param>
        /// <param name="currentRunLength"></param>
        /// <param name="runHistory"></param>
        /// <returns></returns>
        private int FinderPenaltyTerminateAndCount(bool currentRunColor, int currentRunLength, int[] runHistory)
        {
            if (currentRunColor)
            {
                FinderPenaltyAddHistory(currentRunLength, runHistory);
                currentRunLength = 0;
            }

            currentRunLength += size;
            FinderPenaltyAddHistory(currentRunLength, runHistory);

            return FinderPenaltyCountPatterns(runHistory);
        }

        /// <summary>
        /// Pushes the given value to the front and drops the last value. A helper function for getPenaltyScore().
        /// </summary>
        /// <param name="currentRunLength"></param>
        /// <param name="runHistory"></param>
        private static void FinderPenaltyAddHistory(int currentRunLength, int[] runHistory)
        {
            Array.Copy(runHistory, 0, runHistory, 1, runHistory.Length - 1);
            runHistory[0] = currentRunLength;
        }

        /// <summary>
        /// Returns true if the i'th bit of x is set to 1.
        /// </summary>
        /// <param name="x">Integer that is bit-checked.</param>
        /// <param name="i">Index of the bit that is checked.</param>
        /// <returns>Value of the i'th bit of x.</returns>
        public static bool GetBit(int x, int i)
        {
            return (((uint)x >> i) & 1) != 0;
        }

        public const int MIN_VERSION = 1;

        /// <summary>
        /// The maximum version number (40) supported in the QR Code Model 2 standard.
        /// </summary>
        public const int MAX_VERSION = 40;


        // For use in getPenaltyScore(), when evaluating which mask is best.
        private const int PENALTY_N1 = 3;
        private const int PENALTY_N2 = 3;
        private const int PENALTY_N3 = 40;
        private const int PENALTY_N4 = 10;

        // Sorted by Ecc.formatBit
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

        // Sorted by Ecc.formatBit
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

        /// <summary>
        /// The error correction level in a QR Code symbol.
        /// </summary>
        public class Ecc
        {
            /// <summary>
            /// The QR Code can tolerate about 7% erroneous codewords.
            /// </summary>
            public static readonly Ecc LOW = new Ecc(1);

            /// <summary>
            /// The QR Code can tolerate about 15% erroneous codewords.
            /// </summary>
            public static readonly Ecc MEDIUM = new Ecc(0);

            /// <summary>
            /// The QR Code can tolerate about 25% erroneous codewords.
            /// </summary>
            public static readonly Ecc QUARTILE = new Ecc(3);

            /// <summary>
            /// The QR Code can tolerate about 30% erroneous codewords.
            /// </summary>
            public static readonly Ecc HIGH = new Ecc(2);

            /// <summary>
            /// Helper method to allow java-enum-like iteration through the error levels.
            /// </summary>
            /// <returns></returns>
            public static IEnumerable<Ecc> Values()
            {
                return new[] {
                    LOW, MEDIUM, QUARTILE, HIGH
                };
            }

            /// <summary>
            /// In the range 0 to 3 (unsigned 2-bit integer).
            /// </summary>
            public readonly int formatBits;

            private Ecc(int fb)
            {
                formatBits = fb;
            }
        }
    }
}

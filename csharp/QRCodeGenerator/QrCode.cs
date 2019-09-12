using System;
using System.Collections.Generic;

namespace nayuki.qrcodegen
{
    public class QrCode
    {
        public static QrCode EncodeText(string text, Ecc ecl)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (ecl is null) throw new ArgumentNullException(nameof(ecl));

            IList<QrSegment> segments = QrSegment.makeSegments(text);

            return encodeSegments(segments, ecl);
        }

        public struct Ecc
        {
            public static Ecc LOW()
            {
                return new Ecc(1);
            }

            public static Ecc MEDIUM()
            {
                return new Ecc(0);
            }

            public static Ecc QUARTILE()
            {
                return new Ecc(3);
            }

            public static Ecc HIGH()
            {
                return new Ecc(2);
            }

            public int formatBits;

            private Ecc(int fb)
            {
                formatBits = fb;
            }
        }
    }
}

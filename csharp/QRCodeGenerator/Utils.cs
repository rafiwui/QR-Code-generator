using System.Collections.Generic;

namespace nayuki.qrcodegen
{
    public static class Utils
    {
        public static T[] CopyArrayRange<T>(IReadOnlyList<T> src, int start, int end)
        {
            int len  = end - start;
            var dest = new T[len];
            for (int i = 0; i < len; i++)
            {
                dest[i] = src[start + i];
            }

            return dest;
        }

        public static T[] CopyArrayWithNewLength<T>(T[] src, int newLength)
        {
            var dest = new T[newLength];
            for (int i = 0; i < newLength; i++)
            {
                if (i < src.Length)
                    dest[i] = src[i];
                else
                    break;
            }

            return dest;
        }
    }
}

using System;

namespace nayuki.qrcodegen
{
    public class DataTooLongException : Exception
    {
        public DataTooLongException() { }
        public DataTooLongException(string msg) : base(msg) { }
        public DataTooLongException(string msg, Exception inner) : base(msg, inner) { }
    }
}

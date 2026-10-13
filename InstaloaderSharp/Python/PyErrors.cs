using System;

namespace InstaloaderSharp.Python
{
    /// <summary>Base class of the exceptions that correspond to Python's built-in exceptions.</summary>
    public abstract class PyException : Exception
    {
        protected PyException(string message) : base(message ?? "") { }
        protected PyException(string message, Exception inner) : base(message ?? "", inner) { }

        /// <summary>Python class name of the exception, e.g. KeyError.</summary>
        public abstract string PythonName { get; }
    }

    /// <summary>Python's KeyError. Its message is the repr() of the missing key, like str(KeyError(key)).</summary>
    public class PyKeyError : PyException
    {
        public PyKeyError(object key) : base(PyStr.Repr(key)) { Key = key; }
        public object Key { get; private set; }
        public override string PythonName { get { return "KeyError"; } }
    }

    public class PyIndexError : PyException
    {
        public PyIndexError(string message) : base(message) { }
        public override string PythonName { get { return "IndexError"; } }
    }

    public class PyTypeError : PyException
    {
        public PyTypeError(string message) : base(message) { }
        public override string PythonName { get { return "TypeError"; } }
    }

    public class PyValueError : PyException
    {
        public PyValueError(string message) : base(message) { }
        public PyValueError(string message, Exception inner) : base(message, inner) { }
        public override string PythonName { get { return "ValueError"; } }
    }

    public class PyAttributeError : PyException
    {
        public PyAttributeError(string message) : base(message) { }
        public override string PythonName { get { return "AttributeError"; } }
    }

    public class PyZeroDivisionError : PyException
    {
        public PyZeroDivisionError(string message) : base(message) { }
        public override string PythonName { get { return "ZeroDivisionError"; } }
    }

    public class PyOverflowError : PyException
    {
        public PyOverflowError(string message) : base(message) { }
        public override string PythonName { get { return "OverflowError"; } }
    }

    /// <summary>json.decoder.JSONDecodeError (a subclass of ValueError).</summary>
    public class JsonDecodeError : PyValueError
    {
        public JsonDecodeError(string msg, string doc, int pos)
            : base(Format(msg, doc, pos))
        {
            Msg = msg;
            Pos = pos;
        }

        public string Msg { get; private set; }
        public int Pos { get; private set; }

        private static string Format(string msg, string doc, int pos)
        {
            int lineno = 1;
            int lastNewline = -1;
            for (int i = 0; i < pos && doc != null && i < doc.Length; i++)
            {
                if (doc[i] == '\n')
                {
                    lineno++;
                    lastNewline = i;
                }
            }
            int colno = pos - lastNewline;
            return string.Format("{0}: line {1} column {2} (char {3})", msg, lineno, colno, pos);
        }
    }
}

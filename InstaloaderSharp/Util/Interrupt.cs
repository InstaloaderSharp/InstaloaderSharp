using System;
using System.Threading;

namespace InstaloaderSharp.Util
{
    /// <summary>
    /// Emulates Python's KeyboardInterrupt: once installed, hitting ^C does not terminate the process, but causes
    /// a <see cref="KeyboardInterruptException"/> at the next interruption point (sleeps, network transfers,
    /// iteration steps).
    /// </summary>
    public static class Interrupt
    {
        private static readonly ManualResetEvent Signal = new ManualResetEvent(false);
        private static volatile bool _pending;
        private static bool _installed;
        private static readonly object Lock = new object();

        /// <summary>Raised (on a thread pool thread) when ^C is pressed, e.g. to abort network transfers.</summary>
        public static event Action Interrupted;

        /// <summary>Installs the ^C handler. Idempotent.</summary>
        public static void Install()
        {
            lock (Lock)
            {
                if (_installed)
                    return;
                _installed = true;
            }
            Console.CancelKeyPress += (sender, e) =>
            {
                if (e.SpecialKey == ConsoleSpecialKey.ControlBreak)
                    return; // Ctrl+Break terminates
                e.Cancel = true;
                Raise();
            };
        }

        /// <summary>Requests an interruption, as if ^C had been pressed.</summary>
        public static void Raise()
        {
            _pending = true;
            Signal.Set();
            Action handler = Interrupted;
            if (handler != null)
            {
                try
                {
                    handler();
                }
                catch (Exception)
                {
                    // ignore
                }
            }
        }

        /// <summary>Whether an interruption is pending.</summary>
        public static bool IsPending { get { return _pending; } }

        /// <summary>Throws <see cref="KeyboardInterruptException"/> if ^C has been pressed since the last check.</summary>
        public static void Check()
        {
            if (_pending)
            {
                _pending = false;
                Signal.Reset();
                throw new KeyboardInterruptException();
            }
        }

        /// <summary>Python's time.sleep(): sleeps, but is interruptible by ^C.</summary>
        public static void Sleep(double seconds)
        {
            Check();
            if (seconds > 0)
            {
                double ms = seconds * 1000.0;
                if (ms > int.MaxValue)
                    ms = int.MaxValue;
                Signal.WaitOne(TimeSpan.FromMilliseconds(ms));
            }
            Check();
        }
    }
}

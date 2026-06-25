using log4net;
using System;
using System.Diagnostics;
using System.Threading;

namespace SW2URDF.Utilities
{
    internal sealed class OperationHeartbeat : IDisposable
    {
        private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(15);

        private readonly ILog logger;
        private readonly string operationName;
        private readonly Stopwatch stopwatch;
        private readonly Timer timer;
        private int disposed;

        private OperationHeartbeat(ILog logger, string operationName, TimeSpan interval)
        {
            if (logger == null)
            {
                throw new ArgumentNullException("logger");
            }
            if (String.IsNullOrWhiteSpace(operationName))
            {
                throw new ArgumentException("Operation name is required.", "operationName");
            }
            if (interval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException("interval");
            }

            this.logger = logger;
            this.operationName = operationName;
            stopwatch = Stopwatch.StartNew();

            logger.Info(operationName + " started");
            timer = new Timer(LogHeartbeat, null, interval, interval);
        }

        public static OperationHeartbeat Start(ILog logger, string operationName)
        {
            return new OperationHeartbeat(logger, operationName, DefaultInterval);
        }

        private void LogHeartbeat(object state)
        {
            if (Interlocked.CompareExchange(ref disposed, 0, 0) != 0)
            {
                return;
            }

            logger.Info(operationName + " still running; elapsed " +
                FormatElapsed(stopwatch.Elapsed));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            timer.Dispose();
            stopwatch.Stop();
            logger.Info(operationName + " finished; elapsed " +
                FormatElapsed(stopwatch.Elapsed));
        }

        internal static string FormatElapsed(TimeSpan elapsed)
        {
            if (elapsed.TotalHours >= 1.0)
            {
                return elapsed.ToString(@"hh\:mm\:ss");
            }

            return elapsed.ToString(@"mm\:ss");
        }
    }
}

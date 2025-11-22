using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

namespace OmniMouse.Diagnostics
{
    internal static class Logger
    {
        private const int MaxLogEntries = 10000;
        private static readonly string[] _buffer = new string[MaxLogEntries];
        private static int _index = 0;
        private static readonly object _lock = new object();

        private const int MaxLogExceptionPerHour = 1000;
        private static int _lastHour = DateTime.UtcNow.Hour;
        private static int _exceptionCount = 0;

        public static void Log(
            string message,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string sourceFilePath = "",
            [CallerLineNumber] int sourceLineNumber = 0)
        {
            var stamp = DateTime.Now.ToString("MM/dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            var tid = Thread.CurrentThread.ManagedThreadId;
            var formatted = $"{stamp}({tid}){message}";

            // Console + Debug sink (keeps existing experience)
            Console.WriteLine(formatted);
            Debug.WriteLine(formatted);

            lock (_lock)
            {
                _buffer[_index] = formatted;
                _index = (_index + 1) % MaxLogEntries;
            }
        }

        public static void Log(Exception ex,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string sourceFilePath = "",
            [CallerLineNumber] int sourceLineNumber = 0)
        {
            try
            {
                var hour = DateTime.UtcNow.Hour;
                if (hour != _lastHour)
                {
                    _lastHour = hour;
                    _exceptionCount = 0;
                }

                if (_exceptionCount < MaxLogExceptionPerHour)
                {
                    _exceptionCount++;
                    Log($"[EX] {ex}", memberName, sourceFilePath, sourceLineNumber);
                }
            }
            catch
            {
                // never throw from logger
            }
        }

        [Conditional("DEBUG")]
        public static void LogDebug(
            string message,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string sourceFilePath = "",
            [CallerLineNumber] int sourceLineNumber = 0)
        {
            Log(message, memberName, sourceFilePath, sourceLineNumber);
        }

        // Placeholder for telemetry if you adopt one later
        public static void TelemetryLogTrace(string message, string severity = "Info")
        {
            Log($"[TELEMETRY][{severity}] {message}");
        }

        public static void DumpRecentLogs(string filePath)
        {
            try
            {
                var lines = new List<string>(MaxLogEntries);
                lock (_lock)
                {
                    for (int i = 0; i < MaxLogEntries; i++)
                    {
                        int idx = (_index + i) % MaxLogEntries;
                        var entry = _buffer[idx];
                        if (!string.IsNullOrEmpty(entry))
                            lines.Add(entry);
                    }
                }
                File.WriteAllLines(filePath, lines);
            }
            catch (Exception ex)
            {
                // Fallback to console
                Console.WriteLine($"[Logger] Failed to dump logs: {ex.Message}");
            }
        }
    }
}

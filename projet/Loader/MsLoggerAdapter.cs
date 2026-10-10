using System;
using Microsoft.Extensions.Logging;

namespace EllyrLoader
{
    // Journal exigé par Il2CppInterop : on ne garde que les avertissements et les erreurs.
    internal sealed class MsLoggerAdapter : ILogger
    {
        private sealed class EmptyScope : IDisposable
        {
            public void Dispose() { }
        }

        private readonly string _category;

        public MsLoggerAdapter(string category)
        {
            _category = category;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return new EmptyScope();
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= LogLevel.Warning;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            string line = "[" + _category + "] " + (state == null ? string.Empty : state.ToString());
            if (exception != null)
                line += Environment.NewLine + exception;

            if (logLevel >= LogLevel.Error)
                LoaderLog.Error(line);
            else
                LoaderLog.Warning(line);
        }
    }
}

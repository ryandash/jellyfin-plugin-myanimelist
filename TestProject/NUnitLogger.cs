using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace UnitTestProject;

public sealed class NUnitLogger : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
        => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        TestContext.Progress.WriteLine(
            $"{logLevel}: {formatter(state, exception)}");

        if (exception != null)
        {
            TestContext.Progress.WriteLine(exception.ToString());
        }
    }
}

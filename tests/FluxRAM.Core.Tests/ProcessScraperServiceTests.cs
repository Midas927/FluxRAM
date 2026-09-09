using System.Reflection;
using FluxRAM.Core.Services;
using Xunit;

namespace FluxRAM.Core.Tests;

public sealed class ProcessScraperServiceTests
{
    private static readonly DateTimeOffset StartedAt = DateTimeOffset.Parse("2026-09-09T00:00:00Z");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ActivitySample_FirstReadIsUnknownButMeasuredZeroIsValid(bool cpu)
    {
        var service = new ProcessScraperService();

        Assert.Null(RecordSample(service, cpu, 1000, StartedAt));
        Assert.Equal(0d, RecordSample(service, cpu, 1000, StartedAt.AddSeconds(1)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ActivitySample_ComputesRateFromValidCounterDelta(bool cpu)
    {
        var service = new ProcessScraperService();
        RecordSample(service, cpu, 1000, StartedAt);

        var measured = RecordSample(service, cpu, 2000, StartedAt.AddSeconds(1));

        Assert.Equal(cpu ? 100d / Environment.ProcessorCount : 1000d, Assert.IsType<double>(measured), 10);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ActivitySample_ReadFailureClearsOnlyThatBaselineAndRequiresFreshPair(bool cpu)
    {
        var service = new ProcessScraperService();
        RecordSample(service, cpu, 1000, StartedAt);
        RecordSample(service, !cpu, 1000, StartedAt);
        RecordSample(service, cpu, 1000, StartedAt, processId: 11);
        Assert.Equal(0d, RecordSample(service, cpu, 1000, StartedAt.AddSeconds(1)));

        Assert.Null(RecordSample(service, cpu, null, StartedAt.AddSeconds(2)));
        Assert.Equal(0d, RecordSample(service, !cpu, 1000, StartedAt.AddSeconds(2)));
        Assert.Equal(0d, RecordSample(service, cpu, 1000, StartedAt.AddSeconds(2), processId: 11));
        Assert.Null(RecordSample(service, cpu, 1000, StartedAt.AddSeconds(3)));
        Assert.Equal(0d, RecordSample(service, cpu, 1000, StartedAt.AddSeconds(4)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ActivitySample_CounterRegressionIsUnknownAndRebaselines(bool cpu)
    {
        var service = new ProcessScraperService();
        RecordSample(service, cpu, 1000, StartedAt);

        Assert.Null(RecordSample(service, cpu, 500, StartedAt.AddSeconds(1)));
        Assert.Equal(0d, RecordSample(service, cpu, 500, StartedAt.AddSeconds(2)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ActivitySample_ShortIntervalsDoNotDiscardAccumulatedActivity(bool cpu)
    {
        var service = new ProcessScraperService();
        RecordSample(service, cpu, 0, StartedAt);

        Assert.Null(RecordSample(service, cpu, 50, StartedAt.AddMilliseconds(50)));
        Assert.Null(RecordSample(service, cpu, 100, StartedAt.AddMilliseconds(100)));
        var measured = RecordSample(service, cpu, 150, StartedAt.AddMilliseconds(150));
        Assert.Equal(cpu ? 100d / Environment.ProcessorCount : 1000d, Assert.IsType<double>(measured), 10);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(true, -1)]
    [InlineData(false, 0)]
    [InlineData(false, -1)]
    public void ActivitySample_NonIncreasingTimestampIsUnknownAndRebaselines(bool cpu, int seconds)
    {
        var service = new ProcessScraperService();
        RecordSample(service, cpu, 1000, StartedAt);
        var resetAt = StartedAt.AddSeconds(seconds);

        Assert.Null(RecordSample(service, cpu, 2000, resetAt));
        Assert.Equal(0d, RecordSample(service, cpu, 2000, resetAt.AddSeconds(1)));
    }

    private static double? RecordSample(
        ProcessScraperService service,
        bool cpu,
        long? total,
        DateTimeOffset sampledAt,
        int processId = 10)
    {
        // Exercise counter transitions without querying or manipulating live processes.
        var method = typeof(ProcessScraperService).GetMethod(
            cpu ? "RecordCpuSample" : "RecordIoSample",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        object? counter = total.HasValue
            ? cpu ? (object)TimeSpan.FromMilliseconds(total.Value) : (ulong)total.Value
            : null;
        return (double?)method.Invoke(service, new object?[] { processId, counter, sampledAt });
    }
}

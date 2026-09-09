using System.Reflection;
using System.Runtime.InteropServices;
using FluxRAM.Core.Models;
using FluxRAM.Core.Services;
using Xunit;

namespace FluxRAM.Core.Tests;

public sealed class MemoryPurgeServiceTests
{
    [Theory]
    [InlineData(null, 800L, 600L, 700L, 500L, null)]
    [InlineData(1000L, null, 800L, 600L, 700L, 600L)]
    [InlineData(1000L, 800L, null, 700L, null, 700L)]
    [InlineData(1000L, null, null, null, null, null)]
    [InlineData(1000L, 800L, 700L, 900L, 850L, 700L)]
    [InlineData(1000L, 0L, null, null, null, 0L)]
    [InlineData(1000L, null, null, 0L, null, 0L)]
    [InlineData(1000L, null, null, null, 600L, 600L)]
    [InlineData(0L, 0L, 0L, 0L, 0L, 0L)]
    [InlineData(0L, 100L, null, 200L, null, 100L)]
    [InlineData(1000L, 1000L, 1000L, 1000L, 1000L, 1000L)]
    [InlineData(1000L, 1200L, 1300L, null, null, 1200L)]
    public void Purge_RequiresBaselineAndUsesOnlyValidPostTrimSamples(
        long? before, long? first, long? second, long? third, long? fourth, long? expectedAfter)
    {
        var samples = new Queue<long?>([before, first, second, third, fourth]);
        var calls = new List<string>();

        var result = Purge(
            () => { calls.Add("read"); return samples.Dequeue(); },
            () => { calls.Add("trim"); return true; },
            () => { calls.Add("flush"); return true; },
            milliseconds => { Assert.Equal(30, milliseconds); calls.Add("wait"); });

        Assert.Equal(10, result.ProcessId);
        Assert.Equal(before.HasValue, result.Success);
        Assert.Equal(before.HasValue && expectedAfter.HasValue, result.HasMeasurement);
        Assert.Equal(before ?? 0L, result.BeforeWorkingSetBytes);
        Assert.Equal(expectedAfter ?? 0L, result.AfterWorkingSetBytes);
        Assert.Equal((before - expectedAfter) ?? 0L, result.DeltaBytes);
        if (before.HasValue)
        {
            Assert.Null(result.ErrorMessage);
            Assert.Empty(samples);
            Assert.Equal(
                new[] { "read", "trim", "flush", "read", "wait", "read", "wait", "read", "wait", "read" },
                calls);
        }
        else
        {
            Assert.Contains("before trim", result.ErrorMessage);
            Assert.Equal(4, samples.Count);
            Assert.Equal(new[] { "read" }, calls);
        }
    }

    [Theory]
    [InlineData(false, true, 5)]
    [InlineData(true, false, 6)]
    [InlineData(false, false, 5)]
    public void Purge_FailedActionRemainsFailureAndDoesNotSampleAfterwards(
        bool trimSucceeded, bool flushSucceeded, int expectedError)
    {
        var calls = new List<string>();

        var result = Purge(
            () => { calls.Add("read"); return 1000L; },
            () => { calls.Add("trim"); Marshal.SetLastPInvokeError(5); return trimSucceeded; },
            () => { calls.Add("flush"); Marshal.SetLastPInvokeError(6); return flushSucceeded; },
            _ => calls.Add("wait"));

        Assert.False(result.Success);
        Assert.False(result.HasMeasurement);
        Assert.Equal(0L, result.DeltaBytes);
        Assert.Contains($"Win32Error={expectedError}", result.ErrorMessage);
        Assert.Equal(new[] { "read", "trim", "flush" }, calls);
    }

    [Fact]
    public void ReadWorkingSetBytes_InvalidHandleReturnsUnknown()
    {
        var method = typeof(MemoryPurgeService).GetMethod(
            "ReadWorkingSetBytes", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        Assert.Null(method.Invoke(null, new object[] { IntPtr.Zero }));
    }

    [Fact]
    public void Result_PreservesLongFieldsAndSeparatesActionSuccessFromMeasurement()
    {
        var measured = new MemoryPurgeResult(10, true, 1000L, 0L, null);
        Assert.True(measured.HasMeasurement);
        Assert.Equal(1000L, measured.DeltaBytes);
        Assert.Equal(measured, MemoryPurgeResult.Succeeded(10, 1000L, 0L));

        var unknown = MemoryPurgeResult.SucceededWithoutMeasurement(10, 1000L);
        Assert.True(unknown.Success);
        Assert.False(unknown.HasMeasurement);
        Assert.Equal(10, unknown.ProcessId);
        Assert.Equal(1000L, unknown.BeforeWorkingSetBytes);
        Assert.Equal(0L, unknown.AfterWorkingSetBytes);
        Assert.Equal(0L, unknown.DeltaBytes);
        Assert.Null(unknown.ErrorMessage);
        Assert.Equal(measured with { HasMeasurement = false }, unknown);
        Assert.Equal(0L, (measured with { AfterWorkingSetBytes = 250L, HasMeasurement = false }).DeltaBytes);

        var failed = MemoryPurgeResult.Failed(10, "Read failed");
        Assert.False(failed.Success);
        Assert.False(failed.HasMeasurement);
        Assert.Equal(10, failed.ProcessId);
        Assert.Equal(0L, failed.BeforeWorkingSetBytes);
        Assert.Equal(0L, failed.AfterWorkingSetBytes);
        Assert.Equal(0L, failed.DeltaBytes);
        Assert.Equal("Read failed", failed.ErrorMessage);
    }

    private static MemoryPurgeResult Purge(
        Func<long?> readWorkingSetBytes,
        Func<bool> trimWorkingSet,
        Func<bool> emptyWorkingSet,
        Action<int> sleep)
    {
        // Invoke the same flow as production without opening or trimming any process.
        var method = typeof(MemoryPurgeService).GetMethod(
            "PurgeWorkingSet", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<MemoryPurgeResult>(method.Invoke(
            null, new object[] { 10, readWorkingSetBytes, trimWorkingSet, emptyWorkingSet, sleep }));
    }
}

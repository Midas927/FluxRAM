using System.Diagnostics;
using FluxRAM.Core.Interop;
using FluxRAM.Core.Models;

namespace FluxRAM.Core.Services;

public sealed class MemoryStatusService
{
    private CpuSample? _lastCpuSample;

    public bool TryGetAvailablePhysicalMemory(out ulong availablePhysicalMemoryBytes)
    {
        var status = NativeMethods.CreateMemoryStatusEx();
        if (!NativeMethods.GlobalMemoryStatusEx(ref status))
        {
            availablePhysicalMemoryBytes = 0;
            return false;
        }

        availablePhysicalMemoryBytes = status.ullAvailPhys;
        return true;
    }

    public bool TryGetSnapshot(out MemorySnapshot memorySnapshot)
    {
        var status = NativeMethods.CreateMemoryStatusEx();
        if (!NativeMethods.GlobalMemoryStatusEx(ref status))
        {
            memorySnapshot = default;
            return false;
        }

        memorySnapshot = new MemorySnapshot(
            status.ullAvailPhys,
            status.ullTotalPhys,
            status.dwMemoryLoad);
        return true;
    }

    public bool TryGetSelfOverhead(out AppOverheadSnapshot overheadSnapshot)
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var sampledAt = DateTimeOffset.UtcNow;
            var cpuUsagePercent = CalculateCpuPercent(process, sampledAt);

            overheadSnapshot = new AppOverheadSnapshot(
                cpuUsagePercent,
                process.WorkingSet64,
                process.PrivateMemorySize64,
                process.HandleCount);
            return true;
        }
        catch
        {
            overheadSnapshot = default;
            return false;
        }
    }

    private double CalculateCpuPercent(Process process, DateTimeOffset sampledAt)
    {
        var totalProcessorTime = process.TotalProcessorTime;
        var lastSample = _lastCpuSample;

        _lastCpuSample = new CpuSample(totalProcessorTime, sampledAt);
        if (!lastSample.HasValue)
        {
            return 0d;
        }

        var elapsed = sampledAt - lastSample.Value.SampledAt;
        if (elapsed <= TimeSpan.FromMilliseconds(100))
        {
            return 0d;
        }

        var delta = totalProcessorTime - lastSample.Value.TotalProcessorTime;
        if (delta < TimeSpan.Zero)
        {
            return 0d;
        }

        var cpuPercent = delta.TotalMilliseconds / (elapsed.TotalMilliseconds * Environment.ProcessorCount) * 100d;
        return Math.Clamp(cpuPercent, 0d, 100d);
    }

    private readonly record struct CpuSample(TimeSpan TotalProcessorTime, DateTimeOffset SampledAt);
}

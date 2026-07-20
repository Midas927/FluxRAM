using System.Diagnostics;
using System.Runtime.InteropServices;
using FluxRAM.Core.Interop;
using FluxRAM.Core.Models;

namespace FluxRAM.Core.Services;

public sealed class ProcessScraperService
{
    private readonly Dictionary<int, CpuSample> _cpuSamplesByProcessId = new();
    private readonly Dictionary<int, IoSample> _ioSamplesByProcessId = new();

    public IReadOnlyList<ProcessSnapshot> Scrape(
        IReadOnlyDictionary<int, DateTimeOffset>? lastPurgeTimesByProcessId = null)
    {
        var sampledAt = DateTimeOffset.UtcNow;
        var foregroundProcessId = TryGetForegroundProcessId();
        var parentProcessIds = LoadParentProcessIds();
        var snapshots = new List<ProcessSnapshot>();
        var seenProcessIds = new HashSet<int>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                seenProcessIds.Add(process.Id);
                var processName = TryGetProcessName(process);
                if (string.IsNullOrWhiteSpace(processName))
                {
                    continue;
                }

                if (SystemProcessWhitelist.Contains(processName))
                {
                    continue;
                }

                var workingSetBytes = TryGetWorkingSet(process);
                var isForeground = foregroundProcessId.HasValue && process.Id == foregroundProcessId.Value;
                var hasVisibleWindow = TryHasVisibleWindow(process);
                var cpuUsagePercent = TryGetCpuUsagePercent(process, sampledAt);
                var ioBytesPerSecond = TryGetIoBytesPerSecond(process.Id, sampledAt);
                var wasRecentlyPurged = WasRecentlyPurged(process.Id, sampledAt, lastPurgeTimesByProcessId);
                var executablePath = TryGetExecutablePath(process);
                var parentProcessId = parentProcessIds.TryGetValue(process.Id, out var parentId) ? parentId : null;
                var mainWindowTitle = TryGetMainWindowTitle(process);
                var coldnessScore = CalculateColdnessScore(
                    workingSetBytes,
                    cpuUsagePercent,
                    ioBytesPerSecond,
                    isForeground,
                    hasVisibleWindow,
                    wasRecentlyPurged);

                snapshots.Add(
                    new ProcessSnapshot(
                        process.Id,
                        processName,
                        workingSetBytes,
                        isForeground,
                        cpuUsagePercent,
                        hasVisibleWindow,
                        coldnessScore,
                        executablePath,
                        ioBytesPerSecond,
                        parentProcessId,
                        mainWindowTitle));
            }
        }

        CleanupSamples(seenProcessIds);

        return snapshots
            .OrderByDescending(snapshot => snapshot.ColdnessScore)
            .ThenByDescending(snapshot => snapshot.WorkingSetBytes)
            .ToArray();
    }

    private double TryGetCpuUsagePercent(Process process, DateTimeOffset sampledAt)
    {
        TimeSpan totalProcessorTime;
        try
        {
            totalProcessorTime = process.TotalProcessorTime;
        }
        catch
        {
            return 0d;
        }

        if (!_cpuSamplesByProcessId.TryGetValue(process.Id, out var previous))
        {
            _cpuSamplesByProcessId[process.Id] = new CpuSample(totalProcessorTime, sampledAt);
            return 0d;
        }

        var elapsed = sampledAt - previous.SampledAt;
        if (elapsed <= TimeSpan.FromMilliseconds(100))
        {
            _cpuSamplesByProcessId[process.Id] = new CpuSample(totalProcessorTime, sampledAt);
            return 0d;
        }

        var processorDelta = totalProcessorTime - previous.TotalProcessorTime;
        if (processorDelta < TimeSpan.Zero)
        {
            processorDelta = TimeSpan.Zero;
        }

        var cpuPercent = processorDelta.TotalMilliseconds / (elapsed.TotalMilliseconds * Environment.ProcessorCount) * 100d;
        _cpuSamplesByProcessId[process.Id] = new CpuSample(totalProcessorTime, sampledAt);
        return Math.Clamp(cpuPercent, 0d, 100d);
    }

    private static bool WasRecentlyPurged(
        int processId,
        DateTimeOffset sampledAt,
        IReadOnlyDictionary<int, DateTimeOffset>? lastPurgeTimesByProcessId)
    {
        if (lastPurgeTimesByProcessId is null)
        {
            return false;
        }

        if (!lastPurgeTimesByProcessId.TryGetValue(processId, out var lastPurgeAt))
        {
            return false;
        }

        return sampledAt - lastPurgeAt < TimeSpan.FromMinutes(2);
    }

    private static double CalculateColdnessScore(
        long workingSetBytes,
        double cpuUsagePercent,
        double ioBytesPerSecond,
        bool isForeground,
        bool hasVisibleWindow,
        bool wasRecentlyPurged)
    {
        var score = 0d;

        if (!isForeground)
        {
            score += 35d;
        }

        if (!hasVisibleWindow)
        {
            score += 15d;
        }

        if (cpuUsagePercent <= 0.5d)
        {
            score += 25d;
        }
        else if (cpuUsagePercent <= 2d)
        {
            score += 15d;
        }
        else if (cpuUsagePercent > 8d)
        {
            score -= 20d;
        }

        if (ioBytesPerSecond <= 32 * 1024d)
        {
            score += 12d;
        }
        else if (ioBytesPerSecond <= 256 * 1024d)
        {
            score += 6d;
        }
        else if (ioBytesPerSecond >= 2 * 1024d * 1024d)
        {
            score -= 18d;
        }

        if (workingSetBytes >= 512L * 1024 * 1024)
        {
            score += 20d;
        }
        else if (workingSetBytes >= 256L * 1024 * 1024)
        {
            score += 10d;
        }

        if (wasRecentlyPurged)
        {
            score -= 20d;
        }

        return Math.Clamp(score, 0d, 100d);
    }

    private void CleanupCpuSamples(IReadOnlySet<int> seenProcessIds)
    {
        CleanupSamples(seenProcessIds);
    }

    private void CleanupSamples(IReadOnlySet<int> seenProcessIds)
    {
        if (_cpuSamplesByProcessId.Count == 0 && _ioSamplesByProcessId.Count == 0)
        {
            return;
        }

        var staleProcessIds = _cpuSamplesByProcessId.Keys
            .Where(processId => !seenProcessIds.Contains(processId))
            .ToArray();

        foreach (var processId in staleProcessIds)
        {
            _cpuSamplesByProcessId.Remove(processId);
            _ioSamplesByProcessId.Remove(processId);
        }

        var staleIoProcessIds = _ioSamplesByProcessId.Keys
            .Where(processId => !seenProcessIds.Contains(processId))
            .ToArray();
        foreach (var processId in staleIoProcessIds)
        {
            _ioSamplesByProcessId.Remove(processId);
        }
    }

    private double TryGetIoBytesPerSecond(int processId, DateTimeOffset sampledAt)
    {
        var processHandle = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION,
            false,
            processId);
        if (processHandle == IntPtr.Zero)
        {
            return 0d;
        }

        try
        {
            if (!NativeMethods.GetProcessIoCounters(processHandle, out var ioCounters))
            {
                return 0d;
            }

            var totalBytes = ioCounters.ReadTransferCount + ioCounters.WriteTransferCount;
            if (!_ioSamplesByProcessId.TryGetValue(processId, out var previous))
            {
                _ioSamplesByProcessId[processId] = new IoSample(totalBytes, sampledAt);
                return 0d;
            }

            var elapsed = sampledAt - previous.SampledAt;
            if (elapsed <= TimeSpan.FromMilliseconds(100))
            {
                _ioSamplesByProcessId[processId] = new IoSample(totalBytes, sampledAt);
                return 0d;
            }

            var deltaBytes = totalBytes >= previous.TotalBytes
                ? totalBytes - previous.TotalBytes
                : 0UL;
            _ioSamplesByProcessId[processId] = new IoSample(totalBytes, sampledAt);
            return deltaBytes / elapsed.TotalSeconds;
        }
        finally
        {
            _ = NativeMethods.CloseHandle(processHandle);
        }
    }

    private static bool TryHasVisibleWindow(Process process)
    {
        try
        {
            var windowHandle = process.MainWindowHandle;
            return windowHandle != IntPtr.Zero &&
                NativeMethods.IsWindowVisible(windowHandle) &&
                !NativeMethods.IsIconic(windowHandle);
        }
        catch
        {
            return false;
        }
    }

    private static string? TryGetExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetMainWindowTitle(Process process)
    {
        try
        {
            var title = process.MainWindowTitle;
            return string.IsNullOrWhiteSpace(title) ? null : title;
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyDictionary<int, int?> LoadParentProcessIds()
    {
        var parentProcessIds = new Dictionary<int, int?>();
        var snapshotHandle = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPPROCESS, 0);
        if (snapshotHandle == IntPtr.Zero || snapshotHandle == new IntPtr(-1))
        {
            return parentProcessIds;
        }

        try
        {
            var entry = new NativeMethods.PROCESSENTRY32
            {
                dwSize = (uint)Marshal.SizeOf<NativeMethods.PROCESSENTRY32>()
            };

            if (!NativeMethods.Process32First(snapshotHandle, ref entry))
            {
                return parentProcessIds;
            }

            do
            {
                if (entry.th32ProcessID <= int.MaxValue)
                {
                    parentProcessIds[(int)entry.th32ProcessID] = entry.th32ParentProcessID <= int.MaxValue
                        ? (int)entry.th32ParentProcessID
                        : null;
                }
            }
            while (NativeMethods.Process32Next(snapshotHandle, ref entry));

            return parentProcessIds;
        }
        finally
        {
            _ = NativeMethods.CloseHandle(snapshotHandle);
        }
    }

    private static int? TryGetForegroundProcessId()
    {
        var foregroundWindow = NativeMethods.GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
        {
            return null;
        }

        var status = NativeMethods.GetWindowThreadProcessId(foregroundWindow, out var processId);
        if (status == 0 || processId == 0)
        {
            return null;
        }

        return (int)processId;
    }

    private static string? TryGetProcessName(Process process)
    {
        try
        {
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private static long TryGetWorkingSet(Process process)
    {
        try
        {
            return process.WorkingSet64;
        }
        catch
        {
            return 0;
        }
    }

    private readonly record struct CpuSample(TimeSpan TotalProcessorTime, DateTimeOffset SampledAt);
    private readonly record struct IoSample(ulong TotalBytes, DateTimeOffset SampledAt);
}

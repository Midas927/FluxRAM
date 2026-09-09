using System.Runtime.InteropServices;
using System.Threading;
using FluxRAM.Core.Interop;
using FluxRAM.Core.Models;

namespace FluxRAM.Core.Services;

public sealed class MemoryPurgeService
{
    private const uint RequiredAccess =
        NativeMethods.PROCESS_SET_QUOTA |
        NativeMethods.PROCESS_QUERY_INFORMATION |
        NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION;

    public MemoryPurgeResult Purge(int processId)
    {
        var processHandle = NativeMethods.OpenProcess(RequiredAccess, false, processId);
        if (processHandle == IntPtr.Zero)
        {
            return MemoryPurgeResult.Failed(
                processId,
                $"OpenProcess failed with Win32Error={Marshal.GetLastWin32Error()}");
        }

        try
        {
            return PurgeWorkingSet(
                processId,
                () => ReadWorkingSetBytes(processHandle),
                () => NativeMethods.SetProcessWorkingSetSize(processHandle, new IntPtr(-1), new IntPtr(-1)),
                () => NativeMethods.EmptyWorkingSet(processHandle),
                Thread.Sleep);
        }
        finally
        {
            _ = NativeMethods.CloseHandle(processHandle);
        }
    }

    private static MemoryPurgeResult PurgeWorkingSet(
        int processId,
        Func<long?> readWorkingSetBytes,
        Func<bool> trimWorkingSet,
        Func<bool> emptyWorkingSet,
        Action<int> sleep)
    {
        var beforeBytes = readWorkingSetBytes();
        if (!beforeBytes.HasValue)
        {
            return MemoryPurgeResult.Failed(
                processId,
                $"Working-set measurement before trim failed with Win32Error={Marshal.GetLastWin32Error()}");
        }

        var trimRequested = trimWorkingSet();
        var trimError = trimRequested ? 0 : Marshal.GetLastWin32Error();
        var flushRequested = emptyWorkingSet();
        if (!trimRequested || !flushRequested)
        {
            var error = trimRequested ? Marshal.GetLastWin32Error() : trimError;
            return MemoryPurgeResult.Failed(processId, $"Working-set trim failed with Win32Error={error}");
        }

        var afterBytes = ReadWorkingSetBytesWithRetry(readWorkingSetBytes, sleep);
        return afterBytes.HasValue
            ? MemoryPurgeResult.Succeeded(processId, beforeBytes.Value, afterBytes.Value)
            : MemoryPurgeResult.SucceededWithoutMeasurement(processId, beforeBytes.Value);
    }

    private static long? ReadWorkingSetBytes(IntPtr processHandle)
    {
        var bufferSize = (uint)Marshal.SizeOf<NativeMethods.PROCESS_MEMORY_COUNTERS_EX>();
        var success = NativeMethods.GetProcessMemoryInfo(processHandle, out var counters, bufferSize);
        if (!success)
        {
            return null;
        }

        return checked((long)counters.WorkingSetSize.ToUInt64());
    }

    private static long? ReadWorkingSetBytesWithRetry(Func<long?> readWorkingSetBytes, Action<int> sleep)
    {
        var lowestObserved = readWorkingSetBytes();
        for (var attempt = 0; attempt < 3; attempt += 1)
        {
            sleep(30);
            var sampledBytes = readWorkingSetBytes();
            if (sampledBytes.HasValue && (!lowestObserved.HasValue || sampledBytes.Value < lowestObserved.Value))
            {
                lowestObserved = sampledBytes;
            }
        }

        return lowestObserved;
    }
}

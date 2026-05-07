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
            var beforeBytes = ReadWorkingSetBytes(processHandle);
            var trimRequested = NativeMethods.SetProcessWorkingSetSize(processHandle, new IntPtr(-1), new IntPtr(-1));
            var flushRequested = NativeMethods.EmptyWorkingSet(processHandle);
            var afterBytes = ReadWorkingSetBytesWithRetry(processHandle);

            if (!trimRequested || !flushRequested)
            {
                return MemoryPurgeResult.Failed(
                    processId,
                    $"Working-set trim failed with Win32Error={Marshal.GetLastWin32Error()}");
            }

            return MemoryPurgeResult.Succeeded(processId, beforeBytes, afterBytes);
        }
        finally
        {
            _ = NativeMethods.CloseHandle(processHandle);
        }
    }

    private static long ReadWorkingSetBytes(IntPtr processHandle)
    {
        var bufferSize = (uint)Marshal.SizeOf<NativeMethods.PROCESS_MEMORY_COUNTERS_EX>();
        var success = NativeMethods.GetProcessMemoryInfo(processHandle, out var counters, bufferSize);
        if (!success)
        {
            return 0;
        }

        return checked((long)counters.WorkingSetSize.ToUInt64());
    }

    private static long ReadWorkingSetBytesWithRetry(IntPtr processHandle)
    {
        var lowestObserved = ReadWorkingSetBytes(processHandle);
        for (var attempt = 0; attempt < 3; attempt += 1)
        {
            Thread.Sleep(30);
            var sampledBytes = ReadWorkingSetBytes(processHandle);
            if (sampledBytes < lowestObserved)
            {
                lowestObserved = sampledBytes;
            }
        }

        return lowestObserved;
    }
}

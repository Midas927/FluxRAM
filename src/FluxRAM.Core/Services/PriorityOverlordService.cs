using System.Diagnostics;
using FluxRAM.Core.Interop;
using FluxRAM.Core.Models;

namespace FluxRAM.Core.Services;

public sealed class PriorityOverlordService
{
    private const uint RequiredAccess =
        NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION |
        NativeMethods.PROCESS_SET_INFORMATION;

    public PrioritySweepResult Apply()
    {
        var foregroundProcessId = TryGetForegroundProcessId();
        if (!foregroundProcessId.HasValue)
        {
            return new PrioritySweepResult(0, 0, 0, "Unable to detect foreground window.");
        }

        var boostedCount = 0;
        var demotedCount = 0;
        var failedCount = 0;

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                var processName = TryGetProcessName(process);
                if (string.IsNullOrWhiteSpace(processName) || SystemProcessWhitelist.Contains(processName))
                {
                    continue;
                }

                var isForeground = process.Id == foregroundProcessId.Value;
                var targetPriority = isForeground
                    ? NativeMethods.HIGH_PRIORITY_CLASS
                    : NativeMethods.BELOW_NORMAL_PRIORITY_CLASS;

                var updated = TrySetPriorityClass(process.Id, targetPriority);
                if (!updated)
                {
                    failedCount += 1;
                    continue;
                }

                if (isForeground)
                {
                    boostedCount += 1;
                }
                else
                {
                    demotedCount += 1;
                }
            }
        }

        return new PrioritySweepResult(boostedCount, demotedCount, failedCount, null);
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

    private static bool TrySetPriorityClass(int processId, uint targetPriorityClass)
    {
        var processHandle = NativeMethods.OpenProcess(RequiredAccess, false, processId);
        if (processHandle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return NativeMethods.SetPriorityClass(processHandle, targetPriorityClass);
        }
        finally
        {
            _ = NativeMethods.CloseHandle(processHandle);
        }
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
}

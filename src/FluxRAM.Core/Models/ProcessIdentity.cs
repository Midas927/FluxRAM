using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace FluxRAM.Core.Models;

public static class ProcessIdentity
{
    public static bool IsKnown(ProcessSnapshot snapshot) =>
        snapshot.ProcessId > 0 && snapshot.StartTimeUtc.HasValue &&
        !string.IsNullOrWhiteSpace(snapshot.ExecutablePath) && Path.IsPathFullyQualified(snapshot.ExecutablePath);

    public static bool Matches(ProcessSnapshot captured, ProcessSnapshot current) =>
        IsKnown(captured) && IsKnown(current) && captured.ProcessId == current.ProcessId &&
        captured.StartTimeUtc == current.StartTimeUtc &&
        string.Equals(captured.ExecutablePath, current.ExecutablePath, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(captured.ProcessName, current.ProcessName, StringComparison.OrdinalIgnoreCase);

    // Both identity fields come from the retained handle, never a later PID lookup.
    public static ProcessSnapshot Capture(Process process)
    {
        var handle = process.SafeHandle;
        var path = new StringBuilder(32768);
        var length = path.Capacity;
        if (!QueryFullProcessImageName(handle, 0, path, ref length) ||
            !GetProcessTimes(handle, out var creation, out _, out _, out _))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return new ProcessSnapshot(process.Id, Path.GetFileNameWithoutExtension(path.ToString()), 0, false,
            ExecutablePath: path.ToString(), StartTimeUtc: DateTimeOffset.FromFileTime(creation).ToUniversalTime());
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder path, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);
}

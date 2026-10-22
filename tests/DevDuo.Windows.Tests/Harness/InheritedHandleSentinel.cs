using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DevDuo.Windows.Tests;

internal sealed class InheritedHandleSentinel : IDisposable
{
    private nint handle;

    internal InheritedHandleSentinel()
    {
        var attributes = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Inherit = 1 };
        handle = CreateEventW(ref attributes, true, false, null);
        if (handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    internal sealed record Child(uint ProcessId, string Executable, bool InheritedSentinel);

    internal sealed record ChildIdentity(uint ProcessId, string Executable);

    internal IReadOnlyList<ChildIdentity> FindChildren()
    {
        var children = new List<ChildIdentity>();
        var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == -1) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            if (!Process32FirstW(snapshot, ref entry)) throw new Win32Exception(Marshal.GetLastWin32Error());
            do
            {
                if (entry.ParentProcessId == Environment.ProcessId && entry.Executable.StartsWith("WebKit", StringComparison.Ordinal))
                    children.Add(new(entry.ProcessId, entry.Executable));
            } while (Process32NextW(snapshot, ref entry));
        }
        finally { CloseHandle(snapshot); }
        return children;
    }

    internal sealed record NetworkRestart(uint WebProcessId, uint KilledNetworkProcessId, double KilledMilliseconds);

    internal async Task<NetworkRestart> InterruptFirstNetworkConnection(CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(20))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var children = FindChildren();
            var network = children.FirstOrDefault(child => child.Executable == "WebKitNetworkProcess.exe");
            var web = children.FirstOrDefault(child => child.Executable == "WebKitWebProcess.exe");
            if (network is not null && web is not null)
            {
                var process = OpenProcess(0x100001, false, network.ProcessId);
                if (process == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                try
                {
                    // Keep the exact process object open and recheck ownership.
                    // A recycled numeric PID must never target another task.
                    if (FindChildren().Any(child => child == network))
                    {
                        if (!TerminateProcess(process, 77)) throw new Win32Exception(Marshal.GetLastWin32Error());
                        if (WaitForSingleObject(process, 2000) != 0) throw new TimeoutException("The owned NetworkProcess did not exit.");
                        return new(web.ProcessId, network.ProcessId, Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency);
                    }
                }
                finally { CloseHandle(process); }
            }
            await Task.Delay(1, cancellationToken);
        }
        throw new TimeoutException("No owned initial WebProcess and NetworkProcess pair appeared for restart acceptance.");
    }

    internal IReadOnlyList<Child> InspectChildren()
    {
        var children = new List<Child>();
        foreach (var child in FindChildren())
        {
            var process = OpenProcess(0x40, false, child.ProcessId);
            if (process == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var inherited = false;
                // Inherited Windows handles retain their numeric value. Compare
                // kernel object identity without signaling an unrelated child event.
                if (DuplicateHandle(process, handle, GetCurrentProcess(), out var copied, 0, false, 2))
                {
                    try { inherited = CompareObjectHandles(handle, copied); }
                    finally { CloseHandle(copied); }
                }
                children.Add(new(child.ProcessId, child.Executable, inherited));
            }
            finally { CloseHandle(process); }
        }
        return children;
    }

    public void Dispose()
    {
        if (handle != 0) CloseHandle(handle);
        handle = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes { internal int Length; internal nint Descriptor; internal int Inherit; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        internal uint Size, Usage, ProcessId;
        internal nuint HeapId;
        internal uint ModuleId, Threads, ParentProcessId;
        internal int Priority;
        internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string Executable;
    }
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern nint CreateEventW(ref SecurityAttributes attributes, bool manualReset, bool initialState, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool Process32FirstW(nint snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool Process32NextW(nint snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DuplicateHandle(nint source, nint handle, nint target, out nint copied, uint access, bool inherit, uint options);
    [DllImport("kernel32.dll")] private static extern nint GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateProcess(nint process, uint exitCode);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(nint handle, uint milliseconds);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("kernelbase.dll")] private static extern bool CompareObjectHandles(nint first, nint second);
}

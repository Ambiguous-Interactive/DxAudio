Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$profileRight = whoami.exe /priv | Select-String '^SeSystemProfilePrivilege\s'
if ($profileRight) {
    Write-Host "System profiling token right: $($profileRight.Line.Trim())"
} else {
    Write-Host 'System profiling right absent from token.'
}

Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

public static class DxaThreadDispatchProbe
{
    private const uint Dispatch = 1;

    // WinSDK PERFORMANCE_DATA on x64: 32-byte header and sixteen 16-byte hardware counters.
    [StructLayout(LayoutKind.Sequential, Size = 288)]
    private struct PerformanceData
    {
        public ushort Size;
        public byte Version;
        private byte HardwareCounterCount;
        public uint ContextSwitchCount;
        public ulong WaitReasons;
        public ulong Cycles;
        private uint RetryCount;
        private uint Reserved;
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr GetCurrentThread();

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint EnableThreadProfiling(IntPtr thread, uint flags, ulong counters, out IntPtr handle);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint ReadThreadProfilingData(IntPtr handle, uint flags, ref PerformanceData data);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint DisableThreadProfiling(IntPtr handle);

    public static void Run()
    {
        IntPtr handle;
        Require(EnableThreadProfiling(GetCurrentThread(), Dispatch, 0, out handle), "EnableThreadProfiling");
        try
        {
            Read(handle);
            ulong sleepCycles = 0;
            ulong busyCycles = 0;
            uint sleepSwitches = 0;
            for (int i = 0; i < 3; i++)
            {
                sleepCycles += Measure(handle, true, out uint switches);
                sleepSwitches += switches;
                busyCycles += Measure(handle, false, out _);
            }
            if (sleepSwitches == 0 || busyCycles <= sleepCycles)
            {
                throw new InvalidOperationException("Dispatch counters did not distinguish blocking and in-thread work.");
            }
        }
        finally
        {
            Require(DisableThreadProfiling(handle), "DisableThreadProfiling");
        }
    }

    private static ulong Measure(IntPtr handle, bool sleep, out uint switches)
    {
        PerformanceData before = Read(handle);
        long start = Stopwatch.GetTimestamp();
        if (sleep)
        {
            Thread.Sleep(25);
        }
        else
        {
            while (Stopwatch.GetTimestamp() - start < Stopwatch.Frequency / 40)
            {
                Thread.SpinWait(128);
            }
        }
        long elapsed = Stopwatch.GetTimestamp() - start;
        PerformanceData after = Read(handle);
        switches = after.ContextSwitchCount - before.ContextSwitchCount;
        ulong cycles = after.Cycles - before.Cycles;
        Console.WriteLine("{0}: {1:F1} us, {2} cycles, {3} switches, waits 0x{4:X}",
            sleep ? "blocking" : "in-thread", elapsed * 1e6 / Stopwatch.Frequency, cycles, switches, after.WaitReasons);
        return cycles;
    }

    private static PerformanceData Read(IntPtr handle)
    {
        PerformanceData data = new PerformanceData { Size = (ushort)Marshal.SizeOf<PerformanceData>(), Version = 1 };
        Require(ReadThreadProfilingData(handle, Dispatch, ref data), "ReadThreadProfilingData");
        return data;
    }

    private static void Require(uint status, string operation)
    {
        if (status != 0)
        {
            throw new Win32Exception((int)status, operation + " failed with Windows error " + status);
        }
    }
}
'@
[DxaThreadDispatchProbe]::Run()

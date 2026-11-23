Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

powercfg.exe /getactivescheme
powercfg.exe /query SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMIN
powercfg.exe /query SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX

try {
    Get-WinEvent -FilterHashtable @{
        LogName = 'System'
        ProviderName = 'Microsoft-Windows-Kernel-Processor-Power'
        Id = 37
        StartTime = (Get-Date).AddHours(-6)
    } -MaxEvents 30 | ForEach-Object {
        Write-Host ("Firmware frequency limit event {0:o}: {1}" -f $_.TimeCreated.ToUniversalTime(), $_.Message)
    }
} catch {
    Write-Host "Firmware frequency limit query: $($_.FullyQualifiedErrorId)"
}

Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

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

    [StructLayout(LayoutKind.Sequential)]
    private struct Throttling
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessorPower
    {
        public uint Number;
        public uint MaximumMhz;
        public uint CurrentMhz;
        public uint LimitMhz;
        public uint MaximumIdleState;
        public uint CurrentIdleState;
    }

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern UIntPtr SetThreadAffinityMask(IntPtr thread, UIntPtr affinityMask);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool GetThreadInformation(IntPtr thread, int infoClass, ref Throttling state, uint size);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool SetThreadInformation(IntPtr thread, int infoClass, ref Throttling state, uint size);

    [DllImport("powrprof.dll", ExactSpelling = true)]
    private static extern uint CallNtPowerInformation(int level, IntPtr input, uint inputSize, [Out] ProcessorPower[] power, uint size);

    private const int ThreadPowerThrottling = 3;

    public static void RunThroughput()
    {
        IntPtr thread = GetCurrentThread();
        uint structSize = (uint)Marshal.SizeOf<Throttling>();
        Throttling original = new Throttling { Version = 1 };
        if (!GetThreadInformation(thread, ThreadPowerThrottling, ref original, structSize))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetThreadInformation");
        }
        Console.WriteLine("Initial thread power policy: control=0x{0:X}, state=0x{1:X}", original.ControlMask, original.StateMask);
        Throttling high = original;
        high.ControlMask |= 1;
        high.StateMask &= ~1u;
        Require(EnableThreadProfiling(thread, Dispatch, 0, out IntPtr profile), "EnableThreadProfiling");
        try
        {
            foreach (int cpu in new[] { 6, 20, 5, 21 })
            {
                UIntPtr originalMask = SetThreadAffinityMask(thread, (UIntPtr)(1ul << cpu));
                if (originalMask == UIntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "SetThreadAffinityMask for CPU " + cpu);
                }
                try
                {
                    foreach (bool useHigh in new[] { false, true, true, false })
                    {
                        Throttling state = useHigh ? high : original;
                        if (!SetThreadInformation(thread, ThreadPowerThrottling, ref state, structSize))
                        {
                            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetThreadInformation");
                        }
                        MeasureThroughput(profile, cpu, useHigh);
                    }
                }
                finally
                {
                    if (SetThreadAffinityMask(thread, originalMask) == UIntPtr.Zero)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Restore thread affinity");
                    }
                }
            }
        }
        finally
        {
            bool restored = SetThreadInformation(thread, ThreadPowerThrottling, ref original, structSize);
            int restoreError = Marshal.GetLastWin32Error();
            Require(DisableThreadProfiling(profile), "DisableThreadProfiling");
            if (!restored)
            {
                throw new Win32Exception(restoreError, "Restore thread power policy");
            }
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static uint FixedWork(uint state)
    {
        for (int i = 0; i < 100000; i++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
        }
        return state;
    }

    private static void MeasureThroughput(IntPtr profile, int cpu, bool high)
    {
        const int samples = 1024;
        const uint seed = 391782497;
        uint expected = FixedWork(seed);
        for (int i = 0; i < 64; i++)
        {
            FixedWork(seed);
        }

        double[] wall = new double[samples];
        ulong[] cycles = new ulong[samples];
        uint[] switches = new uint[samples];
        var power = new ProcessorPower[Environment.ProcessorCount];
        uint powerSize = (uint)(power.Length * Marshal.SizeOf<ProcessorPower>());
        uint beforePower = CallNtPowerInformation(11, IntPtr.Zero, 0, power, powerSize);
        string beforeState = PowerState(beforePower, power, cpu);
        Console.WriteLine("CPU {0}, {1}, started {2:o}, power {3}", cpu, high ? "HighQoS" : "original QoS", DateTime.UtcNow, beforeState);
        for (int i = 0; i < samples; i++)
        {
            PerformanceData before = Read(profile);
            long start = Stopwatch.GetTimestamp();
            uint value = FixedWork(seed);
            long end = Stopwatch.GetTimestamp();
            PerformanceData after = Read(profile);
            if (value != expected)
            {
                throw new InvalidOperationException("Fixed-work checksum changed.");
            }
            wall[i] = (end - start) * 1e6 / Stopwatch.Frequency;
            cycles[i] = after.Cycles - before.Cycles;
            switches[i] = after.ContextSwitchCount - before.ContextSwitchCount;
        }
        uint afterPower = CallNtPowerInformation(11, IntPtr.Zero, 0, power, powerSize);
        string afterState = PowerState(afterPower, power, cpu);
        int[] order = new int[samples];
        for (int i = 0; i < samples; i++)
        {
            order[i] = i;
        }
        Array.Sort(order, (left, right) => wall[left].CompareTo(wall[right]));
        foreach (int rank in new[] { 0, 50, 95, 99, 100 })
        {
            int index = order[Math.Max(0, (samples * rank + 99) / 100 - 1)];
            Console.WriteLine("  p{0}: {1:F2} us, {2} dispatch cycles, {3} switches", rank, wall[index], cycles[index], switches[index]);
        }
        Console.WriteLine("  Ended {0:o}, power {1}, checksum {2}", DateTime.UtcNow, afterState, expected);
    }

    private static string PowerState(uint result, ProcessorPower[] power, int cpu)
    {
        if (result != 0 || cpu >= power.Length || power[cpu].Number != cpu)
        {
            return "unavailable (power status 0x" + result.ToString("X") + ")";
        }
        return string.Format("{0} current, {1} limited, {2} nominal MHz",
            power[cpu].CurrentMhz, power[cpu].LimitMhz, power[cpu].MaximumMhz);
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
[DxaThreadDispatchProbe]::RunThroughput()

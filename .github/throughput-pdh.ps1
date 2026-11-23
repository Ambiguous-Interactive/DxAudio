Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern UIntPtr SetThreadAffinityMask(IntPtr thread, UIntPtr affinityMask);

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct PdhValue
    {
        [FieldOffset(0)] public uint Status;
        [FieldOffset(8)] public double Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhOpenQueryW", ExactSpelling = true)]
    private static extern uint PdhOpenQuery(string source, UIntPtr data, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhAddEnglishCounterW", ExactSpelling = true)]
    private static extern uint PdhAddEnglishCounter(IntPtr query, string path, UIntPtr data, out IntPtr counter);

    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, IntPtr type, out PdhValue value);

    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCloseQuery(IntPtr query);

    private sealed class ProcessorCounters : IDisposable
    {
        private static readonly string[] Names = { "% Processor Performance", "% Processor Utility", "% Processor Time", "% DPC Time", "% Interrupt Time" };
        private readonly IntPtr _query;
        private readonly IntPtr[] _counters = new IntPtr[Names.Length];

        public ProcessorCounters(int cpu)
        {
            uint open = PdhOpenQuery(null, UIntPtr.Zero, out _query);
            if (open != 0)
            {
                Console.WriteLine("  PDH open failed: 0x{0:X8}", open);
                return;
            }
            for (int i = 0; i < _counters.Length; i++)
            {
                string path = @"\Processor Information(0," + cpu + @")\" + Names[i];
                uint result = PdhAddEnglishCounter(_query, path, UIntPtr.Zero, out _counters[i]);
                if (result != 0)
                {
                    Console.WriteLine("  PDH {0} unavailable: 0x{1:X8}", Names[i], result);
                }
            }
        }

        public bool Sample()
        {
            if (_query == IntPtr.Zero)
            {
                return false;
            }
            uint result = PdhCollectQueryData(_query);
            if (result != 0)
            {
                Console.WriteLine("  PDH sample failed: 0x{0:X8}", result);
            }
            return result == 0;
        }

        public void Report()
        {
            for (int i = 0; i < _counters.Length; i++)
            {
                if (_counters[i] == IntPtr.Zero)
                {
                    continue;
                }
                uint result = PdhGetFormattedCounterValue(_counters[i], 0x8200, IntPtr.Zero, out PdhValue value);
                if (result == 0 && value.Status <= 1)
                {
                    Console.WriteLine("  PDH {0}: {1:F2}", Names[i], value.Value);
                }
                else
                {
                    Console.WriteLine("  PDH {0} read failed: 0x{1:X8}, status 0x{2:X8}", Names[i], result, value.Status);
                }
            }
        }

        public void Dispose()
        {
            if (_query != IntPtr.Zero)
            {
                Require(PdhCloseQuery(_query), "PdhCloseQuery");
            }
        }
    }

    public static void RunThroughput()
    {
        IntPtr thread = GetCurrentThread();
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
                    using var counters = new ProcessorCounters(cpu);
                    for (int arm = 0; arm < 12; arm++)
                    {
                        MeasureThroughput(profile, cpu, arm, counters);
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
            Require(DisableThreadProfiling(profile), "DisableThreadProfiling");
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

    private static void MeasureThroughput(IntPtr profile, int cpu, int arm, ProcessorCounters counters)
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
        Console.WriteLine("CPU {0}, window {1}, started {2:o}", cpu, arm, DateTime.UtcNow);
        bool collected = counters.Sample();
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
        if (collected && counters.Sample())
        {
            counters.Report();
        }
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
        Console.WriteLine("  Ended {0:o}, checksum {1}", DateTime.UtcNow, expected);
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

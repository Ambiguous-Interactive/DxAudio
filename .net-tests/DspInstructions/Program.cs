namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using System.Globalization;
    using System.Runtime.InteropServices;

    internal static class Program
    {
        [DllImport("dxaudio_callgrind", EntryPoint = "start_count")]
        private static extern void StartCount();

        [DllImport("dxaudio_callgrind", EntryPoint = "stop_count")]
        private static extern void StopCount();

        private static void Main(string[] args)
        {
            if (args[0] == "list")
            {
                foreach ((int voices, string effect, bool bursts) in DspRenderWorkload.Cases)
                {
                    Console.WriteLine($"{effect}, {voices} voices, bursts {bursts}");
                }

                return;
            }

            (int count, string name, bool hasBursts) = DspRenderWorkload.Cases[int.Parse(args[0], CultureInfo.InvariantCulture)];
            DspRenderWorkload workload = new(count, name, hasBursts, enabled: true);
            Render(workload, count, DspRenderWorkload.WarmupPasses);
            StopCount(); // Resolve the stop import before enabling instrumentation.
            StartCount();
            Render(workload, count, DspRenderWorkload.Passes);
            StopCount();
        }

        private static void Render(DspRenderWorkload workload, int voices, int passes)
        {
            for (int pass = 0; pass < passes; ++pass)
            {
                for (int voice = 0; voice < voices; ++voice)
                {
                    workload.CopyInput();
                    workload.Process(voice);
                }
            }
        }
    }
}

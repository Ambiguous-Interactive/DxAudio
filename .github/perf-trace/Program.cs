using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Windows.EventTracing;
using Microsoft.Windows.EventTracing.Cpu;

if (args.Length != 2)
{
    throw new ArgumentException("Expected the CPU trace and NUnit result paths.");
}

const string HeadingPattern =
    @"Windows trace for (?<effect>[A-Za-z ]+) \(bursts (?<bursts>True|False)\), (?<voices>\d+) voices\. PID (?<pid>\d+), TID (?<tid>\d+), QPC frequency (?<frequency>\d+)\.";
const string RangePattern = @"(?<rank>p50|p95|p99|max) (?<start>\d+)-(?<end>\d+) \(\d+:\d+ to \d+:\d+\)";

List<(string Workload, string Rank, int Process, int Thread, long Frequency, long Start, long End)> windows = new();
foreach (XElement output in XDocument.Load(args[1]).Descendants("output"))
{
    foreach (string line in output.Value.Split('\n'))
    {
        Match heading = Regex.Match(line, HeadingPattern, RegexOptions.CultureInvariant);
        if (!heading.Success)
        {
            continue;
        }

        string workload = $"{heading.Groups["effect"].Value} {heading.Groups["voices"].Value} bursts={heading.Groups["bursts"].Value}";
        int process = (int)Number(heading, "pid");
        int thread = (int)Number(heading, "tid");
        long frequency = Number(heading, "frequency");
        MatchCollection ranges = Regex.Matches(line, RangePattern, RegexOptions.CultureInvariant);
        if (frequency <= 0 || ranges.Count != 5)
        {
            throw new InvalidOperationException("The benchmark did not record five native clock windows.");
        }

        for (int index = 0; index < ranges.Count; ++index)
        {
            Match range = ranges[index];
            long start = Number(range, "start");
            long end = Number(range, "end");
            if (end <= start)
            {
                throw new InvalidOperationException("The benchmark recorded an invalid trace window.");
            }

            string arm = index == 0 ? "baseline" : "candidate";
            windows.Add((workload, $"{arm} {range.Groups["rank"].Value}", process, thread, frequency, start, end));
        }
    }
}

if (windows.Count == 0)
{
    Console.WriteLine("Windows CPU trace has no budget failures or long render windows to analyze.");
    return;
}

using ITraceProcessor trace = TraceProcessor.Create(args[0]);
ITraceTimestampContext clock = trace.UseTraceTimestampContext();
IPendingResult<ICpuSchedulingDataSource> scheduling = trace.UseCpuSchedulingData();
IPendingResult<IInterruptHandlingDataSource> interrupts = trace.UseInterruptHandlingData();
trace.Process();
if (clock.ClockType != TraceClockType.PerformanceCounter)
{
    throw new InvalidOperationException("The trace and benchmark must both use QPC.");
}

var threadWindows = windows.Select(w => (w.Process, w.Thread)).ToHashSet();
var slices = scheduling
    .Result.CpuTimeSlices.Where(s => s.Thread != null && threadWindows.Contains((s.Thread.ProcessId, s.Thread.Id)) && Usable(s.StartTime) && Usable(s.StopTime))
    .ToLookup(s => (s.Thread.ProcessId, s.Thread.Id));
var interruptIntervals = interrupts.Result.Activity.Select(a => a.Interval).Where(i => Usable(i.StartTime) && Usable(i.StopTime)).ToLookup(i => i.Processor);
foreach (var window in windows)
{
    long scheduled = 0;
    int contextSwitches = 0;
    List<(long Start, long End)> interruptSpans = new();
    HashSet<int> processors = new();
    foreach (ICpuTimeSlice slice in slices[(window.Process, window.Thread)])
    {
        long start = Math.Max(window.Start, slice.StartTime.Value.RawValue);
        long end = Math.Min(window.End, slice.StopTime.Value.RawValue);
        if (end <= start)
        {
            continue;
        }

        processors.Add(slice.Processor);
        scheduled += end - start;
        if (end < window.End)
        {
            ++contextSwitches;
        }

        foreach (IInterruptHandlingInterval interrupt in interruptIntervals[slice.Processor])
        {
            long interruptStart = Math.Max(start, interrupt.StartTime.Value.RawValue);
            long interruptEnd = Math.Min(end, interrupt.StopTime.Value.RawValue);
            if (interruptStart < interruptEnd)
            {
                interruptSpans.Add((interruptStart, interruptEnd));
            }
        }
    }

    long total = window.End - window.Start;
    if (scheduled <= 0 || total < scheduled)
    {
        throw new InvalidOperationException($"Timeline did not match the benchmark thread for {window.Workload}.");
    }

    long interrupted = CoveredTicks(interruptSpans);
    double microseconds = 1_000_000d / window.Frequency;
    Console.WriteLine(
        $"Windows CPU trace: {window.Workload}, {window.Rank}, CPUs {string.Join(",", processors.Order())}. "
            + $"Scheduled {scheduled * microseconds:F2} us of {total * microseconds:F2} us, "
            + $"off CPU {(total - scheduled) * microseconds:F2} us, "
            + $"ISR/DPC overlap {interrupted * microseconds:F2} us, switches out {contextSwitches}."
    );
}

static long Number(Match match, string field)
{
    return long.Parse(match.Groups[field].Value, CultureInfo.InvariantCulture);
}

static bool Usable(TraceTimestamp timestamp)
{
    return timestamp.HasValue && !timestamp.IsPartial;
}

static long CoveredTicks(List<(long Start, long End)> spans)
{
    long lastEnd = 0;
    long ticks = 0;
    foreach ((long start, long end) in spans.OrderBy(span => span.Start))
    {
        if (lastEnd < end)
        {
            ticks += end - Math.Max(lastEnd, start);
            lastEnd = end;
        }
    }

    return ticks;
}

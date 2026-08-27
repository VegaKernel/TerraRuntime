using System.Runtime.InteropServices;

namespace TerraRuntime.Core;

/// <summary>
/// Reads CPU time consumed by the calling OS thread. The clock intentionally excludes work done by
/// network and worker threads so game-loop wall time and CPU time can be compared independently.
/// </summary>
internal static partial class ThreadCpuClock
{
    private const int LinuxClockThreadCpuTimeId = 3;
    private const long NanosecondsPerSecond = 1_000_000_000;
    private const long NanosecondsPerWindowsTick = 100;

    public static bool TryGetTimestampNanoseconds(out long nanoseconds)
    {
        if (OperatingSystem.IsWindows())
        {
            return TryGetWindowsTimestamp(out nanoseconds);
        }

        if (OperatingSystem.IsLinux())
        {
            return TryGetLinuxTimestamp(out nanoseconds);
        }

        nanoseconds = 0;
        return false;
    }

    private static bool TryGetWindowsTimestamp(out long nanoseconds)
    {
        nint thread = GetCurrentThread();
        if (GetThreadTimes(thread, out _, out _, out FileTime kernel, out FileTime user) == 0)
        {
            nanoseconds = 0;
            return false;
        }

        ulong kernelTicks = ToUInt64(kernel);
        ulong userTicks = ToUInt64(user);
        ulong totalTicks = kernelTicks + userTicks;
        if (totalTicks > (ulong)(long.MaxValue / NanosecondsPerWindowsTick))
        {
            nanoseconds = 0;
            return false;
        }

        nanoseconds = (long)totalTicks * NanosecondsPerWindowsTick;
        return true;
    }

    private static bool TryGetLinuxTimestamp(out long nanoseconds)
    {
        if (ClockGetTime(LinuxClockThreadCpuTimeId, out Timespec timestamp) != 0 ||
            timestamp.Seconds < 0 ||
            timestamp.Nanoseconds < 0 ||
            timestamp.Nanoseconds >= NanosecondsPerSecond ||
            timestamp.Seconds > (long.MaxValue - timestamp.Nanoseconds) / NanosecondsPerSecond)
        {
            nanoseconds = 0;
            return false;
        }

        nanoseconds = (timestamp.Seconds * NanosecondsPerSecond) + timestamp.Nanoseconds;
        return true;
    }

    private static ulong ToUInt64(FileTime value) =>
        ((ulong)value.HighDateTime << 32) | value.LowDateTime;

    [LibraryImport("kernel32.dll", EntryPoint = "GetCurrentThread")]
    private static partial nint GetCurrentThread();

    [LibraryImport("kernel32.dll", EntryPoint = "GetThreadTimes", SetLastError = true)]
    private static partial int GetThreadTimes(
        nint thread,
        out FileTime creationTime,
        out FileTime exitTime,
        out FileTime kernelTime,
        out FileTime userTime);

    [LibraryImport("libc", EntryPoint = "clock_gettime", SetLastError = true)]
    private static partial int ClockGetTime(int clockId, out Timespec timestamp);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Timespec
    {
        public long Seconds;
        public long Nanoseconds;
    }
}

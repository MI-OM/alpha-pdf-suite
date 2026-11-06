using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PdfSuite.Pdf.Remote;

/// <summary>
/// Reads a process's memory footprint on macOS: the figure Activity Monitor shows, which counts
/// compressed memory as well as resident memory.
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe partial class MacMemoryFootprint
{
    private const int RUSAGE_INFO_V0 = 0;

    // struct rusage_info_v0: a 16-byte UUID, then ten 64-bit counters. ri_phys_footprint is the eighth.
    private const int InfoSize = 16 + (10 * 8);
    private const int PhysFootprintOffset = 16 + (7 * 8);

    /// <summary>The footprint in bytes, or null if the process could not be read, for example because it has exited.</summary>
    public static long? Of(int processId)
    {
        byte* info = stackalloc byte[InfoSize];
        return proc_pid_rusage(processId, RUSAGE_INFO_V0, info) == 0 ? (long)*(ulong*)(info + PhysFootprintOffset) : null;
    }

    [LibraryImport("/usr/lib/libSystem.B.dylib")]
    private static partial int proc_pid_rusage(int pid, int flavor, byte* buffer);
}

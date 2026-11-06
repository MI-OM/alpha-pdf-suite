using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PdfSuite.Pdf.Worker;

/// <summary>
/// Caps the worker's own memory on Linux, before it reads any request (spike finding 12).
/// RLIMIT_DATA counts private writable memory as it is committed (Linux 4.7 and later), not
/// address space that is only reserved, so the .NET runtime's large reservations do not count
/// against it, as they would against RLIMIT_AS. The hard limit is lowered too, and an unprivileged
/// process cannot raise it again, so a compromised worker cannot lift its own cap. An allocation
/// over the limit fails; the worker reports OutOfMemory and keeps serving, as under the Windows
/// Job Object.
/// </summary>
[SupportedOSPlatform("linux")]
internal static partial class LinuxMemoryLimit
{
    private const int RLIMIT_DATA = 2;

    public static void Apply(long bytes)
    {
        var limit = new RLimit { Current = (ulong)bytes, Maximum = (ulong)bytes };
        if (setrlimit(RLIMIT_DATA, limit) != 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "setrlimit(RLIMIT_DATA) failed.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RLimit
    {
        public ulong Current;
        public ulong Maximum;
    }

    // glibc's soname; the linux-* runtime identifiers the project builds for all use glibc.
    [LibraryImport("libc.so.6", SetLastError = true)]
    private static partial int setrlimit(int resource, in RLimit limit);
}

# Shared helpers for the packaging scripts (spike S6). Dot-source this file.
# Works in Windows PowerShell 5.1 and PowerShell 7.

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$RepoRoot = Split-Path (Split-Path $PSScriptRoot)
$OnWindows = $PSVersionTable.PSEdition -eq 'Desktop' -or $IsWindows

# The package identity used by every format. Names avoid real domains: "test" is a reserved
# top-level domain (RFC 2606), and "PDF Suite" is a working name (README).
$PackageInfo = @{
    DisplayName = 'PDF Suite (spike)'
    Description = 'Technical spike build of PDF Suite. Not a usable application.'
    Command     = 'pdf-suite'
    BundleId    = 'test.pdfsuite.spike'
    Publisher   = 'CN=PDF Suite Spike Test Signing'
}

function Invoke-Native {
    # Runs a native command and stops on a non-zero exit code.
    param([Parameter(Mandatory)] [string] $FilePath, [string[]] $Arguments = @())
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$FilePath exited with code $LASTEXITCODE." }
}

function Get-FolderSize {
    param([Parameter(Mandatory)] [string] $Path)
    $files = @(Get-ChildItem $Path -Recurse -File)
    [pscustomobject]@{ Files = $files.Count; Bytes = ($files | Measure-Object Length -Sum).Sum }
}

function Write-Notice {
    # Prints a line, and in GitHub Actions also a public annotation (readable without signing in).
    param([Parameter(Mandatory)] [string] $Title, [Parameter(Mandatory)] [string] $Message)
    if ($env:GITHUB_ACTIONS -eq 'true') { "::notice title=$Title::$Message" } else { "$Title`: $Message" }
}

function Format-Megabytes {
    param([double] $Bytes)
    '{0:N1} MB' -f ($Bytes / 1MB)
}

function Get-ElfVersionNeeds {
    # The symbol versions an ELF file needs from each shared library: its version references
    # (.gnu.version_r), which the dynamic loader checks before the program starts. Read directly,
    # so packaging needs no binutils. Returns nothing for a file that is not ELF.
    param([Parameter(Mandatory)] [string] $Path)
    $b = [System.IO.File]::ReadAllBytes($Path)
    if ($b.Length -lt 64 -or $b[0] -ne 0x7F -or $b[1] -ne 0x45 -or $b[2] -ne 0x4C -or $b[3] -ne 0x46) { return }
    if ($b[4] -ne 2 -or $b[5] -ne 1) { throw "$Path is ELF, but not 64-bit little-endian." }

    function U16([long] $At) { [long][BitConverter]::ToUInt16($b, $At) }
    function U32([long] $At) { [long][BitConverter]::ToUInt32($b, $At) }
    function Text([long] $At) { [System.Text.Encoding]::ASCII.GetString($b, $At, [Array]::IndexOf($b, [byte]0, [int]$At) - $At) }

    $sectionTable = [BitConverter]::ToInt64($b, 0x28)
    $sectionSize = U16 0x3A
    $sections = U16 0x3C
    if ($sections -eq 0) { throw "$Path has no section headers, so its version references cannot be read." }
    for ($i = 0; $i -lt $sections; $i++) {
        $section = $sectionTable + $i * $sectionSize
        if ((U32 ($section + 4)) -ne 0x6FFFFFFE) { continue }   # SHT_GNU_verneed
        $strings = [BitConverter]::ToInt64($b, $sectionTable + (U32 ($section + 40)) * $sectionSize + 24)
        $need = [BitConverter]::ToInt64($b, $section + 24)
        for ($n = 0; $n -lt (U32 ($section + 44)); $n++) {      # Elf64_Verneed entries
            $library = Text ($strings + (U32 ($need + 4)))
            $aux = $need + (U32 ($need + 8))
            for ($a = 0; $a -lt (U16 ($need + 2)); $a++) {      # Elf64_Vernaux entries
                [pscustomobject]@{ Library = $library; Version = Text ($strings + (U32 ($aux + 8))) }
                $aux += U32 ($aux + 12)
            }
            $need += U32 ($need + 12)
        }
    }
}

function Get-RuntimeLibraryFloor {
    # The newest glibc, libstdc++ and libgcc symbol versions that the ELF files in a folder need,
    # and which files need them. A system with an older glibc cannot start them (spike results,
    # finding 53).
    param([Parameter(Mandatory)] [string] $Folder)
    $needs = foreach ($file in Get-ChildItem $Folder -File) {
        foreach ($need in Get-ElfVersionNeeds $file.FullName) {
            if ($need.Version -eq 'GLIBC_ABI_DT_RELR') {
                # Packed relative relocations, which glibc supports from 2.36.
                [pscustomobject]@{ Family = 'GLIBC'; Version = [version]'2.36'; File = $file.Name }
            }
            elseif ($need.Version -match '^(GLIBC|GLIBCXX|CXXABI|GCC)_(\d+(\.\d+)+)$') {
                [pscustomobject]@{ Family = $Matches[1]; Version = [version]$Matches[2]; File = $file.Name }
            }
        }
    }
    foreach ($family in $needs | Group-Object Family | Sort-Object Name) {
        $newest = ($family.Group | Sort-Object Version -Descending | Select-Object -First 1).Version
        [pscustomobject]@{
            Family  = $family.Name
            Version = $newest
            Files   = @($family.Group | Where-Object { $_.Version -eq $newest } | ForEach-Object File | Sort-Object -Unique)
        }
    }
}

if (-not ('SpikeIcon' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.IO.Compression;

// A placeholder icon drawn in code, so packaging needs no image files or third-party art:
// a white page with a folded corner on a blue rounded square. Written as an RGBA PNG.
public static class SpikeIcon
{
    public static void Write(int size, string path)
    {
        byte[] rgba = new byte[size * size * 4];
        double s = size, margin = s * 0.04, radius = s * 0.18;
        double left = s * 0.27, right = s * 0.73, top = s * 0.17, bottom = s * 0.83, fold = s * 0.14;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double px = x + 0.5, py = y + 0.5;
                uint argb = 0;
                if (InRoundedRect(px, py, margin, margin, s - margin, s - margin, radius))
                {
                    argb = 0xFF2B5CABu;
                }

                if (px >= left && px <= right && py >= top && py <= bottom)
                {
                    double dx = px - (right - fold), dy = py - top;
                    if (dx > 0 && dy < fold)
                    {
                        // Above the diagonal the corner is cut away; below it is the folded flap.
                        if (dx <= dy) { argb = 0xFFC9D6EAu; }
                    }
                    else
                    {
                        argb = 0xFFFFFFFFu;
                        for (int line = 0; line < 4; line++)
                        {
                            double lineTop = top + s * (0.30 + 0.11 * line);
                            double lineRight = line == 3 ? left + s * 0.26 : right - s * 0.06;
                            if (py >= lineTop && py < lineTop + s * 0.035 && px >= left + s * 0.06 && px <= lineRight)
                            {
                                argb = 0xFF8A9BB5u;
                            }
                        }
                    }
                }

                int i = (y * size + x) * 4;
                rgba[i] = (byte)(argb >> 16);
                rgba[i + 1] = (byte)(argb >> 8);
                rgba[i + 2] = (byte)argb;
                rgba[i + 3] = (byte)(argb >> 24);
            }
        }

        File.WriteAllBytes(path, Png(size, rgba));
    }

    private static bool InRoundedRect(double x, double y, double l, double t, double r, double b, double radius)
    {
        if (x < l || x > r || y < t || y > b) { return false; }
        double cx = Math.Max(l + radius, Math.Min(x, r - radius));
        double cy = Math.Max(t + radius, Math.Min(y, b - radius));
        return (x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius;
    }

    private static byte[] Png(int size, byte[] rgba)
    {
        // Each scanline starts with filter type 0 (none).
        byte[] raw = new byte[size * (size * 4 + 1)];
        for (int y = 0; y < size; y++)
        {
            Buffer.BlockCopy(rgba, y * size * 4, raw, y * (size * 4 + 1) + 1, size * 4);
        }

        var output = new MemoryStream();
        output.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
        byte[] header = new byte[13];
        BigEndian(header, 0, (uint)size);
        BigEndian(header, 4, (uint)size);
        header[8] = 8;  // bits per channel
        header[9] = 6;  // RGBA
        Chunk(output, "IHDR", header);
        Chunk(output, "IDAT", Zlib(raw));
        Chunk(output, "IEND", new byte[0]);
        return output.ToArray();
    }

    // zlib framing around a raw deflate stream (DeflateStream writes no header), for .NET Framework too.
    private static byte[] Zlib(byte[] data)
    {
        var output = new MemoryStream();
        output.WriteByte(0x78);
        output.WriteByte(0x9C);
        using (var deflate = new DeflateStream(output, CompressionMode.Compress, true))
        {
            deflate.Write(data, 0, data.Length);
        }

        uint a = 1, b = 0;
        foreach (byte value in data)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }

        byte[] adler = new byte[4];
        BigEndian(adler, 0, (b << 16) | a);
        output.Write(adler, 0, 4);
        return output.ToArray();
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        byte[] length = new byte[4], crc = new byte[4];
        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        BigEndian(length, 0, (uint)data.Length);
        output.Write(length, 0, 4);
        output.Write(typeBytes, 0, 4);
        output.Write(data, 0, data.Length);
        uint c = 0xFFFFFFFFu;
        c = Crc(c, typeBytes);
        c = Crc(c, data);
        BigEndian(crc, 0, c ^ 0xFFFFFFFFu);
        output.Write(crc, 0, 4);
    }

    private static uint Crc(uint c, byte[] data)
    {
        foreach (byte value in data)
        {
            c ^= value;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }
        }

        return c;
    }

    private static void BigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }
}
'@
}

function Write-PlaceholderIcon {
    param([Parameter(Mandatory)] [int] $Size, [Parameter(Mandatory)] [string] $Path)
    New-Item -ItemType Directory -Force (Split-Path $Path) | Out-Null
    [SpikeIcon]::Write($Size, [System.IO.Path]::GetFullPath($Path))
}

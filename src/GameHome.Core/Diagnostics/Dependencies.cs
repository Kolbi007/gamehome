// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Text;
using System.Text.RegularExpressions;

namespace GameHome.Core.Diagnostics;

/// <summary>En Microsoft-pakke som spill ofte trenger (DirectX-tillegg, Visual C++).</summary>
/// <param name="Id">Fast ID, for eksempel «vc2010-x86».</param>
/// <param name="Name">Navn for brukeren.</param>
/// <param name="DownloadUrl">Offisiell nedlasting fra Microsoft.</param>
/// <param name="LocalInstaller">Filnavn (regex) for installasjonsfilen hvis spillet har den med selv.</param>
public sealed record Redistributable(string Id, string Name, string DownloadUrl, string LocalInstaller);

/// <summary>Resultatet av sjekken for én programfil.</summary>
public sealed record DependencyResult(bool Is64Bit, IReadOnlyList<string> MissingDlls, IReadOnlyList<Redistributable> Missing)
{
    public bool Ok => Missing.Count == 0;
}

/// <summary>
/// Finner hvilke DLL-filer en programfil laster direkte (importtabellen i PE-filen), og hvilke av dem som mangler på
/// maskinen, koblet til Microsoft-pakken som gir dem. Leser bare filen; kjører ingenting.
/// </summary>
public static partial class Dependencies
{
    /// <summary>Windows-feilkoden når en DLL ikke finnes (STATUS_DLL_NOT_FOUND) som prosessens sluttkode.</summary>
    public const int DllNotFound = unchecked((int)0xC0000135);

    /// <summary>Feil 32/64-biters DLL (STATUS_INVALID_IMAGE_FORMAT).</summary>
    public const int BadImage = unchecked((int)0xC000007B);

    public static Redistributable DirectX { get; } = new(
        "directx-jun2010", "DirectX (juni 2010)",
        "https://download.microsoft.com/download/1/7/1/1718CCC4-6315-4D8E-9543-8E28A4E18C4C/dxwebsetup.exe",
        @"^(dxsetup|dxwebsetup)\.exe$");

    private static Redistributable Vc(string version, string year, bool is64, string url) => new(
        $"vc{year}-{(is64 ? "x64" : "x86")}", $"Visual C++ {version} ({(is64 ? "64" : "32")}-bit)", url,
        is64 ? $@"^vc_?redist[_.]?x64\.exe$" : $@"^vc_?redist[_.]?x86\.exe$");

    private static readonly (Regex Dll, Func<bool, Redistributable> Package)[] Catalog =
    [
        (DirectXDll(), _ => DirectX),
        (Vc2010Dll(), is64 => Vc("2010", "2010", is64, is64
            ? "https://download.microsoft.com/download/1/6/5/165255E7-1014-4D0A-B094-B6A430A6BFFC/vcredist_x64.exe"
            : "https://download.microsoft.com/download/1/6/5/165255E7-1014-4D0A-B094-B6A430A6BFFC/vcredist_x86.exe")),
        (Vc2012Dll(), is64 => Vc("2012", "2012", is64, is64
            ? "https://download.microsoft.com/download/1/6/B/16B06F60-3B20-4FF2-B699-5E9B7962F9AE/VSU_4/vcredist_x64.exe"
            : "https://download.microsoft.com/download/1/6/B/16B06F60-3B20-4FF2-B699-5E9B7962F9AE/VSU_4/vcredist_x86.exe")),
        (Vc2013Dll(), is64 => Vc("2013", "2013", is64, is64
            ? "https://aka.ms/highdpimfc2013x64enu"
            : "https://aka.ms/highdpimfc2013x86enu")),
        (Vc2015Dll(), is64 => Vc("2015–2022", "2015", is64, is64
            ? "https://aka.ms/vs/17/release/vc_redist.x64.exe"
            : "https://aka.ms/vs/17/release/vc_redist.x86.exe")),
    ];

    /// <summary>Pakken som gir DLL-filen, eller null hvis den er en del av Windows eller ukjent for GameHome.</summary>
    public static Redistributable? PackageFor(string dll, bool is64Bit)
    {
        ArgumentNullException.ThrowIfNull(dll);
        foreach (var (pattern, package) in Catalog)
        {
            if (pattern.IsMatch(dll))
            {
                return package(is64Bit);
            }
        }

        return null;
    }

    /// <summary>
    /// Sjekker en programfil. <paramref name="exists"/> sier om en DLL finnes (i spillets mappe eller i Windows' mappe
    /// for riktig arkitektur). Bare DLL-er GameHome kjenner pakken til, regnes som manglende; resten er Windows' egne.
    /// </summary>
    public static DependencyResult Check(byte[] exe, Func<string, bool, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(exists);
        var (is64, dlls) = ReadImports(exe);
        return Evaluate(is64, dlls, exists);
    }

    private static DependencyResult Evaluate(bool is64, IReadOnlyList<string> dlls, Func<string, bool, bool> exists)
    {
        var missingDlls = new List<string>();
        var missing = new List<Redistributable>();
        foreach (var dll in dlls)
        {
            if (PackageFor(dll, is64) is not { } package || exists(dll, is64))
            {
                continue;
            }

            missingDlls.Add(dll);
            if (!missing.Exists(p => p.Id == package.Id))
            {
                missing.Add(package);
            }
        }

        return new DependencyResult(is64, missingDlls, missing);
    }

    /// <summary>
    /// Som <see cref="ReadImports(byte[])"/>, men leser bare de bytene som trengs fra disk (programfiler kan være flere
    /// hundre MB; hele filen skal ikke i minnet).
    /// </summary>
    public static (bool Is64Bit, IReadOnlyList<string> Dlls) ReadImports(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        byte[] Read(long offset, int count)
        {
            if (offset < 0 || offset >= stream.Length)
            {
                return [];
            }

            stream.Position = offset;
            var buffer = new byte[(int)Math.Min(count, stream.Length - offset)];
            stream.ReadExactly(buffer);
            return buffer;
        }

        var header = Read(0, 4096);
        if (header.Length < 0x40 || header[0] != 'M' || header[1] != 'Z')
        {
            return (false, []);
        }

        var pe = BitConverter.ToInt32(header, 0x3C);
        if (pe <= 0 || pe > 3500)
        {
            return (false, []);
        }

        // Hodet og seksjonstabellen ligger i de første 4 KB; importtabellen og navnene leses der de ligger.
        var (is64, sectionTable, sections, importRva) = Headers(header, pe);
        if (sections == 0 || importRva == 0)
        {
            return (is64, []);
        }

        long ToOffset(uint rva)
        {
            for (var i = 0; i < sections; i++)
            {
                var s = sectionTable + (40 * i);
                if (s + 40 > header.Length)
                {
                    break;
                }

                var virtualSize = BitConverter.ToUInt32(header, s + 8);
                var virtualAddress = BitConverter.ToUInt32(header, s + 12);
                var rawSize = BitConverter.ToUInt32(header, s + 16);
                var raw = BitConverter.ToUInt32(header, s + 20);
                if (rva >= virtualAddress && rva < virtualAddress + Math.Max(virtualSize, rawSize))
                {
                    return raw + (rva - virtualAddress);
                }
            }

            return -1;
        }

        var names = new List<string>();
        var entry = ToOffset(importRva);
        while (entry > 0 && names.Count < 512)
        {
            var descriptor = Read(entry, 20);
            if (descriptor.Length < 20)
            {
                break;
            }

            var nameRva = BitConverter.ToUInt32(descriptor, 12);
            var start = nameRva == 0 ? -1 : ToOffset(nameRva);
            if (start < 0)
            {
                break;
            }

            var raw = Read(start, 260);
            var end = Array.IndexOf(raw, (byte)0);
            names.Add(Encoding.ASCII.GetString(raw, 0, end < 0 ? raw.Length : end));
            entry += 20;
        }

        return (is64, names);
    }

    private static (bool Is64, int SectionTable, int Sections, uint ImportRva) Headers(byte[] b, int pe)
    {
        if (pe + 24 > b.Length || b[pe] != 'P' || b[pe + 1] != 'E')
        {
            return (false, 0, 0, 0);
        }

        var sections = BitConverter.ToUInt16(b, pe + 6);
        var optionalSize = BitConverter.ToUInt16(b, pe + 20);
        var optional = pe + 24;
        var is64 = BitConverter.ToUInt16(b, optional) == 0x20B;
        var directory = optional + (is64 ? 112 : 96) + 8;
        var importRva = directory + 4 <= b.Length ? BitConverter.ToUInt32(b, directory) : 0;
        return (is64, optional + optionalSize, sections, importRva);
    }

    /// <summary>Sjekker en programfil på disk (leser bare det som trengs).</summary>
    public static DependencyResult Check(Stream exe, Func<string, bool, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(exists);
        var (is64, dlls) = ReadImports(exe);
        return Evaluate(is64, dlls, exists);
    }

    /// <summary>
    /// Som <see cref="Check(Stream, Func{string, bool, bool})"/>, men følger også DLL-filene spillet har med selv
    /// (<paramref name="openLocal"/> åpner en DLL fra spillmappen, eller gir null), høyst <paramref name="maxDepth"/>
    /// ledd ned. AC Rogue: ACC.exe → Tobii.EyeX.Client.dll → MSVCR110.dll (Visual C++ 2012), målt 2026-11-02.
    /// Manglende DLL-er oppgis med filen som trenger dem, for eksempel «MSVCR110.dll (Tobii.EyeX.Client.dll)».
    /// </summary>
    public static DependencyResult CheckTree(Stream exe, Func<string, Stream?> openLocal, Func<string, bool, bool> exists, int maxDepth = 3)
    {
        ArgumentNullException.ThrowIfNull(openLocal);
        ArgumentNullException.ThrowIfNull(exists);
        var (is64, rootImports) = ReadImports(exe);
        var missingDlls = new List<string>();
        var missing = new List<Redistributable>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string? Via, IReadOnlyList<string> Imports, int Depth)>();
        queue.Enqueue((null, rootImports, 0));
        while (queue.Count > 0 && visited.Count < 64)
        {
            var (via, imports, depth) = queue.Dequeue();
            foreach (var dll in imports)
            {
                if (PackageFor(dll, is64) is { } package)
                {
                    if (!exists(dll, is64))
                    {
                        var label = via is null ? dll : $"{dll} ({via})";
                        if (!missingDlls.Contains(label, StringComparer.OrdinalIgnoreCase))
                        {
                            missingDlls.Add(label);
                        }

                        if (!missing.Exists(p => p.Id == package.Id))
                        {
                            missing.Add(package);
                        }
                    }

                    continue;
                }

                // En DLL spillet har med selv: les dens avhengigheter også.
                if (depth < maxDepth && visited.Add(dll) && openLocal(dll) is { } stream)
                {
                    using (stream)
                    {
                        queue.Enqueue((dll, ReadImports(stream).Dlls, depth + 1));
                    }
                }
            }
        }

        return new DependencyResult(is64, missingDlls, missing);
    }

    /// <summary>Leser importtabellen i en PE-fil (Windows-programfil): 64-bit eller ikke, og DLL-navnene.</summary>
    public static (bool Is64Bit, IReadOnlyList<string> Dlls) ReadImports(byte[] b)
    {
        ArgumentNullException.ThrowIfNull(b);
        try
        {
            if (b.Length < 0x40 || b[0] != 'M' || b[1] != 'Z')
            {
                return (false, []);
            }

            var pe = BitConverter.ToInt32(b, 0x3C);
            if (pe <= 0 || pe + 24 > b.Length || b[pe] != 'P' || b[pe + 1] != 'E')
            {
                return (false, []);
            }

            var sections = BitConverter.ToUInt16(b, pe + 6);
            var optionalSize = BitConverter.ToUInt16(b, pe + 20);
            var optional = pe + 24;
            var is64 = BitConverter.ToUInt16(b, optional) == 0x20B;
            var importRva = BitConverter.ToUInt32(b, optional + (is64 ? 112 : 96) + 8);
            var sectionTable = optional + optionalSize;

            int ToOffset(uint rva)
            {
                for (var i = 0; i < sections; i++)
                {
                    var s = sectionTable + (40 * i);
                    var virtualSize = BitConverter.ToUInt32(b, s + 8);
                    var virtualAddress = BitConverter.ToUInt32(b, s + 12);
                    var rawSize = BitConverter.ToUInt32(b, s + 16);
                    var raw = BitConverter.ToUInt32(b, s + 20);
                    if (rva >= virtualAddress && rva < virtualAddress + Math.Max(virtualSize, rawSize))
                    {
                        return (int)(raw + (rva - virtualAddress));
                    }
                }

                return -1;
            }

            var names = new List<string>();
            var entry = importRva == 0 ? -1 : ToOffset(importRva);
            while (entry > 0 && entry + 20 <= b.Length && names.Count < 512)
            {
                var nameRva = BitConverter.ToUInt32(b, entry + 12);
                if (nameRva == 0)
                {
                    break;
                }

                var start = ToOffset(nameRva);
                if (start < 0)
                {
                    break;
                }

                var end = start;
                while (end < b.Length && b[end] != 0)
                {
                    end++;
                }

                names.Add(Encoding.ASCII.GetString(b, start, end - start));
                entry += 20;
            }

            return (is64, names);
        }
        catch (ArgumentException)
        {
            return (false, []); // Ødelagt eller avkortet fil.
        }
    }

    [GeneratedRegex(@"^(d3dx9_\d+|d3dx10(_\d+)?|d3dx11_\d+|d3dcompiler_(3\d|4[0-3])|xinput1_[123]|xaudio2_[0-7]|x3daudio1_\d|xactengine\d_\d|xapofx1_\d)\.dll$", RegexOptions.IgnoreCase)]
    private static partial Regex DirectXDll();

    [GeneratedRegex(@"^(msvcr100|msvcp100|mfc100u?)\.dll$", RegexOptions.IgnoreCase)]
    private static partial Regex Vc2010Dll();

    [GeneratedRegex(@"^(msvcr110|msvcp110|mfc110u?)\.dll$", RegexOptions.IgnoreCase)]
    private static partial Regex Vc2012Dll();

    [GeneratedRegex(@"^(msvcr120|msvcp120|mfc120u?)\.dll$", RegexOptions.IgnoreCase)]
    private static partial Regex Vc2013Dll();

    [GeneratedRegex(@"^(vcruntime140(_1)?|msvcp140(_1|_2|_atomic_wait|_codecvt_ids)?|concrt140|vcomp140|mfc140u?)\.dll$", RegexOptions.IgnoreCase)]
    private static partial Regex Vc2015Dll();
}

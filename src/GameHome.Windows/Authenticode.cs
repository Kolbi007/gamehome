// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace GameHome.Windows;

/// <summary>
/// Sjekker at en fil er gyldig digitalt signert (Windows' egen kontroll, WinVerifyTrust) og at signaturen er
/// Microsofts. Brukes før en installasjonsfil for DirectX eller Visual C++ kjøres.
/// </summary>
public static class Authenticode
{
    public static bool IsSignedByMicrosoft(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!IsTrusted(path))
        {
            return false;
        }

        try
        {
#pragma warning disable SYSLIB0057 // Leser bare hvem som signerte; gyldigheten er sjekket av WinVerifyTrust over.
            using var signer = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            return signer.Subject.Contains("O=Microsoft Corporation", StringComparison.Ordinal);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false;
        }
    }

    private static unsafe bool IsTrusted(string path)
    {
        fixed (char* file = path)
        {
            var fileInfo = new WinTrustFileInfo
            {
                StructSize = (uint)sizeof(WinTrustFileInfo),
                FilePath = (nint)file,
            };
            var data = new WinTrustData
            {
                StructSize = (uint)sizeof(WinTrustData),
                UiChoice = 2, // WTD_UI_NONE
                RevocationChecks = 0, // WTD_REVOKE_NONE (fungerer uten nett mot tilbakekallingslister)
                UnionChoice = 1, // WTD_CHOICE_FILE
                FileInfo = (nint)(&fileInfo),
                StateAction = 0,
            };
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2
            return TrustNative.WinVerifyTrust(-1, ref action, (nint)(&data)) == 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint StructSize;
        public nint FilePath;
        public nint FileHandle;
        public nint KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint StructSize;
        public nint PolicyCallbackData;
        public nint SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public nint FileInfo;
        public uint StateAction;
        public nint StateData;
        public nint UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public nint SignatureSettings;
    }
}

internal static partial class TrustNative
{
    [LibraryImport("wintrust.dll")]
    internal static partial int WinVerifyTrust(nint window, ref Guid action, nint data);
}

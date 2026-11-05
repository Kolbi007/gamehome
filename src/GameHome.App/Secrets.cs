// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using Windows.Security.Credentials;
using Windows.Storage;

namespace GameHome.App;

/// <summary>
/// API-nøkler i Windows' sikre lager (Credential Locker), aldri i repoet, biblioteket eller loggen. En nøkkel legges inn
/// ved å legge filen <c>&lt;navn&gt;.txt</c> i appens LocalState (over SSH ved oppsett); den flyttes inn i lageret og filen
/// slettes. Mangler en nøkkel, er kilden av (fase 4 og Steam-biblioteket virker først når nøklene er lagt inn).
/// </summary>
internal static class Secrets
{
    public const string SteamGridDb = "steamgriddb";
    public const string IgdbClientId = "igdb-client-id";
    public const string IgdbClientSecret = "igdb-client-secret";
    public const string SteamWebApiKey = "steam-webapi-key";

    /// <summary>Steam-ID (64-bit) eller profilnavn. Ikke hemmelig, men lagres på samme måte for enkelhets skyld.</summary>
    public const string SteamId = "steam-id";

    private const string ResourcePrefix = "GameHome.";


    /// <summary>Lagrer (eller erstatter) nøkkelen i Windows' sikre lager. <paramref name="log"/> får aldri selve nøkkelen.</summary>
    public static bool Store(string name, string value, Action<string> log, string resource = "", string user = "key")
    {
        ArgumentNullException.ThrowIfNull(log);
        var target = resource.Length > 0 ? resource : ResourcePrefix + name;
        try
        {
            var vault = new PasswordVault();
            try
            {
                vault.Remove(vault.Retrieve(target, user));
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // Fantes ikke fra før.
            }

            vault.Add(new PasswordCredential(target, user, value.Trim()));
            log($"Nøkkelen «{name}» er lagret i Windows' sikre lager");
            return true;
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            log($"Kunne ikke lagre nøkkelen «{name}»: {ex.Message}");
            return false;
        }
    }
    /// <summary>Henter nøkkelen, eller null hvis den ikke er lagt inn. <paramref name="log"/> får aldri selve nøkkelen.</summary>
    public static string? Load(string name, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        var vault = new PasswordVault();
        var resource = ResourcePrefix + name;
        try
        {
            var file = Path.Combine(ApplicationData.Current.LocalFolder.Path, name + ".txt");
            if (File.Exists(file))
            {
                var fromFile = File.ReadAllText(file).Trim();
                File.Delete(file);
                if (fromFile.Length > 0)
                {
                    vault.Add(new PasswordCredential(resource, "key", fromFile));
                    log($"Nøkkelen «{name}» er flyttet til Windows' sikre lager; filen er slettet");
                }
            }

            var credential = vault.Retrieve(resource, "key");
            credential.RetrievePassword();
            return string.IsNullOrWhiteSpace(credential.Password) ? null : credential.Password;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or IOException or UnauthorizedAccessException)
        {
            // Retrieve kaster når nøkkelen ikke finnes.
            return null;
        }
    }
}

// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Globalization;

namespace GameHome.Core.Diagnostics;

/// <summary>
/// Enkel tekstlogg med tidsstempel, én linje per hendelse. Skal aldri inneholde tokens, brukernavn,
/// IP-adresser eller personlige stier.
/// </summary>
public sealed class FileLog
{
    private const long MaximumFileBytes = 1024 * 1024;
    private const int MaximumMessageCharacters = 16 * 1024;
    private readonly Lock _gate = new();

    public FileLog(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }

    /// <summary>Kalles med den ferdige linjen etter at den er skrevet. Kan komme fra hvilken som helst tråd.</summary>
    public event EventHandler<string>? Written;

    public void Write(string category, string message)
    {
        if (message.Length > MaximumMessageCharacters)
        {
            message = message[..MaximumMessageCharacters] + " [avkortet]";
        }

        var line = string.Create(CultureInfo.InvariantCulture, $"{DateTimeOffset.Now:HH:mm:ss.fff} [{category}] {message}");
        lock (_gate)
        {
            try
            {
                var directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Keep one previous log. A failed rotation must not damage the existing log.
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length >= MaximumFileBytes)
                {
                    File.Move(FilePath, FilePath + ".1", overwrite: true);
                }

                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Disk full, unavailable directory or locked file: diagnostics must not stop Home.
                // Still notify the in-memory event view below; never recursively log this failure.
            }
        }

        Written?.Invoke(this, line);
    }
}

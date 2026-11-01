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
    private readonly Lock _gate = new();

    public FileLog(string filePath)
    {
        FilePath = filePath;
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public string FilePath { get; }

    /// <summary>Kalles med den ferdige linjen etter at den er skrevet. Kan komme fra hvilken som helst tråd.</summary>
    public event EventHandler<string>? Written;

    public void Write(string category, string message)
    {
        var line = string.Create(CultureInfo.InvariantCulture, $"{DateTimeOffset.Now:HH:mm:ss.fff} [{category}] {message}");
        lock (_gate)
        {
            File.AppendAllText(FilePath, line + Environment.NewLine);
        }

        Written?.Invoke(this, line);
    }
}

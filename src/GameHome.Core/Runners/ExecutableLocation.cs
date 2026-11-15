// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

namespace GameHome.Core.Runners;

public static class ExecutableLocation
{
    /// <summary>Checks directory membership on normalized paths, including nested game binaries.</summary>
    public static bool IsInDirectory(string directory, string path)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(path))
        {
            return false;
        }

        try
        {
            var relative = Path.GetRelativePath(directory, path);
            return relative is not ("." or "..") && !Path.IsPathRooted(relative)
                && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}

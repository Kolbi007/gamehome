// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

namespace GameHome.Core.Tests;

/// <summary>Regler for repoet som er lette å glemme, sjekket ved hver build.</summary>
public class RepositoryRulesTests
{
    private const string SpdxLine = "SPDX-License-Identifier: GPL-3.0-or-later";

    [Fact]
    public void Every_source_file_has_an_spdx_header()
    {
        var root = FindRepositoryRoot();
        var files = new[] { "src", "tests" }
            .SelectMany(dir => Directory.EnumerateFiles(Path.Combine(root, dir), "*.*", SearchOption.AllDirectories))
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".xaml", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        var missing = files.Where(f => !File.ReadLines(f).Take(3).Any(l => l.Contains(SpdxLine, StringComparison.Ordinal)))
            .Select(f => Path.GetRelativePath(root, f))
            .ToList();

        Assert.True(missing.Count == 0, "Mangler SPDX-header: " + string.Join(", ", missing));
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GameHome.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Fant ikke GameHome.slnx.");
    }
}

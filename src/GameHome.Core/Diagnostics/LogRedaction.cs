// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Net;
using System.Text.RegularExpressions;

namespace GameHome.Core.Diagnostics;

internal static partial class LogRedaction
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private const string Hidden = "[redacted]";

    public static string Apply(string message)
    {
        message = Authorization().Replace(message, "$1 " + Hidden);
        message = Fields().Replace(message, "$1" + Hidden);
        message = UrlUserInfo().Replace(message, Hidden + "@");
        message = UrlData().Replace(message, "$1?" + Hidden);
        message = Paths().Replace(message, "[path]");
        message = Email().Replace(message, Hidden);
        message = Ipv6().Replace(message, match => IPAddress.TryParse(match.Value.TrimEnd('.'), out _) ? "[ip]" : match.Value);
        return Ipv4().Replace(message, match => IPAddress.TryParse(match.Value, out _) ? "[ip]" : match.Value);
    }

    [GeneratedRegex(@"\b(Bearer|Basic)[ \t]+[a-z0-9+/.=_-]+", Options)]
    private static partial Regex Authorization();

    [GeneratedRegex("""(\b(?:access[_-]?token|refresh[_-]?token|id[_-]?token|client[_-]?secret|api[_-]?key|x-api-key|token|key|password|passwd|user(?:[_-]?name)?)["']?[ \t]*[:=][ \t]*)(?:"(?:\\.|[^"\\\r\n])*(?:"|$)|'[^'\r\n]*(?:'|$)|[^ \t\r\n,;&}"']+)""", Options)]
    private static partial Regex Fields();

    [GeneratedRegex("""(?<=://)[^/\s:@]+(?::[^/\s@]*)?@""", Options)]
    private static partial Regex UrlUserInfo();

    [GeneratedRegex("""(https?://[^\s"'<>?#]+)[?#][^\s"'<>]*""", Options)]
    private static partial Regex UrlData();

    [GeneratedRegex("""(?<!\w)(?:[a-z]:[\\/]|\\\\[^\\/ \r\n]+[\\/]|/(?:home|Users)/)[^<>"'«»\r\n]+""", Options)]
    private static partial Regex Paths();

    [GeneratedRegex(@"[a-z0-9._%+-]+@[a-z0-9.-]+\.[a-z]{2,}", Options)]
    private static partial Regex Email();

    [GeneratedRegex(@"(?<![\w:])(?:[a-f0-9]*:){2}[a-f0-9:.]*(?:%[\w.-]+)?(?![\w:])", Options)]
    private static partial Regex Ipv6();

    [GeneratedRegex(@"(?<!versjon )(?<!version )(?<![\w.])(?:[0-9]{1,3}\.){3}[0-9]{1,3}(?!\w|\.[0-9])", Options)]
    private static partial Regex Ipv4();
}

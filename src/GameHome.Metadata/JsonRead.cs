// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Text.Json;

namespace GameHome.Metadata;

/// <summary>Små, tilgivende oppslag i <see cref="JsonElement"/>: feil type eller manglende felt gir null.</summary>
internal static class JsonRead
{
    public static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public static int? Int(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;

    public static long? Long(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : null;

    public static double? Double(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ? number : null;

    public static bool Bool(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.True;

    /// <summary>En liste under <paramref name="name"/>, eller ingenting.</summary>
    public static IEnumerable<JsonElement> Array(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];

    /// <summary>Unix-tid i sekunder som UTC.</summary>
    public static DateTime? UnixSeconds(JsonElement element, string name) =>
        Long(element, name) is { } seconds && seconds is > -62135596800 and < 253402300800
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
            : null;
}

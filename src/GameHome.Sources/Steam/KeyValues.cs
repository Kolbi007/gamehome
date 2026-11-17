// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Text;

namespace GameHome.Sources.Steam;

/// <summary>
/// En node i Valves tekstformat KeyValues (VDF/ACF): enten en verdi eller en gruppe med barn.
/// Egen, liten leser for det offentlige formatet; bare det Steam-bibliotekfilene bruker.
/// </summary>
public sealed class KeyValues
{
    private const int MaximumNestingDepth = 128;

    private Dictionary<string, KeyValues>? _children;
    private List<string>? _order;

    private KeyValues(string? value) => Value = value;

    public string? Value { get; }

    public IEnumerable<(string Key, KeyValues Node)> Children => _order is null
        ? []
        : _order.Select(k => (k, _children![k]));

    public KeyValues? this[string key] => _children?.GetValueOrDefault(key);

    public string? GetString(string key) => this[key]?.Value;

    /// <summary>Leser en hel fil. Rotnoden har ett barn, for eksempel «libraryfolders» eller «AppState».</summary>
    public static KeyValues Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var position = 0;
        var root = new KeyValues(null);
        ReadChildren(text, ref position, root, expectClosingBrace: false, depth: 0);
        return root;
    }

    private static void ReadChildren(string text, ref int position, KeyValues parent, bool expectClosingBrace, int depth)
    {
        while (true)
        {
            var token = NextToken(text, ref position);
            if (token is null)
            {
                if (expectClosingBrace)
                {
                    throw new FormatException("Uventet slutt på filen: mangler }");
                }

                return;
            }

            if (token is { Quoted: false, Text: "}" })
            {
                if (!expectClosingBrace)
                {
                    throw new FormatException("Uventet }");
                }

                return;
            }

            if (token is { Quoted: false, Text: "{" })
            {
                throw new FormatException("Uventet {");
            }

            var key = token.Value.Text;
            var next = NextToken(text, ref position) ?? throw new FormatException($"Mangler verdi for «{key}»");
            if (next is { Quoted: false, Text: "}" })
            {
                throw new FormatException($"Mangler verdi for «{key}»");
            }

            KeyValues child;
            if (next is { Quoted: false, Text: "{" })
            {
                if (depth >= MaximumNestingDepth)
                {
                    throw new FormatException("KeyValues er nøstet for dypt");
                }

                child = new KeyValues(null);
                ReadChildren(text, ref position, child, expectClosingBrace: true, depth + 1);
            }
            else
            {
                child = new KeyValues(next.Text);
            }

            // Ved like nøkler vinner den siste, som i Steam.
            parent._children ??= new Dictionary<string, KeyValues>(StringComparer.OrdinalIgnoreCase);
            parent._order ??= [];
            if (parent._children.TryAdd(key, child))
            {
                parent._order.Add(key);
            }
            else
            {
                parent._children[key] = child;
            }
        }
    }

    private static (string Text, bool Quoted)? NextToken(string text, ref int position)
    {
        while (position < text.Length)
        {
            var c = text[position];
            if (char.IsWhiteSpace(c))
            {
                position++;
            }
            else if (c == '/' && position + 1 < text.Length && text[position + 1] == '/')
            {
                while (position < text.Length && text[position] != '\n')
                {
                    position++;
                }
            }
            else
            {
                break;
            }
        }

        if (position >= text.Length)
        {
            return null;
        }

        if (text[position] is '{' or '}')
        {
            return (text[position++].ToString(), false);
        }

        if (text[position] == '"')
        {
            var contentStart = ++position;
            while (position < text.Length && text[position] is not ('"' or '\\'))
            {
                position++;
            }

            if (position < text.Length && text[position] == '"')
            {
                var value = text[contentStart..position];
                position++;
                return (value, true);
            }

            var builder = new StringBuilder();
            builder.Append(text, contentStart, position - contentStart);
            while (position < text.Length)
            {
                if (text[position] == '"')
                {
                    position++;
                    return (builder.ToString(), true);
                }

                if (text[position] == '\\' && position + 1 < text.Length)
                {
                    position++;
                    builder.Append(text[position] switch { 'n' => '\n', 't' => '\t', _ => text[position] });
                }
                else
                {
                    builder.Append(text[position]);
                }

                position++;
            }

            throw new FormatException("Uventet slutt på filen i sitert token");
        }

        var start = position;
        while (position < text.Length && !char.IsWhiteSpace(text[position]) && text[position] is not ('{' or '}' or '"'))
        {
            position++;
        }

        return (text[start..position], false);
    }
}

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
    private readonly Dictionary<string, KeyValues> _children = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _order = [];

    private KeyValues(string? value) => Value = value;

    public string? Value { get; }

    public IEnumerable<(string Key, KeyValues Node)> Children => _order.Select(k => (k, _children[k]));

    public KeyValues? this[string key] => _children.GetValueOrDefault(key);

    public string? GetString(string key) => this[key]?.Value;

    /// <summary>Leser en hel fil. Rotnoden har ett barn, for eksempel «libraryfolders» eller «AppState».</summary>
    public static KeyValues Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var position = 0;
        var root = new KeyValues(null);
        ReadChildren(text, ref position, root, expectClosingBrace: false);
        return root;
    }

    private static void ReadChildren(string text, ref int position, KeyValues parent, bool expectClosingBrace)
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

            var key = token.Value.Text;
            var next = NextToken(text, ref position) ?? throw new FormatException($"Mangler verdi for «{key}»");
            KeyValues child;
            if (next is { Quoted: false, Text: "{" })
            {
                child = new KeyValues(null);
                ReadChildren(text, ref position, child, expectClosingBrace: true);
            }
            else
            {
                child = new KeyValues(next.Text);
            }

            // Ved like nøkler vinner den siste, som i Steam.
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
            position++;
            var builder = new StringBuilder();
            while (position < text.Length && text[position] != '"')
            {
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

            position++; // avsluttende "
            return (builder.ToString(), true);
        }

        var start = position;
        while (position < text.Length && !char.IsWhiteSpace(text[position]) && text[position] is not ('{' or '}' or '"'))
        {
            position++;
        }

        return (text[start..position], false);
    }
}

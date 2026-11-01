namespace PdfSuite.Pdf.Redaction;

/// <summary>
/// Pulls the string operands out of a content stream, which is where shown text lives. Operators,
/// numbers and names are skipped, and so is inline image data.
/// </summary>
/// <remarks>
/// The strings are joined without separators, because text is routinely split across operands:
/// <c>[(CAN) -20 (ARY)] TJ</c>, or one <c>Tj</c> per glyph. Joining can only make a search match
/// more, never less. The bytes are character codes, which match the text only for simple fonts
/// with standard encodings; text extraction covers the rest.
/// </remarks>
internal static class ContentStrings
{
    public static byte[] Extract(ReadOnlySpan<byte> content)
    {
        var strings = new List<byte>();
        int i = 0;
        while (i < content.Length)
        {
            byte b = content[i];
            if (b == '%')
            {
                while (i < content.Length && content[i] is not ((byte)'\r' or (byte)'\n'))
                {
                    i++;
                }
            }
            else if (b == '(')
            {
                i = ReadLiteral(content, i + 1, strings);
            }
            else if (b == '<' && i + 1 < content.Length && content[i + 1] == '<')
            {
                i += 2;
            }
            else if (b == '<')
            {
                i = ReadHex(content, i + 1, strings);
            }
            else if (IsToken(content, i, "BI"u8))
            {
                i = SkipInlineImage(content, i + 2);
            }
            else
            {
                i++;
            }
        }

        return [.. strings];
    }

    // A literal string: balanced parentheses, with backslash escapes. Returns the index after ')'.
    private static int ReadLiteral(ReadOnlySpan<byte> content, int i, List<byte> output)
    {
        int depth = 1;
        while (i < content.Length)
        {
            byte b = content[i++];
            if (b == '\\' && i < content.Length)
            {
                byte e = content[i++];
                switch (e)
                {
                    case (byte)'n': output.Add((byte)'\n'); break;
                    case (byte)'r': output.Add((byte)'\r'); break;
                    case (byte)'t': output.Add((byte)'\t'); break;
                    case (byte)'b': output.Add((byte)'\b'); break;
                    case (byte)'f': output.Add((byte)'\f'); break;
                    case (byte)'\r':
                        // A backslash before a line break continues the string on the next line.
                        if (i < content.Length && content[i] == '\n')
                        {
                            i++;
                        }

                        break;
                    case (byte)'\n':
                        break;
                    case >= (byte)'0' and <= (byte)'7':
                        int value = e - '0';
                        for (int digits = 1; digits < 3 && i < content.Length && content[i] is >= (byte)'0' and <= (byte)'7'; digits++)
                        {
                            value = (value * 8) + (content[i++] - '0');
                        }

                        output.Add((byte)value);
                        break;
                    default:
                        // \( \) \\ and any unknown escape stand for the character itself.
                        output.Add(e);
                        break;
                }
            }
            else if (b == '(')
            {
                depth++;
                output.Add(b);
            }
            else if (b == ')')
            {
                if (--depth == 0)
                {
                    break;
                }

                output.Add(b);
            }
            else
            {
                output.Add(b);
            }
        }

        return i;
    }

    // A hex string; whitespace is ignored and a final odd digit is followed by an implied 0.
    private static int ReadHex(ReadOnlySpan<byte> content, int i, List<byte> output)
    {
        int high = -1;
        while (i < content.Length && content[i] != '>')
        {
            int digit = HexValue(content[i++]);
            if (digit < 0)
            {
                continue;
            }

            if (high < 0)
            {
                high = digit;
            }
            else
            {
                output.Add((byte)((high << 4) | digit));
                high = -1;
            }
        }

        if (high >= 0)
        {
            output.Add((byte)(high << 4));
        }

        return i + 1;
    }

    // Inline image: BI <dictionary> ID <binary data> EI. The data has no length, so it ends at the
    // first "EI" that stands as a token of its own.
    private static int SkipInlineImage(ReadOnlySpan<byte> content, int i)
    {
        while (i < content.Length && !IsToken(content, i, "ID"u8))
        {
            i++;
        }

        for (i += 3; i < content.Length; i++)
        {
            if (IsToken(content, i, "EI"u8))
            {
                return i + 2;
            }
        }

        return content.Length;
    }

    private static bool IsToken(ReadOnlySpan<byte> content, int i, ReadOnlySpan<byte> token) =>
        content[i..].StartsWith(token)
        && (i == 0 || IsWhitespace(content[i - 1]))
        && (i + token.Length == content.Length || IsWhitespace(content[i + token.Length]));

    private static bool IsWhitespace(byte b) => b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or (byte)'\f' or 0;

    private static int HexValue(byte b) => b switch
    {
        >= (byte)'0' and <= (byte)'9' => b - '0',
        >= (byte)'a' and <= (byte)'f' => b - 'a' + 10,
        >= (byte)'A' and <= (byte)'F' => b - 'A' + 10,
        _ => -1,
    };
}

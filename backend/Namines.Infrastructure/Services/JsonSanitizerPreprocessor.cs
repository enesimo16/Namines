using System;
using System.Text;

namespace Namines.Infrastructure.Services;

public static class JsonSanitizerPreprocessor
{
    public static string Sanitize(string? json)
    {
        if (string.IsNullOrEmpty(json)) return string.Empty;

        var sb = new StringBuilder(json.Length);
        bool inString = false;
        bool isEscaped = false;

        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];

            if (inString)
            {
                if (isEscaped)
                {
                    sb.Append(c);
                    isEscaped = false;
                }
                else if (c == '\\')
                {
                    // Check if this is a valid JSON escape sequence start
                    if (i + 1 < json.Length)
                    {
                        char next = json[i + 1];
                        if (next == '"' || next == '\\' || next == '/' || next == 'b' || next == 'f' || next == 'n' || next == 'r' || next == 't' || next == 'u')
                        {
                            sb.Append(c);
                            isEscaped = true; // This is a valid escape sequence start
                        }
                        else
                        {
                            // This is an unescaped backslash! Double it to escape it.
                            sb.Append("\\\\");
                        }
                    }
                    else
                    {
                        // Trailing backslash, double it
                        sb.Append("\\\\");
                    }
                }
                else if (c == '"')
                {
                    sb.Append(c);
                    inString = false;
                }
                else if (c == '\n')
                {
                    sb.Append("\\n");
                }
                else if (c == '\r')
                {
                    sb.Append("\\r");
                }
                else if (c == '\t')
                {
                    sb.Append("\\t");
                }
                else if (char.IsControl(c))
                {
                    // Escape other control characters
                    sb.AppendFormat("\\u{0:X4}", (int)c);
                }
                else
                {
                    sb.Append(c);
                }
            }
            else
            {
                sb.Append(c);
                if (c == '"')
                {
                    inString = true;
                    isEscaped = false;
                }
            }
        }

        return sb.ToString();
    }
}

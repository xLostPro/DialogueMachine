using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DialogueMachine.Runtime
{
    // Unity has no System.Text.Json. Objects parse to Dictionary<string, object>, arrays to
    // List<object> and numbers to double, so 64-bit values must travel as strings.
    public static class MiniJson
    {
        private const int MaxDepth = 64;

        public static object Parse(string json)
        {
            if (json == null) throw new FormatException("JSON text is missing.");
            var reader = new Reader(json);
            var value = reader.ReadValue(0);
            reader.ExpectEnd();
            return value;
        }

        public static string Serialize(object value)
        {
            var output = new StringBuilder();
            Write(output, value);
            return output.ToString();
        }

        private static void Write(StringBuilder output, object value)
        {
            switch (value)
            {
                case null: output.Append("null"); break;
                case string text: WriteString(output, text); break;
                case bool flag: output.Append(flag ? "true" : "false"); break;
                case double number: output.Append(number.ToString("R", CultureInfo.InvariantCulture)); break;
                case IFormattable number: output.Append(number.ToString(null, CultureInfo.InvariantCulture)); break;
                case IDictionary map:
                    output.Append('{');
                    var firstEntry = true;
                    foreach (DictionaryEntry entry in map)
                    {
                        if (!firstEntry) output.Append(',');
                        firstEntry = false;
                        WriteString(output, (string)entry.Key);
                        output.Append(':');
                        Write(output, entry.Value);
                    }
                    output.Append('}');
                    break;
                case IEnumerable list:
                    output.Append('[');
                    var firstItem = true;
                    foreach (var item in list)
                    {
                        if (!firstItem) output.Append(',');
                        firstItem = false;
                        Write(output, item);
                    }
                    output.Append(']');
                    break;
                default: throw new ArgumentException("Cannot write " + value.GetType().Name + " as JSON.");
            }
        }

        private static void WriteString(StringBuilder output, string text)
        {
            output.Append('"');
            foreach (var c in text)
            {
                switch (c)
                {
                    case '"': output.Append("\\\""); break;
                    case '\\': output.Append("\\\\"); break;
                    case '\n': output.Append("\\n"); break;
                    case '\r': output.Append("\\r"); break;
                    case '\t': output.Append("\\t"); break;
                    default:
                        if (c < ' ') output.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else output.Append(c);
                        break;
                }
            }
            output.Append('"');
        }

        private sealed class Reader
        {
            private readonly string json;
            private int position;

            public Reader(string json) { this.json = json; }

            public void ExpectEnd()
            {
                SkipWhitespace();
                if (position != json.Length) throw Error("Unexpected text after the JSON value");
            }

            public object ReadValue(int depth)
            {
                if (depth > MaxDepth) throw Error("JSON is nested too deeply");
                SkipWhitespace();
                if (position >= json.Length) throw Error("Unexpected end of JSON");
                var c = json[position];
                if (c == '{') return ReadObject(depth);
                if (c == '[') return ReadArray(depth);
                if (c == '"') return ReadString();
                if (TryWord("true")) return true;
                if (TryWord("false")) return false;
                if (TryWord("null")) return null;
                return ReadNumber();
            }

            private Dictionary<string, object> ReadObject(int depth)
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                position++;
                if (TrySymbol('}')) return result;
                do
                {
                    SkipWhitespace();
                    if (position >= json.Length || json[position] != '"') throw Error("Expected a property name");
                    var key = ReadString();
                    if (!TrySymbol(':')) throw Error("Expected ':'");
                    result[key] = ReadValue(depth + 1);
                } while (TrySymbol(','));
                if (!TrySymbol('}')) throw Error("Expected ',' or '}'");
                return result;
            }

            private List<object> ReadArray(int depth)
            {
                var result = new List<object>();
                position++;
                if (TrySymbol(']')) return result;
                do result.Add(ReadValue(depth + 1));
                while (TrySymbol(','));
                if (!TrySymbol(']')) throw Error("Expected ',' or ']'");
                return result;
            }

            private string ReadString()
            {
                var result = new StringBuilder();
                position++;
                while (true)
                {
                    if (position >= json.Length) throw Error("Unterminated string");
                    var c = json[position++];
                    if (c == '"') return result.ToString();
                    if (c < ' ') throw Error("Unescaped control character in string");
                    result.Append(c == '\\' ? ReadEscape() : c);
                }
            }

            private char ReadEscape()
            {
                if (position >= json.Length) throw Error("Unterminated string");
                var c = json[position++];
                switch (c)
                {
                    case '"': case '\\': case '/': return c;
                    case 'b': return '\b';
                    case 'f': return '\f';
                    case 'n': return '\n';
                    case 'r': return '\r';
                    case 't': return '\t';
                    case 'u':
                        if (position + 4 <= json.Length && ushort.TryParse(json.Substring(position, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var unit))
                        {
                            position += 4;
                            return (char)unit;
                        }
                        throw Error("Invalid \\u escape");
                    default: throw Error("Invalid escape");
                }
            }

            private double ReadNumber()
            {
                var start = position;
                while (position < json.Length && "+-.eE0123456789".IndexOf(json[position]) >= 0) position++;
                if (position > start && double.TryParse(json.Substring(start, position - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return number;
                throw Error("Expected a JSON value");
            }

            private bool TryWord(string word)
            {
                if (string.CompareOrdinal(json, position, word, 0, word.Length) != 0) return false;
                position += word.Length;
                return true;
            }

            private bool TrySymbol(char symbol)
            {
                SkipWhitespace();
                if (position >= json.Length || json[position] != symbol) return false;
                position++;
                return true;
            }

            private void SkipWhitespace()
            {
                while (position < json.Length && " \t\r\n".IndexOf(json[position]) >= 0) position++;
            }

            private FormatException Error(string message) => new FormatException(message + " at character " + position + ".");
        }
    }
}

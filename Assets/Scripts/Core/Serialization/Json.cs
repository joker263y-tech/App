using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Shadowbound.Core.Serialization
{
    public enum JsonKind
    {
        Null = 0,
        Bool = 1,
        Number = 2,
        String = 3,
        Array = 4,
        Object = 5
    }

    /// <summary>Thrown when text is not valid JSON. Carries the offset for diagnostics.</summary>
    public sealed class JsonParseException : Exception
    {
        public JsonParseException(string message, int position)
            : base(message + " (at character " + position + ")")
        {
            Position = position;
        }

        public int Position { get; private set; }
    }

    /// <summary>
    /// A small JSON tree, used to read and write save files.
    ///
    /// Hand-written rather than using a library because the core targets
    /// netstandard2.1 with no engine references: Unity's JsonUtility lives in
    /// UnityEngine and cannot be used here, and System.Text.Json is not available
    /// in Unity's profile. A save file also needs to survive content changes, so
    /// every read is tolerant and falls back to a default rather than throwing
    /// when a field is missing or of an unexpected type.
    ///
    /// All number formatting and parsing uses the invariant culture. Without that
    /// a machine with a comma decimal separator would write "1,5" and produce an
    /// unreadable save, which is a classic and very hard to diagnose bug.
    /// </summary>
    public sealed class JsonValue
    {
        private const int MaxDepth = 64;

        private bool _bool;
        private double _number;
        private string _string;

        private List<JsonValue> _array;
        private List<KeyValuePair<string, JsonValue>> _properties;
        private Dictionary<string, JsonValue> _lookup;

        private JsonValue(JsonKind kind)
        {
            Kind = kind;
        }

        public JsonKind Kind { get; private set; }

        public bool IsNull
        {
            get { return Kind == JsonKind.Null; }
        }

        // ------------------------------- factories -------------------------------

        public static JsonValue Null()
        {
            return new JsonValue(JsonKind.Null);
        }

        public static JsonValue FromBool(bool value)
        {
            return new JsonValue(JsonKind.Bool) { _bool = value };
        }

        public static JsonValue FromNumber(double value)
        {
            return new JsonValue(JsonKind.Number) { _number = value };
        }

        public static JsonValue FromString(string value)
        {
            return new JsonValue(JsonKind.String) { _string = value ?? string.Empty };
        }

        public static JsonValue NewArray()
        {
            return new JsonValue(JsonKind.Array) { _array = new List<JsonValue>(8) };
        }

        public static JsonValue NewObject()
        {
            return new JsonValue(JsonKind.Object)
            {
                _properties = new List<KeyValuePair<string, JsonValue>>(8),
                _lookup = new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            };
        }

        // --------------------------------- object --------------------------------

        public int Count
        {
            get
            {
                if (Kind == JsonKind.Object)
                {
                    return _properties.Count;
                }

                return Kind == JsonKind.Array ? _array.Count : 0;
            }
        }

        public IEnumerable<KeyValuePair<string, JsonValue>> Properties
        {
            get { return _properties ?? (IEnumerable<KeyValuePair<string, JsonValue>>)Array.Empty<KeyValuePair<string, JsonValue>>(); }
        }

        public bool Has(string key)
        {
            return Kind == JsonKind.Object && key != null && _lookup.ContainsKey(key);
        }

        /// <summary>Reads a property, or null when absent. Never throws, so callers can fall back.</summary>
        public JsonValue Get(string key)
        {
            if (Kind != JsonKind.Object || key == null)
            {
                return null;
            }

            return _lookup.TryGetValue(key, out JsonValue value) ? value : null;
        }

        /// <summary>Sets a property, preserving insertion order for readable diffs.</summary>
        public void Set(string key, JsonValue value)
        {
            if (Kind != JsonKind.Object || string.IsNullOrEmpty(key))
            {
                return;
            }

            JsonValue stored = value ?? Null();

            if (_lookup.TryGetValue(key, out JsonValue existing))
            {
                for (int i = 0; i < _properties.Count; i++)
                {
                    if (_properties[i].Key == key)
                    {
                        _properties[i] = new KeyValuePair<string, JsonValue>(key, stored);
                        break;
                    }
                }
            }
            else
            {
                _properties.Add(new KeyValuePair<string, JsonValue>(key, stored));
            }

            _lookup[key] = stored;
        }

        public void Set(string key, double value)
        {
            Set(key, FromNumber(value));
        }

        public void Set(string key, int value)
        {
            Set(key, FromNumber(value));
        }

        public void Set(string key, long value)
        {
            Set(key, FromNumber(value));
        }

        public void Set(string key, float value)
        {
            Set(key, FromNumber(value));
        }

        public void Set(string key, bool value)
        {
            Set(key, FromBool(value));
        }

        public void Set(string key, string value)
        {
            Set(key, FromString(value));
        }

        public void Remove(string key)
        {
            if (Kind != JsonKind.Object || key == null || !_lookup.Remove(key))
            {
                return;
            }

            for (int i = _properties.Count - 1; i >= 0; i--)
            {
                if (_properties[i].Key == key)
                {
                    _properties.RemoveAt(i);
                }
            }
        }

        // ---------------------------------- array --------------------------------

        public IReadOnlyList<JsonValue> Items
        {
            get { return _array ?? (IReadOnlyList<JsonValue>)Array.Empty<JsonValue>(); }
        }

        public void Add(JsonValue value)
        {
            if (Kind == JsonKind.Array)
            {
                _array.Add(value ?? Null());
            }
        }

        public void Add(double value)
        {
            Add(FromNumber(value));
        }

        public void Add(int value)
        {
            Add(FromNumber(value));
        }

        public void Add(string value)
        {
            Add(FromString(value));
        }

        public JsonValue At(int index)
        {
            if (Kind != JsonKind.Array || index < 0 || index >= _array.Count)
            {
                return null;
            }

            return _array[index];
        }

        // ------------------------------ typed readers ----------------------------

        public double AsNumber(double fallback = 0d)
        {
            if (Kind == JsonKind.Number)
            {
                return double.IsNaN(_number) || double.IsInfinity(_number) ? fallback : _number;
            }

            return fallback;
        }

        public int AsInt(int fallback = 0)
        {
            double value = AsNumber(double.MinValue);
            if (value == double.MinValue)
            {
                return fallback;
            }

            if (value > int.MaxValue)
            {
                return int.MaxValue;
            }

            if (value < int.MinValue)
            {
                return int.MinValue;
            }

            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }

        public long AsLong(long fallback = 0L)
        {
            double value = AsNumber(double.MinValue);
            if (value == double.MinValue)
            {
                return fallback;
            }

            if (value > long.MaxValue)
            {
                return long.MaxValue;
            }

            if (value < long.MinValue)
            {
                return long.MinValue;
            }

            return (long)Math.Round(value, MidpointRounding.AwayFromZero);
        }

        public float AsFloat(float fallback = 0f)
        {
            double value = AsNumber(double.MinValue);
            if (value == double.MinValue)
            {
                return fallback;
            }

            if (value > float.MaxValue)
            {
                return float.MaxValue;
            }

            if (value < float.MinValue)
            {
                return float.MinValue;
            }

            return (float)value;
        }

        public bool AsBool(bool fallback = false)
        {
            if (Kind == JsonKind.Bool)
            {
                return _bool;
            }

            // Accept 0/1 as booleans too, so hand-edited saves stay workable.
            if (Kind == JsonKind.Number)
            {
                return Math.Abs(_number) > 0.5d;
            }

            return fallback;
        }

        public string AsString(string fallback = "")
        {
            return Kind == JsonKind.String ? _string : fallback;
        }

        /// <summary>Reads an array of strings, skipping anything that is not a string.</summary>
        public List<string> AsStringList()
        {
            var result = new List<string>();

            if (Kind != JsonKind.Array)
            {
                return result;
            }

            for (int i = 0; i < _array.Count; i++)
            {
                if (_array[i].Kind == JsonKind.String)
                {
                    result.Add(_array[i]._string);
                }
            }

            return result;
        }

        /// <summary>Reads an array of integers, falling back per element rather than failing the whole list.</summary>
        public int[] AsIntArray()
        {
            if (Kind != JsonKind.Array)
            {
                return Array.Empty<int>();
            }

            var result = new int[_array.Count];
            for (int i = 0; i < _array.Count; i++)
            {
                result[i] = _array[i].AsInt();
            }

            return result;
        }

        // --------------------------------- writing -------------------------------

        public string ToJson(bool indented = false)
        {
            var builder = new StringBuilder(256);
            Write(builder, indented, 0);
            return builder.ToString();
        }

        private void Write(StringBuilder builder, bool indented, int depth)
        {
            switch (Kind)
            {
                case JsonKind.Null:
                    builder.Append("null");
                    return;

                case JsonKind.Bool:
                    builder.Append(_bool ? "true" : "false");
                    return;

                case JsonKind.Number:
                    builder.Append(FormatNumber(_number));
                    return;

                case JsonKind.String:
                    WriteString(builder, _string);
                    return;

                case JsonKind.Array:
                    WriteArray(builder, indented, depth);
                    return;

                default:
                    WriteObject(builder, indented, depth);
                    return;
            }
        }

        private void WriteArray(StringBuilder builder, bool indented, int depth)
        {
            if (_array.Count == 0)
            {
                builder.Append("[]");
                return;
            }

            builder.Append('[');

            for (int i = 0; i < _array.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                NewLine(builder, indented, depth + 1);
                _array[i].Write(builder, indented, depth + 1);
            }

            NewLine(builder, indented, depth);
            builder.Append(']');
        }

        private void WriteObject(StringBuilder builder, bool indented, int depth)
        {
            if (_properties.Count == 0)
            {
                builder.Append("{}");
                return;
            }

            builder.Append('{');

            for (int i = 0; i < _properties.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                NewLine(builder, indented, depth + 1);
                WriteString(builder, _properties[i].Key);
                builder.Append(':');
                if (indented)
                {
                    builder.Append(' ');
                }

                _properties[i].Value.Write(builder, indented, depth + 1);
            }

            NewLine(builder, indented, depth);
            builder.Append('}');
        }

        private static void NewLine(StringBuilder builder, bool indented, int depth)
        {
            if (!indented)
            {
                return;
            }

            builder.Append('\n');
            for (int i = 0; i < depth; i++)
            {
                builder.Append("  ");
            }
        }

        /// <summary>
        /// Formats a number without a trailing ".0" for integral values, so a
        /// hand-edited save reads naturally and byte-for-byte comparisons stay
        /// stable. Uses the round-trip format to avoid losing precision.
        /// </summary>
        private static string FormatNumber(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return "0";
            }

            if (value == Math.Floor(value) && Math.Abs(value) < 1e15)
            {
                return ((long)value).ToString(CultureInfo.InvariantCulture);
            }

            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static void WriteString(StringBuilder builder, string value)
        {
            builder.Append('"');

            if (value != null)
            {
                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];

                    switch (c)
                    {
                        case '"':
                            builder.Append("\\\"");
                            break;
                        case '\\':
                            builder.Append("\\\\");
                            break;
                        case '\n':
                            builder.Append("\\n");
                            break;
                        case '\r':
                            builder.Append("\\r");
                            break;
                        case '\t':
                            builder.Append("\\t");
                            break;
                        case '\b':
                            builder.Append("\\b");
                            break;
                        case '\f':
                            builder.Append("\\f");
                            break;
                        default:
                            if (c < ' ' || c == '\u007f')
                            {
                                builder.Append("\\u");
                                builder.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                            }
                            else
                            {
                                builder.Append(c);
                            }

                            break;
                    }
                }
            }

            builder.Append('"');
        }

        // --------------------------------- parsing -------------------------------

        public static JsonValue Parse(string text)
        {
            if (text == null)
            {
                throw new JsonParseException("Input is null.", 0);
            }

            int index = 0;
            JsonValue value = ParseValue(text, ref index, 0);
            SkipWhitespace(text, ref index);

            if (index < text.Length)
            {
                throw new JsonParseException("Unexpected trailing content.", index);
            }

            return value;
        }

        /// <summary>
        /// Parses without throwing. Returns false and an error message instead,
        /// which is what a save loader needs: a corrupt slot should be reported,
        /// not crash the game.
        /// </summary>
        public static bool TryParse(string text, out JsonValue value, out string error)
        {
            value = null;
            error = null;

            try
            {
                value = Parse(text);
                return true;
            }
            catch (JsonParseException exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static JsonValue ParseValue(string text, ref int index, int depth)
        {
            if (depth > MaxDepth)
            {
                throw new JsonParseException("Nesting is too deep.", index);
            }

            SkipWhitespace(text, ref index);

            if (index >= text.Length)
            {
                throw new JsonParseException("Unexpected end of input.", index);
            }

            char c = text[index];

            switch (c)
            {
                case '{':
                    return ParseObject(text, ref index, depth);
                case '[':
                    return ParseArray(text, ref index, depth);
                case '"':
                    return FromString(ParseString(text, ref index));
                case 't':
                    Expect(text, ref index, "true");
                    return FromBool(true);
                case 'f':
                    Expect(text, ref index, "false");
                    return FromBool(false);
                case 'n':
                    Expect(text, ref index, "null");
                    return Null();
                default:
                    return FromNumber(ParseNumber(text, ref index));
            }
        }

        private static JsonValue ParseObject(string text, ref int index, int depth)
        {
            var result = NewObject();
            index++;

            SkipWhitespace(text, ref index);

            if (index < text.Length && text[index] == '}')
            {
                index++;
                return result;
            }

            while (true)
            {
                SkipWhitespace(text, ref index);

                if (index >= text.Length || text[index] != '"')
                {
                    throw new JsonParseException("Expected a property name.", index);
                }

                string key = ParseString(text, ref index);
                SkipWhitespace(text, ref index);

                if (index >= text.Length || text[index] != ':')
                {
                    throw new JsonParseException("Expected ':' after a property name.", index);
                }

                index++;
                result.Set(key, ParseValue(text, ref index, depth + 1));

                SkipWhitespace(text, ref index);

                if (index >= text.Length)
                {
                    throw new JsonParseException("Unterminated object.", index);
                }

                if (text[index] == ',')
                {
                    index++;
                    continue;
                }

                if (text[index] == '}')
                {
                    index++;
                    return result;
                }

                throw new JsonParseException("Expected ',' or '}'.", index);
            }
        }

        private static JsonValue ParseArray(string text, ref int index, int depth)
        {
            var result = NewArray();
            index++;

            SkipWhitespace(text, ref index);

            if (index < text.Length && text[index] == ']')
            {
                index++;
                return result;
            }

            while (true)
            {
                result.Add(ParseValue(text, ref index, depth + 1));
                SkipWhitespace(text, ref index);

                if (index >= text.Length)
                {
                    throw new JsonParseException("Unterminated array.", index);
                }

                if (text[index] == ',')
                {
                    index++;
                    continue;
                }

                if (text[index] == ']')
                {
                    index++;
                    return result;
                }

                throw new JsonParseException("Expected ',' or ']'.", index);
            }
        }

        private static string ParseString(string text, ref int index)
        {
            index++;
            var builder = new StringBuilder(32);

            while (true)
            {
                if (index >= text.Length)
                {
                    throw new JsonParseException("Unterminated string.", index);
                }

                char c = text[index++];

                if (c == '"')
                {
                    return builder.ToString();
                }

                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }

                if (index >= text.Length)
                {
                    throw new JsonParseException("Unterminated escape sequence.", index);
                }

                char escaped = text[index++];
                switch (escaped)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'u':
                        if (index + 4 > text.Length)
                        {
                            throw new JsonParseException("Truncated unicode escape.", index);
                        }

                        string hex = text.Substring(index, 4);
                        if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                        {
                            throw new JsonParseException("Invalid unicode escape.", index);
                        }

                        builder.Append((char)code);
                        index += 4;
                        break;
                    default:
                        throw new JsonParseException("Unknown escape sequence '\\" + escaped + "'.", index);
                }
            }
        }

        private static double ParseNumber(string text, ref int index)
        {
            int start = index;

            if (index < text.Length && (text[index] == '-' || text[index] == '+'))
            {
                index++;
            }

            while (index < text.Length)
            {
                char c = text[index];
                if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-')
                {
                    index++;
                    continue;
                }

                break;
            }

            string token = text.Substring(start, index - start);

            if (token.Length == 0
                || !double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                throw new JsonParseException("Invalid number '" + token + "'.", start);
            }

            return value;
        }

        private static void Expect(string text, ref int index, string literal)
        {
            if (index + literal.Length > text.Length
                || string.CompareOrdinal(text, index, literal, 0, literal.Length) != 0)
            {
                throw new JsonParseException("Expected '" + literal + "'.", index);
            }

            index += literal.Length;
        }

        private static void SkipWhitespace(string text, ref int index)
        {
            while (index < text.Length)
            {
                char c = text[index];

                if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                {
                    index++;
                    continue;
                }

                // Tolerate a UTF-8 byte order mark at the very start.
                if (c == '\uFEFF' && index == 0)
                {
                    index++;
                    continue;
                }

                break;
            }
        }

        public override string ToString()
        {
            return ToJson(false);
        }
    }
}

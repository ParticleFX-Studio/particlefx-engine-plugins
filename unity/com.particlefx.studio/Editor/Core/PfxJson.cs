// ParticleFX Studio for Unity. MIT licensed.
// Small JSON reader and writer. Pure C#, no UnityEngine dependency, so the
// manifest and API parsing can be unit tested outside Unity. JsonUtility is
// not used because it cannot tell a JSON null from 0 (exports_left_today is
// null when uncapped) and cannot read top level arrays or free form objects.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ParticleFX.Studio.Editor.Core
{
    /// <summary>Thrown when a JSON document cannot be parsed.</summary>
    public sealed class PfxJsonException : Exception
    {
        public PfxJsonException(string message) : base(message) { }
    }

    /// <summary>
    /// Parses JSON into Dictionary&lt;string, object&gt;, List&lt;object&gt;,
    /// string, double, bool or null.
    /// </summary>
    public static class PfxJson
    {
        public static object Parse(string text)
        {
            if (text == null) throw new PfxJsonException("No JSON text.");
            var p = new Parser(text);
            p.SkipWhitespace();
            var value = p.ReadValue(0);
            p.SkipWhitespace();
            if (!p.AtEnd) throw new PfxJsonException("Unexpected text after the JSON value at position " + p.Position + ".");
            return value;
        }

        /// <summary>Parses text and returns the root object, or throws.</summary>
        public static Dictionary<string, object> ParseObject(string text)
        {
            var root = Parse(text) as Dictionary<string, object>;
            if (root == null) throw new PfxJsonException("Expected a JSON object.");
            return root;
        }

        // ---- typed accessors ---------------------------------------------------

        public static object Get(Dictionary<string, object> obj, string key)
        {
            object v;
            return obj != null && obj.TryGetValue(key, out v) ? v : null;
        }

        public static bool Has(Dictionary<string, object> obj, string key)
        {
            return obj != null && obj.ContainsKey(key) && obj[key] != null;
        }

        public static string GetString(Dictionary<string, object> obj, string key, string fallback = null)
        {
            var v = Get(obj, key);
            if (v == null) return fallback;
            var s = v as string;
            if (s != null) return s;
            if (v is double) return ((double)v).ToString(CultureInfo.InvariantCulture);
            if (v is bool) return (bool)v ? "true" : "false";
            return fallback;
        }

        public static double GetDouble(Dictionary<string, object> obj, string key, double fallback = 0)
        {
            var v = Get(obj, key);
            if (v is double) return (double)v;
            var s = v as string;
            double d;
            if (s != null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            return fallback;
        }

        public static double? GetNullableDouble(Dictionary<string, object> obj, string key)
        {
            var v = Get(obj, key);
            if (v is double) return (double)v;
            return null;
        }

        public static int GetInt(Dictionary<string, object> obj, string key, int fallback = 0)
        {
            var v = Get(obj, key);
            if (v is double) return ToInt((double)v);
            var s = v as string;
            int i;
            if (s != null && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) return i;
            return fallback;
        }

        public static int? GetNullableInt(Dictionary<string, object> obj, string key)
        {
            var v = Get(obj, key);
            if (v is double) return ToInt((double)v);
            return null;
        }

        public static bool GetBool(Dictionary<string, object> obj, string key, bool fallback = false)
        {
            var v = Get(obj, key);
            if (v is bool) return (bool)v;
            if (v is double) return (double)v != 0;
            return fallback;
        }

        public static Dictionary<string, object> GetObject(Dictionary<string, object> obj, string key)
        {
            return Get(obj, key) as Dictionary<string, object>;
        }

        public static List<object> GetArray(Dictionary<string, object> obj, string key)
        {
            return Get(obj, key) as List<object>;
        }

        public static List<string> GetStringList(Dictionary<string, object> obj, string key)
        {
            var result = new List<string>();
            var arr = GetArray(obj, key);
            if (arr == null) return result;
            foreach (var item in arr)
            {
                var s = item as string;
                if (s != null) result.Add(s);
            }
            return result;
        }

        static int ToInt(double d)
        {
            if (double.IsNaN(d)) return 0;
            if (d >= int.MaxValue) return int.MaxValue;
            if (d <= int.MinValue) return int.MinValue;
            return (int)Math.Round(d);
        }

        // ---- writer ------------------------------------------------------------

        /// <summary>Serialises dictionaries, lists, strings, numbers, bools and null.</summary>
        public static string Serialize(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, object value)
        {
            if (value == null) { sb.Append("null"); return; }
            var s = value as string;
            if (s != null) { WriteString(sb, s); return; }
            if (value is bool) { sb.Append((bool)value ? "true" : "false"); return; }
            if (value is int) { sb.Append(((int)value).ToString(CultureInfo.InvariantCulture)); return; }
            if (value is long) { sb.Append(((long)value).ToString(CultureInfo.InvariantCulture)); return; }
            if (value is float) { sb.Append(((float)value).ToString("R", CultureInfo.InvariantCulture)); return; }
            if (value is double) { sb.Append(((double)value).ToString("R", CultureInfo.InvariantCulture)); return; }
            var dict = value as IDictionary<string, object>;
            if (dict != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (var kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, kv.Key);
                    sb.Append(':');
                    Write(sb, kv.Value);
                }
                sb.Append('}');
                return;
            }
            var list = value as System.Collections.IEnumerable;
            if (list != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (var item in list)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Write(sb, item);
                }
                sb.Append(']');
                return;
            }
            WriteString(sb, value.ToString());
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---- parser ------------------------------------------------------------

        sealed class Parser
        {
            const int MaxDepth = 64;
            readonly string _s;
            int _i;

            public Parser(string s) { _s = s; }
            public bool AtEnd { get { return _i >= _s.Length; } }
            public int Position { get { return _i; } }

            public void SkipWhitespace()
            {
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\uFEFF') _i++;
                    else break;
                }
            }

            PfxJsonException Error(string what)
            {
                return new PfxJsonException(what + " at position " + _i + ".");
            }

            public object ReadValue(int depth)
            {
                if (depth > MaxDepth) throw Error("JSON nested too deeply");
                SkipWhitespace();
                if (AtEnd) throw Error("Unexpected end of JSON");
                char c = _s[_i];
                switch (c)
                {
                    case '{': return ReadObject(depth);
                    case '[': return ReadArray(depth);
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        throw Error("Unexpected character '" + c + "'");
                }
            }

            void Expect(string word)
            {
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) throw Error("Expected '" + word + "'");
                _i += word.Length;
            }

            Dictionary<string, object> ReadObject(int depth)
            {
                var obj = new Dictionary<string, object>(StringComparer.Ordinal);
                _i++; // {
                SkipWhitespace();
                if (!AtEnd && _s[_i] == '}') { _i++; return obj; }
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _s[_i] != '"') throw Error("Expected a property name");
                    var key = ReadString();
                    SkipWhitespace();
                    if (AtEnd || _s[_i] != ':') throw Error("Expected ':'");
                    _i++;
                    obj[key] = ReadValue(depth + 1);
                    SkipWhitespace();
                    if (AtEnd) throw Error("Unexpected end of JSON in an object");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == '}') { _i++; return obj; }
                    throw Error("Expected ',' or '}'");
                }
            }

            List<object> ReadArray(int depth)
            {
                var list = new List<object>();
                _i++; // [
                SkipWhitespace();
                if (!AtEnd && _s[_i] == ']') { _i++; return list; }
                while (true)
                {
                    list.Add(ReadValue(depth + 1));
                    SkipWhitespace();
                    if (AtEnd) throw Error("Unexpected end of JSON in an array");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == ']') { _i++; return list; }
                    throw Error("Expected ',' or ']'");
                }
            }

            string ReadString()
            {
                _i++; // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw Error("Unterminated string");
                    char c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (AtEnd) throw Error("Unterminated escape");
                    char e = _s[_i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_i + 4 > _s.Length) throw Error("Bad unicode escape");
                            int code;
                            if (!int.TryParse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                throw Error("Bad unicode escape");
                            sb.Append((char)code);
                            _i += 4;
                            break;
                        default: throw Error("Bad escape '\\" + e + "'");
                    }
                }
            }

            double ReadNumber()
            {
                int start = _i;
                if (_s[_i] == '-') _i++;
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') _i++;
                    else break;
                }
                double d;
                if (!double.TryParse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                    throw Error("Bad number");
                return d;
            }
        }
    }
}

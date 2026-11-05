using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FarmWorld.Core
{
    /// <summary>
    /// Small JSON reader for the game's data files (objects become Dictionary&lt;string, object&gt;,
    /// arrays List&lt;object&gt;, numbers double). JsonUtility cannot read dictionaries, which the config uses.
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string json)
        {
            int i = 0;
            var v = Value(json, ref i);
            return v;
        }

        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) return null;
            char c = s[i];
            if (c == '{') return Obj(s, ref i);
            if (c == '[') return Arr(s, ref i);
            if (c == '"') return Str(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var d);
            return d;
        }

        static Dictionary<string, object> Obj(string s, ref int i)
        {
            var d = new Dictionary<string, object>();
            i++;
            while (true)
            {
                Ws(s, ref i);
                if (i >= s.Length) return d;
                if (s[i] == '}') { i++; return d; }
                if (s[i] == ',') { i++; continue; }
                string k = Str(s, ref i);
                Ws(s, ref i);
                if (i < s.Length && s[i] == ':') i++;
                d[k] = Value(s, ref i);
            }
        }

        static List<object> Arr(string s, ref int i)
        {
            var l = new List<object>();
            i++;
            while (true)
            {
                Ws(s, ref i);
                if (i >= s.Length) return l;
                if (s[i] == ']') { i++; return l; }
                if (s[i] == ',') { i++; continue; }
                l.Add(Value(s, ref i));
            }
        }

        static string Str(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++; // opening quote
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c == '\\' && i < s.Length)
                {
                    char e = s[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (i + 4 <= s.Length)
                            {
                                sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber));
                                i += 4;
                            }
                            break;
                        default: sb.Append(e); break;
                    }
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        // ---- typed helpers
        public static Dictionary<string, object> O(object o, string key) =>
            o is Dictionary<string, object> d && d.TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;
        public static List<object> A(object o, string key) =>
            o is Dictionary<string, object> d && d.TryGetValue(key, out var v) ? v as List<object> : null;
        public static double N(object o, string key, double def = 0) =>
            o is Dictionary<string, object> d && d.TryGetValue(key, out var v) && v is double x ? x : def;
        public static string S(object o, string key, string def = "") =>
            o is Dictionary<string, object> d && d.TryGetValue(key, out var v) && v is string x ? x : def;
    }
}

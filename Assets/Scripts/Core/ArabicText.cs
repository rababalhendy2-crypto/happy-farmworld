using System.Collections.Generic;
using System.Text;

namespace FarmWorld.Core
{
    /// <summary>
    /// Makes Arabic readable in Unity's legacy uGUI Text, which neither joins Arabic letters nor lays
    /// text out right-to-left. Fix() (1) replaces letters with their joined presentation forms
    /// (isolated / final / initial / medial, plus lam-alef ligatures) and (2) reorders the line into
    /// visual left-to-right order, keeping numbers and Latin words readable.
    /// Strings without Arabic letters are returned unchanged.
    /// </summary>
    public static class ArabicText
    {
        // code point -> (first presentation form, dual-joining?)
        static readonly Dictionary<char, (int form, bool dual)> Forms = new()
        {
            { 'ء', (0xFE80, false) }, { 'آ', (0xFE81, false) }, { 'أ', (0xFE83, false) },
            { 'ؤ', (0xFE85, false) }, { 'إ', (0xFE87, false) }, { 'ئ', (0xFE89, true) },
            { 'ا', (0xFE8D, false) }, { 'ب', (0xFE8F, true) }, { 'ة', (0xFE93, false) },
            { 'ت', (0xFE95, true) }, { 'ث', (0xFE99, true) }, { 'ج', (0xFE9D, true) },
            { 'ح', (0xFEA1, true) }, { 'خ', (0xFEA5, true) }, { 'د', (0xFEA9, false) },
            { 'ذ', (0xFEAB, false) }, { 'ر', (0xFEAD, false) }, { 'ز', (0xFEAF, false) },
            { 'س', (0xFEB1, true) }, { 'ش', (0xFEB5, true) }, { 'ص', (0xFEB9, true) },
            { 'ض', (0xFEBD, true) }, { 'ط', (0xFEC1, true) }, { 'ظ', (0xFEC5, true) },
            { 'ع', (0xFEC9, true) }, { 'غ', (0xFECD, true) }, { 'ف', (0xFED1, true) },
            { 'ق', (0xFED5, true) }, { 'ك', (0xFED9, true) }, { 'ل', (0xFEDD, true) },
            { 'م', (0xFEE1, true) }, { 'ن', (0xFEE5, true) }, { 'ه', (0xFEE9, true) },
            { 'و', (0xFEED, false) }, { 'ى', (0xFEEF, false) }, { 'ي', (0xFEF1, true) },
        };

        // lam + alef variant -> isolated ligature (final form is +1)
        static readonly Dictionary<char, int> LamAlef = new()
        {
            { 'آ', 0xFEF5 }, { 'أ', 0xFEF7 }, { 'إ', 0xFEF9 }, { 'ا', 0xFEFB },
        };

        const char Hamza = 'ء';
        const char Tatweel = 'ـ';

        static bool IsMark(char c) => (c >= 'ً' && c <= 'ٟ') || c == 'ٰ';
        static bool IsArabicLetter(char c) => Forms.ContainsKey(c) || c == Tatweel;
        static bool IsRtl(char c) => (c >= '؀' && c <= 'ۿ') || (c >= 'ﭐ' && c <= '﷿') || (c >= 'ﹰ' && c <= '﻿');
        static bool IsLtrStrong(char c) => char.IsLetter(c) && !IsRtl(c);
        static bool IsDigit(char c) => c >= '0' && c <= '9';

        static readonly Dictionary<string, string> Cache = new();

        public static string Fix(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            bool any = false;
            foreach (char c in s) if (IsRtl(c)) { any = true; break; }
            if (!any) return s;
            if (Cache.TryGetValue(s, out var cached)) return cached;

            // NotoSansArabic has no en dash, middle dot or Latin percent sign: swap in glyphs it has
            var lines = s.Replace('–', '-').Replace('—', '-').Replace('·', '-').Replace('%', '٪').Split('\n');
            for (int i = 0; i < lines.Length; i++) lines[i] = Reorder(Shape(lines[i]));
            var result = string.Join("\n", lines);
            if (Cache.Count > 512) Cache.Clear();
            Cache[s] = result;
            return result;
        }

        /// <summary>Joins letters (logical order in, logical order out). Diacritics are dropped.</summary>
        static string Shape(string s)
        {
            var src = new List<char>(s.Length);
            foreach (char c in s) if (!IsMark(c)) src.Add(c);

            var sb = new StringBuilder(src.Count);
            for (int i = 0; i < src.Count; i++)
            {
                char c = src[i];
                if (!IsArabicLetter(c)) { sb.Append(c); continue; }
                if (c == Tatweel) { sb.Append(c); continue; }

                char prev = i > 0 ? src[i - 1] : '\0';
                bool joinPrev = prev != '\0' && (prev == Tatweel || (Forms.TryGetValue(prev, out var pf) && pf.dual));

                // lam-alef ligature
                if (c == 'ل' && i + 1 < src.Count && LamAlef.TryGetValue(src[i + 1], out int lig))
                {
                    sb.Append((char)(lig + (joinPrev ? 1 : 0)));
                    i++;
                    continue;
                }

                var (form, dual) = Forms[c];
                char next = i + 1 < src.Count ? src[i + 1] : '\0';
                bool joinNext = dual && next != '\0' && IsArabicLetter(next) && next != Hamza;
                if (c == Hamza) { sb.Append((char)form); continue; }

                int offset;
                if (!dual) offset = joinPrev ? 1 : 0;                       // right-joining: isolated / final
                else if (joinPrev && joinNext) offset = 3;                   // medial
                else if (joinPrev) offset = 1;                               // final
                else if (joinNext) offset = 2;                               // initial
                else offset = 0;                                             // isolated
                sb.Append((char)(form + offset));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Converts one right-to-left line from logical to visual order: runs of numbers / Latin text
        /// keep their order, everything else is reversed, brackets are mirrored.
        /// </summary>
        static string Reorder(string s)
        {
            var segments = new List<string>();
            int i = 0;
            while (i < s.Length)
            {
                if (StartsLtrRun(s, i))
                {
                    int j = i + 1;
                    while (j < s.Length)
                    {
                        char c = s[j];
                        if (IsDigit(c) || IsLtrStrong(c)) { j++; continue; }
                        // keep separators inside a run when an LTR char follows: 0:37, 8/60, 1.5, "Farm World"
                        if ((c == ':' || c == '/' || c == '.' || c == ',' || c == ' ' || c == '-' || c == '\'') &&
                            j + 1 < s.Length && (IsDigit(s[j + 1]) || IsLtrStrong(s[j + 1])) &&
                            !(c == ' ' && IsDigit(s[j - 1]) != IsDigit(s[j + 1])))
                        { j++; continue; }
                        if ((c == '%' || c == '٪') && IsDigit(s[j - 1])) { j++; continue; }
                        break;
                    }
                    segments.Add(s.Substring(i, j - i));
                    i = j;
                }
                else
                {
                    segments.Add(Mirror(s[i]).ToString());
                    i++;
                }
            }
            segments.Reverse();
            return string.Concat(segments);
        }

        static bool StartsLtrRun(string s, int i)
        {
            char c = s[i];
            if (IsDigit(c) || IsLtrStrong(c)) return true;
            // a sign directly before a number belongs to it: "+3", "-5"
            return (c == '+' || c == '-') && i + 1 < s.Length && IsDigit(s[i + 1]);
        }

        static char Mirror(char c) => c switch
        {
            '(' => ')', ')' => '(', '[' => ']', ']' => '[', '{' => '}', '}' => '{', '<' => '>', '>' => '<',
            _ => c,
        };
    }
}

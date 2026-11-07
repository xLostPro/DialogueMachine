using System;
using System.Text;
using System.Text.RegularExpressions;

namespace DialogueMachine.Runtime.Unity
{
    public static class RichText
    {
        private static readonly Regex TagRegex = new Regex(@"\[(/?)(b|i|u|colour)(?:=([^\]]*))?\]", RegexOptions.IgnoreCase);

        /// <summary>
        /// Converts Dialogue Machine markup tags ([b], [i], [u], [colour=X]) into Unity rich text tags
        /// and escapes any pre-existing angle brackets so they are not parsed as tags.
        /// </summary>
        public static string ToUnity(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;

            // Escape existing angle brackets so Unity's rich text parser doesn't treat them as tags.
            var escaped = text.Replace("<", "<\u200B");

            return TagRegex.Replace(escaped, m =>
            {
                var slash = m.Groups[1].Value;
                var tag = m.Groups[2].Value.ToLowerInvariant();
                var val = m.Groups[3].Value;
                if (tag == "colour")
                {
                    return slash == "/" ? "</color>" : (string.IsNullOrEmpty(val) ? "<color>" : "<color=" + val + ">");
                }
                return "<" + slash + tag + ">";
            });
        }

        /// <summary>Number of characters a reader sees in converted text (tags and escape marks don't count).</summary>
        public static int VisibleLength(string unityText)
        {
            Reveal(unityText, int.MaxValue, out var count);
            return count;
        }

        /// <summary>
        /// The first <paramref name="visible"/> characters of converted text, keeping every tag before them,
        /// so a typewriter shows bold and colour while it types. Unity closes unclosed tags itself.
        /// </summary>
        public static string Reveal(string unityText, int visible) => Reveal(unityText, visible, out _);

        private static string Reveal(string text, int visible, out int count)
        {
            count = 0;
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sb = new StringBuilder(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '<' && i + 1 < text.Length && text[i + 1] != '​')
                {
                    var close = text.IndexOf('>', i + 1);
                    if (close > 0)
                    {
                        sb.Append(text, i, close - i + 1);
                        i = close;
                        continue;
                    }
                }
                if (c != '​')
                {
                    if (count == visible) break;
                    count++;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}

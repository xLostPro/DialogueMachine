using System;
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
    }
}

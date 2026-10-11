using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using VRage;
using VRage.Utils;

namespace HighwayEngineer.Common.Helpers
{
    /// <summary>
    /// Localized text from Content/Data/Localization/MyTexts*.resx (keys start with <see cref="Prefix"/>).
    /// <para>
    /// Text the server sends to players (rejections, statuses, chat replies) is built with <see cref="Message"/>:
    /// a key and its arguments, encoded into the packet's string so each client shows it in its own language
    /// with <see cref="Resolve"/>. Arguments may be other messages, numbers (formatted with the reader's culture)
    /// or plain text such as a player name or an exception message.
    /// </para>
    /// </summary>
    public static class LocHelper
    {
        public const string Prefix = "HighwayEngineer_";

        // Private-use characters: valid in XML (control characters are not, and the contract store
        // holding a message, such as a bridge's rebar item name, could not be saved) and never in game text.
        const char MessageStart = '\uE000';
        const char MessageEnd = '\uE001';
        const char Separator = '\uE002';
        const char NumberMark = '\uE003';

        public static string GetLoc(string key) => MyTexts.Get(MyStringId.GetOrCompute(key)).ToString();

        /// <summary>The key's text with <paramref name="args"/> formatted in, in this machine's language.</summary>
        public static string Format(string key, params object[] args) => SafeFormat(GetLoc(key), args);

        /// <summary>"a", "a and b" or "a, b and c", in this machine's language.</summary>
        public static string JoinAnd(IList<string> items)
        {
            if (items == null || items.Count == 0) return string.Empty;
            if (items.Count == 1) return items[0];
            var head = new string[items.Count - 1];
            for (int i = 0; i < head.Length; i++) head[i] = items[i];
            return Format("HighwayEngineer_Common_And", string.Join(GetLoc("HighwayEngineer_Common_ListSeparator"), head),
                items[items.Count - 1]);
        }

        /// <summary>Like <see cref="JoinAnd"/>, for messages (see <see cref="Message"/>) another machine resolves.</summary>
        public static string MessageJoinAnd(IList<string> items)
        {
            if (items == null || items.Count == 0) return string.Empty;
            string joined = items[items.Count - 1];
            if (items.Count == 1) return joined;
            string head = items[0];
            for (int i = 1; i < items.Count - 1; i++)
                head = Message("HighwayEngineer_Common_ListPair", head, items[i]);
            return Message("HighwayEngineer_Common_And", head, joined);
        }

        /// <summary>
        /// A key and its arguments for another machine to show in its language (see <see cref="Resolve"/>).
        /// Plain text passes through Resolve unchanged, so callers may mix both.
        /// </summary>
        public static string Message(string key, params object[] args)
        {
            var builder = new StringBuilder();
            builder.Append(MessageStart).Append(key);
            if (args != null)
                foreach (object arg in args)
                {
                    builder.Append(Separator);
                    AppendArgument(builder, arg);
                }
            return builder.Append(MessageEnd).ToString();
        }

        /// <summary>Text for display: every encoded message in <paramref name="text"/> is replaced by its localized text.</summary>
        public static string Resolve(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf(MessageStart) < 0)
                return text;
            int index = 0;
            return ReadText(text, ref index, false);
        }

        static void AppendArgument(StringBuilder builder, object arg)
        {
            if (arg == null)
                return;
            if (arg is int || arg is long || arg is float || arg is double || arg is decimal || arg is short ||
                arg is uint || arg is ulong || arg is ushort || arg is byte)
            {
                builder.Append(NumberMark)
                    .Append(Convert.ToDouble(arg, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            string value = arg.ToString();
            // Control characters of the encoding only appear as part of nested messages.
            if (value.IndexOf(Separator) >= 0 || value.IndexOf(MessageEnd) >= 0 || value.IndexOf(NumberMark) >= 0)
                if (value.IndexOf(MessageStart) < 0)
                    value = value.Replace(Separator, ' ').Replace(MessageEnd, ' ').Replace(NumberMark, ' ');
            builder.Append(value);
        }

        /// <summary>Plain text and messages up to the end, or (inside a message) up to the next separator or end.</summary>
        static string ReadText(string text, ref int index, bool inMessage)
        {
            var builder = new StringBuilder();
            while (index < text.Length)
            {
                char c = text[index];
                if (inMessage && (c == Separator || c == MessageEnd))
                    break;
                if (c == MessageStart)
                {
                    builder.Append(ReadMessage(text, ref index));
                    continue;
                }
                builder.Append(c);
                index++;
            }
            return builder.ToString();
        }

        static string ReadMessage(string text, ref int index)
        {
            index++; // MessageStart
            int keyStart = index;
            while (index < text.Length && text[index] != Separator && text[index] != MessageEnd)
                index++;
            string key = text.Substring(keyStart, index - keyStart);
            var args = new List<object>();
            while (index < text.Length && text[index] == Separator)
            {
                index++;
                if (index < text.Length && text[index] == NumberMark)
                {
                    int numberStart = ++index;
                    while (index < text.Length && text[index] != Separator && text[index] != MessageEnd)
                        index++;
                    double number;
                    string raw = text.Substring(numberStart, index - numberStart);
                    args.Add(double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
                        ? (object)number
                        : raw);
                }
                else
                    args.Add(ReadText(text, ref index, true));
            }
            if (index < text.Length && text[index] == MessageEnd)
                index++;
            return SafeFormat(GetLoc(key), args.ToArray());
        }

        static string SafeFormat(string format, object[] args)
        {
            if (args == null || args.Length == 0)
                return format;
            try
            {
                return string.Format(CultureInfo.CurrentCulture, format, args);
            }
            catch (FormatException)
            {
                return format + " (" + string.Join(", ", args) + ")";
            }
        }
    }
}

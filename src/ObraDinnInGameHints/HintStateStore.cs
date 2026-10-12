using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ObraDinnInGameHints
{
    internal sealed class JsonHintStateStore : IHintStateStore
    {
        private const string JsonString = @"""((?:\\[""\\/bfnrt]|\\u[0-9a-fA-F]{4}|[^""\\\x00-\x1f])*)""";
        private static readonly Regex EntryRegex = new Regex(
            @"\G\s*\{\s*""profile""\s*:\s*" + JsonString +
            @"\s*,\s*""face""\s*:\s*" + JsonString +
            @"\s*,\s*""branch""\s*:\s*([01])\s*,\s*""reflected""\s*:\s*(true|false)" +
            @"\s*,\s*""revealed""\s*:\s*(0|[1-9][0-9]*)\s*,\s*""final""\s*:\s*(true|false)\s*\}",
            RegexOptions.CultureInvariant);
        private static readonly Regex DocumentRegex = new Regex(
            @"\A\s*\{\s*""version""\s*:\s*1\s*,\s*""entries""\s*:\s*\[(.*)\]\s*\}\s*\z",
            RegexOptions.CultureInvariant | RegexOptions.Singleline);

        private readonly string path;
        private readonly Action<string> log;
        private readonly Dictionary<string, HintProgress> states = new Dictionary<string, HintProgress>(StringComparer.Ordinal);

        public JsonHintStateStore(string path, Action<string> log)
        {
            this.path = path;
            this.log = log;
            Load();
        }

        public HintProgress Get(string profileId, string faceId, HintBranch branch)
        {
            string key = MakeKey(profileId, faceId, branch);
            HintProgress value;
            return states.TryGetValue(key, out value) ? value.Clone() : new HintProgress();
        }

        public void Put(string profileId, string faceId, HintBranch branch, HintProgress progress)
        {
            string key = MakeKey(profileId, faceId, branch);
            HintProgress previous;
            bool existed = states.TryGetValue(key, out previous);
            states[key] = progress.Clone();
            try { Save(); }
            catch
            {
                if (existed) states[key] = previous;
                else states.Remove(key);
                throw;
            }
        }

        public void ResetProfile(string profileId)
        {
            string prefix = Normalize(profileId) + "\u001f";
            List<string> remove = new List<string>();
            foreach (string key in states.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                    remove.Add(key);
            }
            Dictionary<string, HintProgress> previous = new Dictionary<string, HintProgress>(states);
            for (int i = 0; i < remove.Count; i++)
                states.Remove(remove[i]);
            try { Save(); }
            catch
            {
                foreach (string key in remove) states[key] = previous[key];
                throw;
            }
        }

        private void Load()
        {
            if (!File.Exists(path))
                return;
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                Match document = DocumentRegex.Match(json);
                if (!document.Success)
                    throw new InvalidOperationException("Unsupported state schema.");

                string entries = document.Groups[1].Value.Trim();
                int offset = 0;
                while (offset < entries.Length)
                {
                    Match match = EntryRegex.Match(entries, offset);
                    if (!match.Success)
                        throw new InvalidOperationException("Invalid state entry.");
                    string profile = Unescape(match.Groups[1].Value);
                    string face = Unescape(match.Groups[2].Value);
                    HintBranch branch = match.Groups[3].Value == "0" ? HintBranch.Identity : HintBranch.Fate;
                    HintProgress progress = new HintProgress
                    {
                        Reflected = match.Groups[4].Value == "true",
                        Revealed = int.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture),
                        FinalRevealed = match.Groups[6].Value == "true"
                    };
                    states.Add(MakeKey(profile, face, branch), progress);
                    offset = match.Index + match.Length;
                    while (offset < entries.Length && char.IsWhiteSpace(entries[offset])) offset++;
                    if (offset < entries.Length)
                    {
                        if (entries[offset++] != ',' || offset == entries.Length)
                            throw new InvalidOperationException("Invalid state separator.");
                    }
                }
            }
            catch (Exception ex)
            {
                string backup = path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".json";
                try { File.Copy(path, backup, true); } catch { }
                states.Clear();
                if (log != null)
                    log("Hint state was corrupt and has been reset: " + ex.GetType().Name);
            }
        }

        private void Save()
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, Serialize(), new UTF8Encoding(false));

            if (File.Exists(path))
            {
                string backup = path + ".bak";
                // If atomic replacement fails, leave the original file intact.
                File.Replace(temporary, path, backup);
                return;
            }
            File.Move(temporary, path);
        }

        private string Serialize()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("{\"version\":1,\"entries\":[");
            bool first = true;
            List<string> keys = new List<string>(states.Keys);
            keys.Sort(StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
            {
                string[] parts = keys[i].Split('\u001f');
                HintProgress progress = states[keys[i]];
                if (!first)
                    builder.Append(',');
                first = false;
                builder.Append("{\"profile\":\"").Append(Escape(parts[0]))
                    .Append("\",\"face\":\"").Append(Escape(parts[1]))
                    .Append("\",\"branch\":").Append(parts[2])
                    .Append(",\"reflected\":").Append(progress.Reflected ? "true" : "false")
                    .Append(",\"revealed\":").Append(Math.Max(0, progress.Revealed).ToString(CultureInfo.InvariantCulture))
                    .Append(",\"final\":").Append(progress.FinalRevealed ? "true" : "false")
                    .Append('}');
            }
            builder.Append("]}");
            return builder.ToString();
        }

        private static string MakeKey(string profileId, string faceId, HintBranch branch)
        {
            return Normalize(profileId) + "\u001f" + Normalize(faceId) + "\u001f" + ((int)branch).ToString(CultureInfo.InvariantCulture);
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrEmpty(value) ? "default" : value.Replace("\u001f", "_");
        }

        private static string Escape(string value)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\\' || c == '"') builder.Append('\\').Append(c);
                else if (c < 32) builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else builder.Append(c);
            }
            return builder.ToString();
        }

        private static string Unescape(string value)
        {
            StringBuilder builder = new StringBuilder();
            bool escaping = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!escaping && c == '\\')
                {
                    escaping = true;
                    continue;
                }
                if (escaping)
                {
                    if (c == 'n') builder.Append('\n');
                    else if (c == 'r') builder.Append('\r');
                    else if (c == 't') builder.Append('\t');
                    else if (c == 'b') builder.Append('\b');
                    else if (c == 'f') builder.Append('\f');
                    else if (c == 'u')
                    {
                        builder.Append((char)int.Parse(value.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                    }
                    else builder.Append(c);
                    escaping = false;
                }
                else builder.Append(c);
            }
            return builder.ToString();
        }
    }
}

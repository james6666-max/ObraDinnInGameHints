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
        private static readonly Regex EntryRegex = new Regex(
            "\\{\\\"profile\\\":\\\"((?:\\\\.|[^\\\"])*)\\\",\\\"face\\\":\\\"((?:\\\\.|[^\\\"])*)\\\",\\\"branch\\\":([01]),\\\"reflected\\\":(true|false),\\\"revealed\\\":([0-9]+),\\\"final\\\":(true|false)\\}",
            RegexOptions.CultureInvariant);

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
            states[MakeKey(profileId, faceId, branch)] = progress.Clone();
            Save();
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
            for (int i = 0; i < remove.Count; i++)
                states.Remove(remove[i]);
            Save();
        }

        private void Load()
        {
            if (!File.Exists(path))
                return;
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                if (json.IndexOf("\"version\":1", StringComparison.Ordinal) < 0 ||
                    json.IndexOf("\"entries\":[", StringComparison.Ordinal) < 0)
                    throw new InvalidOperationException("Unsupported state schema.");

                MatchCollection matches = EntryRegex.Matches(json);
                for (int i = 0; i < matches.Count; i++)
                {
                    Match match = matches[i];
                    string profile = Unescape(match.Groups[1].Value);
                    string face = Unescape(match.Groups[2].Value);
                    HintBranch branch = match.Groups[3].Value == "0" ? HintBranch.Identity : HintBranch.Fate;
                    HintProgress progress = new HintProgress
                    {
                        Reflected = match.Groups[4].Value == "true",
                        Revealed = int.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture),
                        FinalRevealed = match.Groups[6].Value == "true"
                    };
                    states[MakeKey(profile, face, branch)] = progress;
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
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, Serialize(), new UTF8Encoding(false));

            if (File.Exists(path))
            {
                string backup = path + ".bak";
                try
                {
                    File.Replace(temporary, path, backup);
                    return;
                }
                catch
                {
                    File.Copy(path, backup, true);
                    File.Delete(path);
                }
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
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
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
                    else builder.Append(c);
                    escaping = false;
                }
                else builder.Append(c);
            }
            return builder.ToString();
        }
    }
}

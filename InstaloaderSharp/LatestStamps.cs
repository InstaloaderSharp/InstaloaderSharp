using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using InstaloaderSharp.Python;

namespace InstaloaderSharp
{
    /// <summary>Error of <see cref="ConfigParser"/> (configparser.Error).</summary>
    public class ConfigParserException : Exception
    {
        public ConfigParserException(string message) : base(message) { }
    }

    /// <summary>
    /// A minimal counterpart of Python's configparser.ConfigParser (with BasicInterpolation), reading and writing
    /// the same INI dialect.
    /// </summary>
    public sealed class ConfigParser
    {
        private const string DefaultSection = "DEFAULT";

        private readonly List<string> _sectionOrder = new List<string>();
        private readonly Dictionary<string, List<KeyValuePair<string, string>>> _sections =
            new Dictionary<string, List<KeyValuePair<string, string>>>(StringComparer.Ordinal);
        private readonly List<KeyValuePair<string, string>> _defaults = new List<KeyValuePair<string, string>>();

        private static readonly Regex SectionRegex = new Regex(@"^\[(?<header>.+)\]");
        private static readonly Regex OptionRegex = new Regex(@"^(?<option>.*?)\s*(?<vi>=|:)\s*(?<value>.*)$");

        /// <summary>Python's optionxform: lowercases option names.</summary>
        private static string OptionXform(string option)
        {
            return option.ToLowerInvariant();
        }

        /// <summary>ConfigParser.read(filename): silently ignores missing files.</summary>
        public void Read(string filename)
        {
            if (!File.Exists(filename))
                return;
            byte[] bytes = File.ReadAllBytes(filename);
            string text;
            try
            {
                text = new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                text = PyOs.LocaleEncoding.GetString(bytes);
            }
            if (text.Length > 0 && text[0] == (char)0xFEFF)
                text = text.Substring(1);
            ReadString(text);
        }

        public void ReadString(string text)
        {
            List<KeyValuePair<string, string>> current = null;
            int currentOptionIndex = -1;
            int currentIndent = 0;
            foreach (string rawLine in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                string stripped = rawLine.Trim();
                if (stripped.StartsWith("#") || stripped.StartsWith(";"))
                    continue;
                int indent = rawLine.Length - rawLine.TrimStart().Length;
                if (stripped.Length == 0)
                {
                    // empty line ends a multi-line value unless followed by indented continuation; keep simple
                    if (current != null && currentOptionIndex >= 0)
                    {
                        var kv = current[currentOptionIndex];
                        current[currentOptionIndex] = new KeyValuePair<string, string>(kv.Key, kv.Value + "\n");
                    }
                    continue;
                }
                if (current != null && currentOptionIndex >= 0 && indent > currentIndent)
                {
                    var kv = current[currentOptionIndex];
                    current[currentOptionIndex] = new KeyValuePair<string, string>(kv.Key, kv.Value + "\n" + stripped);
                    continue;
                }
                TrimTrailingNewlines(current, currentOptionIndex);
                currentIndent = indent;
                Match section = SectionRegex.Match(stripped);
                if (section.Success)
                {
                    string name = section.Groups["header"].Value;
                    if (name == DefaultSection)
                    {
                        current = _defaults;
                    }
                    else
                    {
                        if (!_sections.ContainsKey(name))
                        {
                            _sections[name] = new List<KeyValuePair<string, string>>();
                            _sectionOrder.Add(name);
                        }
                        current = _sections[name];
                    }
                    currentOptionIndex = -1;
                    continue;
                }
                if (current == null)
                    throw new ConfigParserException("File contains no section headers.");
                Match option = OptionRegex.Match(stripped);
                if (!option.Success || option.Groups["option"].Value.Trim().Length == 0)
                    throw new ConfigParserException("Source contains parsing errors: " + PyStr.Repr(rawLine));
                string key = OptionXform(option.Groups["option"].Value.TrimEnd());
                string value = option.Groups["value"].Value.Trim();
                int existing = current.FindIndex(kv => kv.Key == key);
                if (existing >= 0)
                {
                    current[existing] = new KeyValuePair<string, string>(key, value);
                    currentOptionIndex = existing;
                }
                else
                {
                    current.Add(new KeyValuePair<string, string>(key, value));
                    currentOptionIndex = current.Count - 1;
                }
            }
            TrimTrailingNewlines(current, currentOptionIndex);
        }

        private static void TrimTrailingNewlines(List<KeyValuePair<string, string>> section, int index)
        {
            if (section == null || index < 0)
                return;
            var kv = section[index];
            section[index] = new KeyValuePair<string, string>(kv.Key, kv.Value.TrimEnd('\n'));
        }

        public bool HasSection(string section)
        {
            return _sections.ContainsKey(section);
        }

        public void AddSection(string section)
        {
            if (section == DefaultSection)
                throw new PyValueError("Invalid section name: 'DEFAULT'");
            if (_sections.ContainsKey(section))
                throw new ConfigParserException(string.Format("Section {0} already exists", PyStr.Repr(section)));
            _sections[section] = new List<KeyValuePair<string, string>>();
            _sectionOrder.Add(section);
        }

        public void RemoveSection(string section)
        {
            if (_sections.Remove(section))
                _sectionOrder.Remove(section);
        }

        private string RawGet(string section, string option)
        {
            List<KeyValuePair<string, string>> items;
            if (!_sections.TryGetValue(section, out items))
            {
                if (section != DefaultSection)
                    throw new ConfigParserException(string.Format("No section: {0}", PyStr.Repr(section)));
                items = _defaults;
            }
            string key = OptionXform(option);
            foreach (var kv in items)
                if (kv.Key == key)
                    return kv.Value;
            foreach (var kv in _defaults)
                if (kv.Key == key)
                    return kv.Value;
            throw new ConfigParserException(string.Format("No option {0} in section: {1}", PyStr.Repr(key), PyStr.Repr(section)));
        }

        public bool HasOption(string section, string option)
        {
            try
            {
                RawGet(section, option);
                return true;
            }
            catch (ConfigParserException)
            {
                return false;
            }
        }

        /// <summary>ConfigParser.get with BasicInterpolation.</summary>
        public string Get(string section, string option)
        {
            return Interpolate(section, RawGet(section, option), 1);
        }

        private string Interpolate(string section, string value, int depth)
        {
            if (depth > 10)
                throw new ConfigParserException("Recursion limit exceeded in value substitution");
            var sb = new StringBuilder();
            int i = 0;
            while (i < value.Length)
            {
                char c = value[i];
                if (c != '%')
                {
                    sb.Append(c);
                    i++;
                    continue;
                }
                if (i + 1 < value.Length && value[i + 1] == '%')
                {
                    sb.Append('%');
                    i += 2;
                    continue;
                }
                if (i + 1 < value.Length && value[i + 1] == '(')
                {
                    int close = value.IndexOf(")s", i, StringComparison.Ordinal);
                    if (close < 0)
                        throw new ConfigParserException("bad interpolation variable reference " + PyStr.Repr(value));
                    string name = value.Substring(i + 2, close - i - 2);
                    sb.Append(Interpolate(section, RawGet(section, name), depth + 1));
                    i = close + 2;
                    continue;
                }
                throw new ConfigParserException("'%' must be followed by '%' or '(', found: " + PyStr.Repr(value.Substring(i)));
            }
            return sb.ToString();
        }

        /// <summary>ConfigParser.getint</summary>
        public long GetInt(string section, string option)
        {
            return PyInt.ParseLong(Get(section, option));
        }

        /// <summary>ConfigParser.set</summary>
        public void Set(string section, string option, string value)
        {
            if (value != null)
            {
                // BasicInterpolation.before_set validation
                string tmp = value.Replace("%%", "");
                tmp = Regex.Replace(tmp, @"%\([^)]*\)s", "");
                if (tmp.Contains("%"))
                    throw new PyValueError(string.Format("invalid interpolation syntax in {0} at position {1}",
                                                         PyStr.Repr(value), tmp.IndexOf('%')));
            }
            List<KeyValuePair<string, string>> items;
            if (section == DefaultSection)
                items = _defaults;
            else if (!_sections.TryGetValue(section, out items))
                throw new ConfigParserException(string.Format("No section: {0}", PyStr.Repr(section)));
            string key = OptionXform(option);
            int index = items.FindIndex(kv => kv.Key == key);
            if (index >= 0)
                items[index] = new KeyValuePair<string, string>(key, value);
            else
                items.Add(new KeyValuePair<string, string>(key, value));
        }

        /// <summary>ConfigParser.write: the text written to the file (with '\n' newlines).</summary>
        public string Write()
        {
            var sb = new StringBuilder();
            if (_defaults.Count > 0)
                WriteSection(sb, DefaultSection, _defaults);
            foreach (string section in _sectionOrder)
                WriteSection(sb, section, _sections[section]);
            return sb.ToString();
        }

        private static void WriteSection(StringBuilder sb, string name, List<KeyValuePair<string, string>> items)
        {
            sb.Append('[').Append(name).Append("]\n");
            foreach (var kv in items)
            {
                string value = kv.Value == null ? "" : " = " + kv.Value.Replace("\n", "\n\t");
                sb.Append(kv.Key).Append(value).Append('\n');
            }
            sb.Append('\n');
        }
    }

    /// <summary>
    /// LatestStamps class.
    ///
    /// Convenience class for retrieving and storing data from the --latest-stamps file.
    /// </summary>
    public class LatestStamps
    {
        public const string ProfileIdKey = "profile-id";
        public const string ProfilePicKey = "profile-pic";
        public const string PostTimestampKey = "post-timestamp";
        public const string TaggedTimestampKey = "tagged-timestamp";
        public const string IgtvTimestampKey = "igtv-timestamp";
        public const string ReelsTimestampKey = "reels-timestamp";
        public const string StoryTimestampKey = "story-timestamp";
        public const string IsoFormat = "%Y-%m-%dT%H:%M:%S.%f%z";

        private readonly string _file;
        private readonly ConfigParser _data;

        /// <param name="latestStampsFile">path to file.</param>
        public LatestStamps(string latestStampsFile)
        {
            _file = latestStampsFile;
            _data = new ConfigParser();
            _data.Read(latestStampsFile);
        }

        public string File { get { return _file; } }

        private void Save()
        {
            string dn = PyOs.DirName(_file);
            if (dn.Length > 0)
                PyOs.MakeDirs(dn);
            PyOs.WriteText(_file, _data.Write(), PyOs.LocaleEncoding);
        }

        private void EnsureSection(string section)
        {
            if (!_data.HasSection(section))
                _data.AddSection(section);
        }

        /// <summary>Returns stored ID of profile.</summary>
        public long? GetProfileId(string profileName)
        {
            try
            {
                return _data.GetInt(profileName, ProfileIdKey);
            }
            catch (Exception ex) when (ex is ConfigParserException || ex is PyValueError || ex is PyOverflowError)
            {
                return null;
            }
        }

        /// <summary>Stores ID of profile.</summary>
        public void SaveProfileId(string profileName, long profileId)
        {
            EnsureSection(profileName);
            _data.Set(profileName, ProfileIdKey, profileId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Save();
        }

        /// <summary>Renames a profile.</summary>
        public void RenameProfile(string oldProfile, string newProfile)
        {
            EnsureSection(newProfile);
            foreach (string option in new[] { ProfileIdKey, ProfilePicKey, PostTimestampKey, TaggedTimestampKey,
                                               IgtvTimestampKey, StoryTimestampKey })
            {
                if (_data.HasOption(oldProfile, option))
                {
                    string value = _data.Get(oldProfile, option);
                    _data.Set(newProfile, option, value);
                }
            }
            _data.RemoveSection(oldProfile);
            Save();
        }

        private DateTimeOffset GetTimestamp(string section, string key)
        {
            try
            {
                return PyDateTime.ParseIsoWithOffset(_data.Get(section, key));
            }
            catch (Exception ex) when (ex is ConfigParserException || ex is PyValueError)
            {
                return new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
            }
        }

        private void SetTimestamp(string section, string key, DateTimeOffset timestamp)
        {
            EnsureSection(section);
            _data.Set(section, key, PyDateTime.FormatIsoWithOffset(timestamp));
            Save();
        }

        /// <summary>Returns timestamp of last download of a profile's posts.</summary>
        public DateTimeOffset GetLastPostTimestamp(string profileName)
        {
            return GetTimestamp(profileName, PostTimestampKey);
        }

        /// <summary>Sets timestamp of last download of a profile's posts.</summary>
        public void SetLastPostTimestamp(string profileName, DateTimeOffset timestamp)
        {
            SetTimestamp(profileName, PostTimestampKey, timestamp);
        }

        /// <summary>Returns timestamp of last download of a profile's tagged posts.</summary>
        public DateTimeOffset GetLastTaggedTimestamp(string profileName)
        {
            return GetTimestamp(profileName, TaggedTimestampKey);
        }

        /// <summary>Sets timestamp of last download of a profile's tagged posts.</summary>
        public void SetLastTaggedTimestamp(string profileName, DateTimeOffset timestamp)
        {
            SetTimestamp(profileName, TaggedTimestampKey, timestamp);
        }

        /// <summary>Returns timestamp of last download of a profile's reels posts.</summary>
        public DateTimeOffset GetLastReelsTimestamp(string profileName)
        {
            return GetTimestamp(profileName, ReelsTimestampKey);
        }

        /// <summary>Sets timestamp of last download of a profile's reels posts.</summary>
        public void SetLastReelsTimestamp(string profileName, DateTimeOffset timestamp)
        {
            SetTimestamp(profileName, ReelsTimestampKey, timestamp);
        }

        /// <summary>Returns timestamp of last download of a profile's igtv posts.</summary>
        public DateTimeOffset GetLastIgtvTimestamp(string profileName)
        {
            return GetTimestamp(profileName, IgtvTimestampKey);
        }

        /// <summary>Sets timestamp of last download of a profile's igtv posts.</summary>
        public void SetLastIgtvTimestamp(string profileName, DateTimeOffset timestamp)
        {
            SetTimestamp(profileName, IgtvTimestampKey, timestamp);
        }

        /// <summary>Returns timestamp of last download of a profile's stories.</summary>
        public DateTimeOffset GetLastStoryTimestamp(string profileName)
        {
            return GetTimestamp(profileName, StoryTimestampKey);
        }

        /// <summary>Sets timestamp of last download of a profile's stories.</summary>
        public void SetLastStoryTimestamp(string profileName, DateTimeOffset timestamp)
        {
            SetTimestamp(profileName, StoryTimestampKey, timestamp);
        }

        /// <summary>Returns filename of profile's last downloaded profile pic.</summary>
        public string GetProfilePic(string profileName)
        {
            try
            {
                return _data.Get(profileName, ProfilePicKey);
            }
            catch (ConfigParserException)
            {
                return "";
            }
        }

        /// <summary>Sets filename of profile's last downloaded profile pic.</summary>
        public void SetProfilePic(string profileName, string profilePic)
        {
            EnsureSection(profileName);
            _data.Set(profileName, ProfilePicKey, profilePic);
            Save();
        }
    }
}

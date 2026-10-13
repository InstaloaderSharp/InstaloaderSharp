using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace InstaloaderSharp.Python
{
    /// <summary>Python's os and os.path functions (with the semantics of ntpath on Windows).</summary>
    public static class PyOs
    {
        public static readonly bool IsWindows = Environment.OSVersion.Platform == PlatformID.Win32NT ||
                                                Environment.OSVersion.Platform == PlatformID.Win32Windows;

        /// <summary>os.linesep</summary>
        public static string LineSep { get { return IsWindows ? "\r\n" : "\n"; } }

        private static bool IsSep(char c)
        {
            return c == '/' || (IsWindows && c == '\\');
        }

        private static string Sep { get { return IsWindows ? "\\" : "/"; } }

        /// <summary>os.path.splitdrive</summary>
        public static void SplitDrive(string p, out string drive, out string path)
        {
            if (IsWindows && p.Length >= 2)
            {
                string normp = p.Replace('/', '\\');
                if (normp.StartsWith("\\\\") && (normp.Length < 3 || normp[2] != '\\'))
                {
                    // UNC path: \\server\share
                    int index = normp.IndexOf('\\', 2);
                    if (index == -1)
                    {
                        drive = "";
                        path = p;
                        return;
                    }
                    int index2 = normp.IndexOf('\\', index + 1);
                    if (index2 == index + 1)
                    {
                        drive = "";
                        path = p;
                        return;
                    }
                    if (index2 == -1)
                        index2 = p.Length;
                    drive = p.Substring(0, index2);
                    path = p.Substring(index2);
                    return;
                }
                if (normp[1] == ':')
                {
                    drive = p.Substring(0, 2);
                    path = p.Substring(2);
                    return;
                }
            }
            drive = "";
            path = p;
        }

        /// <summary>os.path.isabs</summary>
        public static bool IsAbs(string p)
        {
            string drive, rest;
            SplitDrive(p, out drive, out rest);
            return rest.Length > 0 && IsSep(rest[0]);
        }

        /// <summary>os.path.join (ntpath.join on Windows).</summary>
        public static string Join(string a, params string[] p)
        {
            if (!IsWindows)
            {
                string path = a;
                foreach (string b in p)
                {
                    if (b.StartsWith("/"))
                        path = b;
                    else if (path.Length == 0 || path.EndsWith("/"))
                        path += b;
                    else
                        path += "/" + b;
                }
                return path;
            }
            string resultDrive, resultPath;
            SplitDrive(a, out resultDrive, out resultPath);
            foreach (string part in p)
            {
                string pDrive, pPath;
                SplitDrive(part, out pDrive, out pPath);
                if (pPath.Length > 0 && IsSep(pPath[0]))
                {
                    // Second path is absolute
                    if (pDrive.Length > 0 || resultDrive.Length == 0)
                        resultDrive = pDrive;
                    resultPath = pPath;
                    continue;
                }
                if (pDrive.Length > 0 && !string.Equals(pDrive, resultDrive, StringComparison.OrdinalIgnoreCase))
                {
                    // Different drives => ignore the first path entirely
                    resultDrive = pDrive;
                    resultPath = pPath;
                    continue;
                }
                if (pDrive.Length > 0)
                    resultDrive = pDrive;
                // Second path is relative to the first
                if (resultPath.Length > 0 && !IsSep(resultPath[resultPath.Length - 1]))
                    resultPath = resultPath + "\\";
                resultPath = resultPath + pPath;
            }
            // add separator between UNC and non-absolute path
            if (resultPath.Length > 0 && !IsSep(resultPath[0]) && resultDrive.Length > 0 &&
                resultDrive[resultDrive.Length - 1] != ':')
                return resultDrive + "\\" + resultPath;
            return resultDrive + resultPath;
        }

        /// <summary>os.path.split</summary>
        public static void Split(string p, out string head, out string tail)
        {
            string d, rest;
            SplitDrive(p, out d, out rest);
            int i = rest.Length;
            while (i > 0 && !IsSep(rest[i - 1]))
                i--;
            head = rest.Substring(0, i);
            tail = rest.Substring(i);
            string trimmed = head.TrimEnd(IsWindows ? new[] { '/', '\\' } : new[] { '/' });
            head = d + (trimmed.Length > 0 ? trimmed : head);
        }

        /// <summary>os.path.dirname</summary>
        public static string DirName(string p)
        {
            string head, tail;
            Split(p, out head, out tail);
            return head;
        }

        /// <summary>os.path.basename</summary>
        public static string BaseName(string p)
        {
            string head, tail;
            Split(p, out head, out tail);
            return tail;
        }

        /// <summary>os.path.splitext: returns (root, ext).</summary>
        public static void SplitExt(string p, out string root, out string ext)
        {
            int sepIndex = -1;
            for (int i = p.Length - 1; i >= 0; i--)
            {
                if (IsSep(p[i]))
                {
                    sepIndex = i;
                    break;
                }
            }
            int dotIndex = p.LastIndexOf('.');
            if (dotIndex > sepIndex)
            {
                // skip all leading dots
                int filenameIndex = sepIndex + 1;
                while (filenameIndex < dotIndex)
                {
                    if (p[filenameIndex] != '.')
                    {
                        root = p.Substring(0, dotIndex);
                        ext = p.Substring(dotIndex);
                        return;
                    }
                    filenameIndex++;
                }
            }
            root = p;
            ext = "";
        }

        /// <summary>os.path.isfile</summary>
        public static bool IsFile(string p)
        {
            try
            {
                return File.Exists(p);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>os.path.exists</summary>
        public static bool Exists(string p)
        {
            return File.Exists(p) || Directory.Exists(p);
        }

        /// <summary>os.makedirs(name, exist_ok=True). An empty name (the current directory) is accepted.</summary>
        public static void MakeDirs(string name)
        {
            if (string.IsNullOrEmpty(name))
                return;
            Directory.CreateDirectory(name);
        }

        /// <summary>os.replace: renames, replacing the destination if it exists.</summary>
        public static void Replace(string src, string dst)
        {
            if (File.Exists(dst))
            {
                if (!MoveFileEx(src, dst, 0x1 /* MOVEFILE_REPLACE_EXISTING */))
                {
                    File.Delete(dst);
                    File.Move(src, dst);
                }
            }
            else
            {
                File.Move(src, dst);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MoveFileEx(string lpExistingFileName, string lpNewFileName, int dwFlags);

        /// <summary>os.rename</summary>
        public static void Rename(string src, string dst)
        {
            if (Directory.Exists(src))
                Directory.Move(src, dst);
            else
                File.Move(src, dst);
        }

        /// <summary>os.utime(path, (atime, mtime)) with mtime given as instant.</summary>
        public static void UTime(string path, DateTime accessTimeUtc, DateTime modifiedTimeUtc)
        {
            File.SetLastAccessTimeUtc(path, accessTimeUtc);
            File.SetLastWriteTimeUtc(path, modifiedTimeUtc);
        }

        /// <summary>
        /// Writes a text file like Python's open(filename, 'w', encoding=...): '\n' is translated to os.linesep.
        /// </summary>
        public static void WriteText(string path, string text, Encoding encoding)
        {
            string translated = IsWindows ? text.Replace("\n", "\r\n") : text;
            File.WriteAllBytes(path, encoding.GetBytes(translated));
        }

        /// <summary>Reads a text file like Python's open(filename): universal newlines.</summary>
        public static string ReadText(string path, Encoding encoding)
        {
            string text = encoding.GetString(File.ReadAllBytes(path));
            if (text.Length > 0 && text[0] == (char)0xFEFF)
                text = text.Substring(1);
            return text.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        /// <summary>The encoding Python uses for open() without encoding argument (locale encoding).</summary>
        public static Encoding LocaleEncoding
        {
            get
            {
                try
                {
                    return Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
                }
                catch (Exception)
                {
                    return Encoding.Default;
                }
            }
        }
    }

    /// <summary>Python's textwrap.wrap and textwrap.fill with default settings.</summary>
    public static class TextWrap
    {
        // textwrap.TextWrapper.wordsep_re
        private const string Ws = @"[\t\n\x0b\x0c\r ]";
        private const string Wp = @"[\w!""'&.,?]";
        private const string Lt = @"[^\d\W]";
        private static readonly System.Text.RegularExpressions.Regex WordSep = new System.Text.RegularExpressions.Regex(
            "(" + Ws + "+" +
            "|(?<=" + Wp + ")-{2,}(?=\\w)" +
            "|[^\\t\\n\\x0b\\x0c\\r ]+?(?:" +
                "-(?:(?<=" + Lt + "{2}-)|(?<=" + Lt + "-" + Lt + "-))(?=" + Lt + "-?" + Lt + ")" +
                "|(?=" + Ws + "|\\z)" +
                "|(?<=" + Wp + ")(?=-{2,}\\w)" +
            "))");

        private static List<string> Split(string text)
        {
            var chunks = new List<string>();
            int pos = 0;
            foreach (System.Text.RegularExpressions.Match m in WordSep.Matches(text))
            {
                if (m.Index > pos)
                    chunks.Add(text.Substring(pos, m.Index - pos));
                if (m.Length > 0)
                    chunks.Add(m.Value);
                pos = m.Index + m.Length;
            }
            if (pos < text.Length)
                chunks.Add(text.Substring(pos));
            return chunks;
        }

        /// <summary>textwrap.wrap(text, width)</summary>
        public static List<string> Wrap(string text, int width = 70)
        {
            // expand_tabs and replace_whitespace
            var sb = new StringBuilder();
            foreach (char c in text.Replace("\t", "        "))
                sb.Append(c == '\n' || c == '\x0b' || c == '\x0c' || c == '\r' ? ' ' : c);
            List<string> chunks = Split(sb.ToString());
            chunks.Reverse();
            var lines = new List<string>();
            while (chunks.Count > 0)
            {
                var curLine = new List<string>();
                int curLen = 0;
                if (chunks[chunks.Count - 1].Trim().Length == 0 && lines.Count > 0)
                    chunks.RemoveAt(chunks.Count - 1);
                while (chunks.Count > 0)
                {
                    int l = chunks[chunks.Count - 1].Length;
                    if (curLen + l <= width)
                    {
                        curLine.Add(chunks[chunks.Count - 1]);
                        chunks.RemoveAt(chunks.Count - 1);
                        curLen += l;
                    }
                    else
                    {
                        break;
                    }
                }
                if (chunks.Count > 0 && chunks[chunks.Count - 1].Length > width)
                {
                    // _handle_long_word with break_long_words and break_on_hyphens
                    string chunk = chunks[chunks.Count - 1];
                    int spaceLeft = Math.Max(width - curLen, 1);
                    int end = spaceLeft;
                    if (chunk.Length > spaceLeft)
                    {
                        int hyphen = chunk.LastIndexOf('-', spaceLeft - 1, spaceLeft);
                        if (hyphen > 0 && chunk.Substring(0, hyphen).Any(ch => ch != '-'))
                            end = hyphen + 1;
                    }
                    curLine.Add(chunk.Substring(0, end));
                    chunks[chunks.Count - 1] = chunk.Substring(end);
                }
                if (curLine.Count > 0 && curLine[curLine.Count - 1].Trim().Length == 0)
                    curLine.RemoveAt(curLine.Count - 1);
                if (curLine.Count > 0)
                    lines.Add(string.Concat(curLine));
            }
            return lines;
        }

        /// <summary>textwrap.fill(text, width)</summary>
        public static string Fill(string text, int width = 70)
        {
            return string.Join("\n", Wrap(text, width));
        }
    }
}

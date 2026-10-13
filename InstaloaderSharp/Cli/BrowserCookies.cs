using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using InstaloaderSharp.Json;

namespace InstaloaderSharp.Cli
{
    /// <summary>A cookie read from a browser's cookie store.</summary>
    public sealed class BrowserCookie
    {
        public string Name;
        public string Value;
        public string Domain;
        public string Path;
    }

    /// <summary>
    /// Reads cookies from the cookie databases of installed browsers, like the browser_cookie3 library that
    /// Instaloader uses for --load-cookies.
    /// </summary>
    public static class BrowserCookies
    {
        private static string Env(string name)
        {
            return Environment.GetEnvironmentVariable(name) ?? "";
        }

        /// <summary>Loads all cookies of the given browser (or from the given cookie database file).</summary>
        public static List<BrowserCookie> Load(string browser, string cookieFile)
        {
            switch (browser)
            {
                case "chrome":
                    return Chromium(Path.Combine(Env("LOCALAPPDATA"), @"Google\Chrome\User Data"), true, cookieFile, "Chrome");
                case "chromium":
                    return Chromium(Path.Combine(Env("LOCALAPPDATA"), @"Chromium\User Data"), true, cookieFile, "Chromium");
                case "edge":
                    return Chromium(Path.Combine(Env("LOCALAPPDATA"), @"Microsoft\Edge\User Data"), true, cookieFile, "Edge");
                case "brave":
                    return Chromium(Path.Combine(Env("LOCALAPPDATA"), @"BraveSoftware\Brave-Browser\User Data"), true, cookieFile, "Brave");
                case "vivaldi":
                    return Chromium(Path.Combine(Env("LOCALAPPDATA"), @"Vivaldi\User Data"), true, cookieFile, "Vivaldi");
                case "opera":
                    return Chromium(Path.Combine(Env("APPDATA"), @"Opera Software\Opera Stable"), false, cookieFile, "Opera");
                case "opera_gx":
                    return Chromium(Path.Combine(Env("APPDATA"), @"Opera Software\Opera GX Stable"), false, cookieFile, "Opera GX");
                case "firefox":
                    return Firefox(Path.Combine(Env("APPDATA"), @"Mozilla\Firefox"), cookieFile, "Firefox");
                case "librewolf":
                    return Firefox(Path.Combine(Env("APPDATA"), "librewolf"), cookieFile, "LibreWolf");
                case "safari":
                    throw new LoginException("Safari cookies can only be loaded on macOS.");
            }
            throw new InvalidArgumentException("Unsupported browser " + browser);
        }

        #region Chromium

        private static string FindChromiumCookieFile(string userData, bool hasProfiles, string name)
        {
            var candidates = new List<string>();
            var profileDirs = new List<string>();
            if (hasProfiles)
            {
                profileDirs.Add(Path.Combine(userData, "Default"));
                if (Directory.Exists(userData))
                    profileDirs.AddRange(Directory.GetDirectories(userData, "Profile *").OrderBy(d => d, StringComparer.Ordinal));
            }
            else
            {
                profileDirs.Add(userData);
            }
            foreach (string dir in profileDirs)
            {
                foreach (string file in new[] { Path.Combine(dir, @"Network\Cookies"), Path.Combine(dir, "Cookies") })
                {
                    if (File.Exists(file))
                    {
                        candidates.Add(file);
                        break;
                    }
                }
                if (candidates.Count > 0)
                    break;
            }
            if (candidates.Count == 0)
                throw new LoginException(string.Format("Failed to find {0} cookie", name));
            return candidates[0];
        }

        private static byte[] GetChromiumKey(string userData)
        {
            string localState = Path.Combine(userData, "Local State");
            if (!File.Exists(localState))
                return null;
            string json;
            using (var fs = new FileStream(localState, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(fs, Encoding.UTF8))
                json = reader.ReadToEnd();
            JToken state = PyJson.Loads(json);
            JToken encryptedKey = state.Get("os_crypt") == null ? null : state["os_crypt"].Get("encrypted_key");
            if (encryptedKey.IsNone())
                return null;
            byte[] key = Convert.FromBase64String((string)encryptedKey);
            if (key.Length < 5 || Encoding.ASCII.GetString(key, 0, 5) != "DPAPI")
                return null;
            return ProtectedData.Unprotect(key.Skip(5).ToArray(), null, DataProtectionScope.CurrentUser);
        }

        private static List<BrowserCookie> Chromium(string userData, bool hasProfiles, string cookieFile, string name)
        {
            string file = cookieFile ?? FindChromiumCookieFile(userData, hasProfiles, name);
            SqliteReader db;
            try
            {
                db = new SqliteReader(file);
            }
            catch (IOException ex)
            {
                throw new LoginException(string.Format(
                    "Unable to read the {0} cookie database {1}: {2} Close {0} and try again.", name, file, ex.Message));
            }
            int metaVersion = 0;
            try
            {
                foreach (var row in db.ReadTable("meta"))
                {
                    if ("version".Equals(row["key"] as string))
                    {
                        int v;
                        if (int.TryParse(Convert.ToString(row["value"]), out v))
                            metaVersion = v;
                    }
                }
            }
            catch (InvalidDataException)
            {
            }
            byte[] key = null;
            bool keyLoaded = false;
            bool appBoundSkipped = false;
            var cookies = new List<BrowserCookie>();
            foreach (var row in db.ReadTable("cookies"))
            {
                string value = row.ContainsKey("value") ? row["value"] as string : null;
                byte[] encrypted = row.ContainsKey("encrypted_value") ? row["encrypted_value"] as byte[] : null;
                if (string.IsNullOrEmpty(value) && encrypted != null && encrypted.Length > 0)
                {
                    if (!keyLoaded)
                    {
                        key = GetChromiumKey(userData);
                        keyLoaded = true;
                    }
                    byte[] decrypted = DecryptChromium(encrypted, key, ref appBoundSkipped);
                    if (decrypted == null)
                        continue;
                    if (metaVersion >= 24 && decrypted.Length >= 32)
                        decrypted = decrypted.Skip(32).ToArray();
                    value = Encoding.UTF8.GetString(decrypted);
                }
                cookies.Add(new BrowserCookie
                {
                    Name = row["name"] as string ?? "",
                    Value = value ?? "",
                    Domain = row["host_key"] as string ?? "",
                    Path = row.ContainsKey("path") ? row["path"] as string ?? "/" : "/"
                });
            }
            if (appBoundSkipped)
                Console.Error.WriteLine("Warning: some {0} cookies use app-bound encryption (v20) and cannot be decrypted.", name);
            return cookies;
        }

        private static byte[] DecryptChromium(byte[] encrypted, byte[] key, ref bool appBoundSkipped)
        {
            if (encrypted.Length >= 3 && encrypted[0] == 'v' && (encrypted[1] == '1' || encrypted[1] == '2'))
            {
                if (encrypted[1] == '2')
                {
                    appBoundSkipped = true;
                    return null;
                }
                if (key == null || encrypted.Length < 3 + 12 + 16)
                    return null;
                byte[] nonce = encrypted.Skip(3).Take(12).ToArray();
                byte[] ciphertext = encrypted.Skip(15).Take(encrypted.Length - 15 - 16).ToArray();
                byte[] tag = encrypted.Skip(encrypted.Length - 16).ToArray();
                return AesGcm.Decrypt(key, nonce, ciphertext, tag);
            }
            try
            {
                return ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            }
            catch (CryptographicException)
            {
                return null;
            }
        }

        #endregion

        #region Firefox

        private static string FindFirefoxProfile(string baseDir, string name)
        {
            string ini = Path.Combine(baseDir, "profiles.ini");
            if (File.Exists(ini))
            {
                string section = null;
                var sections = new List<Dictionary<string, string>>();
                Dictionary<string, string> current = null;
                foreach (string raw in File.ReadAllLines(ini))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        section = line.Substring(1, line.Length - 2);
                        current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "__section", section } };
                        sections.Add(current);
                        continue;
                    }
                    int eq = line.IndexOf('=');
                    if (current != null && eq > 0)
                        current[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
                Func<string, bool, string> resolve = (p, relative) =>
                    relative ? Path.Combine(baseDir, p.Replace('/', '\\')) : p;
                foreach (var s in sections)
                {
                    string def;
                    if (s["__section"].StartsWith("Install", StringComparison.OrdinalIgnoreCase) && s.TryGetValue("Default", out def))
                    {
                        string path = resolve(def, !Path.IsPathRooted(def));
                        if (File.Exists(Path.Combine(path, "cookies.sqlite")))
                            return path;
                    }
                }
                foreach (var s in sections)
                {
                    string def, p, rel;
                    if (s.TryGetValue("Default", out def) && def == "1" && s.TryGetValue("Path", out p))
                    {
                        bool relative = !s.TryGetValue("IsRelative", out rel) || rel == "1";
                        string path = resolve(p, relative);
                        if (File.Exists(Path.Combine(path, "cookies.sqlite")))
                            return path;
                    }
                }
            }
            string profiles = Path.Combine(baseDir, "Profiles");
            if (Directory.Exists(profiles))
            {
                string newest = Directory.GetDirectories(profiles)
                                         .Where(d => File.Exists(Path.Combine(d, "cookies.sqlite")))
                                         .OrderByDescending(d => File.GetLastWriteTimeUtc(Path.Combine(d, "cookies.sqlite")))
                                         .FirstOrDefault();
                if (newest != null)
                    return newest;
            }
            throw new LoginException(string.Format("Failed to find {0} profile", name));
        }

        private static List<BrowserCookie> Firefox(string baseDir, string cookieFile, string name)
        {
            string file = cookieFile ?? Path.Combine(FindFirefoxProfile(baseDir, name), "cookies.sqlite");
            SqliteReader db;
            try
            {
                db = new SqliteReader(file);
            }
            catch (IOException ex)
            {
                throw new LoginException(string.Format("Unable to read the {0} cookie database {1}: {2}", name, file, ex.Message));
            }
            var cookies = new List<BrowserCookie>();
            foreach (var row in db.ReadTable("moz_cookies"))
            {
                cookies.Add(new BrowserCookie
                {
                    Name = row["name"] as string ?? "",
                    Value = row["value"] as string ?? "",
                    Domain = row["host"] as string ?? "",
                    Path = row.ContainsKey("path") ? row["path"] as string ?? "/" : "/"
                });
            }
            return cookies;
        }

        #endregion
    }

    /// <summary>AES-GCM decryption (not available in .NET Framework), built on AES-ECB.</summary>
    internal static class AesGcm
    {
        private static byte[] EncryptBlock(ICryptoTransform aes, byte[] block)
        {
            var output = new byte[16];
            aes.TransformBlock(block, 0, 16, output, 0);
            return output;
        }

        private static void Xor(byte[] a, byte[] b)
        {
            for (int i = 0; i < 16; i++)
                a[i] ^= b[i];
        }

        /// <summary>Multiplication in GF(2^128) as specified for GHASH.</summary>
        private static byte[] GfMult(byte[] x, byte[] y)
        {
            var z = new byte[16];
            var v = (byte[])y.Clone();
            for (int i = 0; i < 128; i++)
            {
                if ((x[i / 8] & (0x80 >> (i % 8))) != 0)
                    Xor(z, v);
                bool lsb = (v[15] & 1) != 0;
                for (int j = 15; j > 0; j--)
                    v[j] = (byte)((v[j] >> 1) | (v[j - 1] << 7));
                v[0] >>= 1;
                if (lsb)
                    v[0] ^= 0xE1;
            }
            return z;
        }

        private static void Increment(byte[] counter)
        {
            for (int i = 15; i >= 12; i--)
            {
                if (++counter[i] != 0)
                    break;
            }
        }

        /// <summary>Decrypts and authenticates; returns null if the tag does not match.</summary>
        public static byte[] Decrypt(byte[] key, byte[] nonce, byte[] ciphertext, byte[] tag)
        {
            using (var aes = new AesManaged { Mode = CipherMode.ECB, Padding = PaddingMode.None, Key = key })
            using (ICryptoTransform enc = aes.CreateEncryptor())
            {
                byte[] h = EncryptBlock(enc, new byte[16]);
                var j0 = new byte[16];
                Buffer.BlockCopy(nonce, 0, j0, 0, 12);
                j0[15] = 1;

                // GHASH over the ciphertext (no additional authenticated data)
                var s = new byte[16];
                for (int i = 0; i < ciphertext.Length; i += 16)
                {
                    var block = new byte[16];
                    Buffer.BlockCopy(ciphertext, i, block, 0, Math.Min(16, ciphertext.Length - i));
                    Xor(s, block);
                    s = GfMult(s, h);
                }
                var lengths = new byte[16];
                ulong bits = (ulong)ciphertext.Length * 8;
                for (int i = 0; i < 8; i++)
                    lengths[15 - i] = (byte)(bits >> (8 * i));
                Xor(s, lengths);
                s = GfMult(s, h);
                byte[] expectedTag = EncryptBlock(enc, j0);
                Xor(expectedTag, s);
                if (!expectedTag.SequenceEqual(tag))
                    return null;

                var plaintext = new byte[ciphertext.Length];
                var counter = (byte[])j0.Clone();
                for (int i = 0; i < ciphertext.Length; i += 16)
                {
                    Increment(counter);
                    byte[] keystream = EncryptBlock(enc, counter);
                    for (int j = 0; j < 16 && i + j < ciphertext.Length; j++)
                        plaintext[i + j] = (byte)(ciphertext[i + j] ^ keystream[j]);
                }
                return plaintext;
            }
        }
    }
}

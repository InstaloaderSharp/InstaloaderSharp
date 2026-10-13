using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using InstaloaderSharp.Python;

namespace InstaloaderSharp.Http
{
    /// <summary>An HTTP cookie, like http.cookiejar.Cookie.</summary>
    public sealed class Cookie
    {
        public string Name { get; set; }
        public string Value { get; set; }

        /// <summary>
        /// Domain: "" matches every host (cookies created by name only, like requests' create_cookie),
        /// ".example.com" matches example.com and its subdomains, "www.example.com" matches that host.
        /// </summary>
        public string Domain { get; set; }

        public bool DomainSpecified { get; set; }
        public string Path { get; set; }
        public bool Secure { get; set; }

        /// <summary>Expiry (UTC), or null for session cookies.</summary>
        public DateTime? Expires { get; set; }

        public Cookie(string name, string value, string domain = "", string path = "/")
        {
            Name = name;
            Value = value;
            Domain = domain ?? "";
            Path = path ?? "/";
        }

        public bool IsExpired(DateTime nowUtc)
        {
            return Expires.HasValue && Expires.Value <= nowUtc;
        }

        public override string ToString()
        {
            return string.Format("<Cookie {0}={1} for {2}{3}>", Name, Value, Domain, Path);
        }
    }

    /// <summary>
    /// Cookie jar with the semantics of requests' RequestsCookieJar (on top of http.cookiejar.CookieJar),
    /// including its iteration order (sorted by domain, path and name).
    /// </summary>
    public sealed class CookieJar : IEnumerable<Cookie>
    {
        private readonly List<Cookie> _cookies = new List<Cookie>();

        private static int Compare(Cookie a, Cookie b)
        {
            int c = string.CompareOrdinal(a.Domain, b.Domain);
            if (c != 0) return c;
            c = string.CompareOrdinal(a.Path, b.Path);
            if (c != 0) return c;
            return string.CompareOrdinal(a.Name, b.Name);
        }

        /// <summary>Stores a cookie, replacing one with the same domain, path and name.</summary>
        public void SetCookie(Cookie cookie)
        {
            for (int i = 0; i < _cookies.Count; i++)
            {
                if (Compare(_cookies[i], cookie) == 0)
                {
                    _cookies[i] = cookie;
                    return;
                }
            }
            _cookies.Add(cookie);
            _cookies.Sort(Compare);
        }

        /// <summary>RequestsCookieJar.set(name, value): None removes all cookies with that name.</summary>
        public void Set(string name, string value, string domain = "", string path = "/")
        {
            if (value == null)
            {
                Remove(name);
                return;
            }
            SetCookie(new Cookie(name, value, domain, path));
        }

        /// <summary>RequestsCookieJar.update(dict).</summary>
        public void Update(IEnumerable<KeyValuePair<string, string>> cookies)
        {
            foreach (var kv in cookies)
                Set(kv.Key, kv.Value);
        }

        /// <summary>Copies all cookies of another jar into this one.</summary>
        public void Update(CookieJar other)
        {
            foreach (Cookie c in other)
                SetCookie(new Cookie(c.Name, c.Value, c.Domain, c.Path)
                {
                    DomainSpecified = c.DomainSpecified,
                    Secure = c.Secure,
                    Expires = c.Expires
                });
        }

        /// <summary>remove_cookie_by_name: removes all cookies with the given name.</summary>
        public void Remove(string name)
        {
            _cookies.RemoveAll(c => c.Name == name);
        }

        /// <summary>Removes the cookie with given domain, path and name, if present.</summary>
        public void Clear(string domain, string path, string name)
        {
            _cookies.RemoveAll(c => c.Domain == domain && c.Path == path && c.Name == name);
        }

        public void Clear()
        {
            _cookies.Clear();
        }

        public int Count { get { return _cookies.Count; } }

        /// <summary>
        /// RequestsCookieJar.__getitem__: the value of the only cookie with that name; KeyError if there is none
        /// (or its value is empty), error on conflicting cookies.
        /// </summary>
        public string this[string name]
        {
            get
            {
                string result = null;
                foreach (Cookie c in _cookies)
                {
                    if (c.Name == name)
                    {
                        if (result != null)
                            throw new InvalidOperationException(string.Format("There are multiple cookies with name, {0}", PyStr.Repr(name)));
                        result = c.Value;
                    }
                }
                if (!string.IsNullOrEmpty(result))
                    return result;
                throw new PyKeyError(string.Format("name={0}, domain=None, path=None", PyStr.Repr(name)));
            }
            set { Set(name, value); }
        }

        /// <summary>RequestsCookieJar.get(name, default) without conflict check.</summary>
        public string Get(string name, string defaultValue = null)
        {
            string result = null;
            foreach (Cookie c in _cookies)
                if (c.Name == name)
                    result = c.Value;
            return result ?? defaultValue;
        }

        /// <summary>requests.utils.dict_from_cookiejar / RequestsCookieJar.get_dict().</summary>
        public List<KeyValuePair<string, string>> GetDict()
        {
            var order = new List<string>();
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Cookie c in _cookies)
            {
                if (!values.ContainsKey(c.Name))
                    order.Add(c.Name);
                values[c.Name] = c.Value;
            }
            var result = new List<KeyValuePair<string, string>>();
            foreach (string name in order)
                result.Add(new KeyValuePair<string, string>(name, values[name]));
            return result;
        }

        /// <summary>Dictionary view of <see cref="GetDict"/>.</summary>
        public Dictionary<string, string> ToDictionary()
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in GetDict())
                d[kv.Key] = kv.Value;
            return d;
        }

        /// <summary>requests.utils.cookiejar_from_dict.</summary>
        public static CookieJar FromDict(IEnumerable<KeyValuePair<string, string>> dict)
        {
            var jar = new CookieJar();
            foreach (var kv in dict)
                jar.SetCookie(new Cookie(kv.Key, kv.Value ?? ""));
            return jar;
        }

        public IEnumerator<Cookie> GetEnumerator()
        {
            return new List<Cookie>(_cookies).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #region request and response handling

        private static bool DomainReturnOk(Cookie cookie, string host)
        {
            if (cookie.Domain.Length == 0)
                return true;
            string erhn = host.IndexOf('.') >= 0 ? host : host + ".local";
            if (cookie.Domain.StartsWith("."))
                return ("." + erhn).EndsWith(cookie.Domain, StringComparison.OrdinalIgnoreCase);
            return string.Equals(erhn, cookie.Domain, StringComparison.OrdinalIgnoreCase) ||
                   ("." + erhn).EndsWith("." + cookie.Domain, StringComparison.OrdinalIgnoreCase) && cookie.DomainSpecified;
        }

        private static bool PathReturnOk(Cookie cookie, string requestPath)
        {
            string path = cookie.Path;
            if (requestPath == path)
                return true;
            return requestPath.StartsWith(path, StringComparison.Ordinal) &&
                   (path.EndsWith("/") || (requestPath.Length > path.Length && requestPath[path.Length] == '/'));
        }

        /// <summary>The value for the Cookie request header, or null if no cookie applies.</summary>
        public string GetCookieHeader(ParsedUrl url)
        {
            DateTime now = DateTime.UtcNow;
            var applicable = new List<Cookie>();
            foreach (Cookie c in _cookies)
            {
                if (c.IsExpired(now))
                    continue;
                if (c.Secure && url.Scheme != "https")
                    continue;
                if (!DomainReturnOk(c, url.Host))
                    continue;
                if (!PathReturnOk(c, url.Path))
                    continue;
                applicable.Add(c);
            }
            if (applicable.Count == 0)
                return null;
            // http.cookiejar sorts by path length (longest first), stable
            var indexed = new List<KeyValuePair<int, Cookie>>();
            for (int i = 0; i < applicable.Count; i++)
                indexed.Add(new KeyValuePair<int, Cookie>(i, applicable[i]));
            indexed.Sort((a, b) =>
            {
                int c = b.Value.Path.Length.CompareTo(a.Value.Path.Length);
                return c != 0 ? c : a.Key.CompareTo(b.Key);
            });
            var parts = new List<string>();
            foreach (var kv in indexed)
                parts.Add(kv.Value.Value == null ? kv.Value.Name : kv.Value.Name + "=" + kv.Value.Value);
            return string.Join("; ", parts);
        }

        /// <summary>Processes the Set-Cookie headers of a response, like CookieJar.extract_cookies.</summary>
        public void ExtractCookies(ParsedUrl requestUrl, IEnumerable<string> setCookieHeaders)
        {
            foreach (string header in setCookieHeaders)
            {
                Cookie cookie;
                bool expired;
                if (!TryParseSetCookie(header, requestUrl, out cookie, out expired))
                    continue;
                if (expired)
                {
                    Clear(cookie.Domain, cookie.Path, cookie.Name);
                    continue;
                }
                SetCookie(cookie);
            }
        }

        private static bool TryParseSetCookie(string header, ParsedUrl url, out Cookie cookie, out bool expired)
        {
            cookie = null;
            expired = false;
            string[] parts = header.Split(';');
            string first = parts[0];
            int eq = first.IndexOf('=');
            string name = (eq >= 0 ? first.Substring(0, eq) : first).Trim();
            if (name.Length == 0)
                return false;
            string value = eq >= 0 ? first.Substring(eq + 1).Trim() : null;
            string domain = null;
            string path = null;
            bool secure = false;
            DateTime? expires = null;
            bool maxAgeSet = false;
            DateTime now = DateTime.UtcNow;
            for (int i = 1; i < parts.Length; i++)
            {
                string p = parts[i];
                int peq = p.IndexOf('=');
                string k = (peq >= 0 ? p.Substring(0, peq) : p).Trim().ToLowerInvariant();
                string v = peq >= 0 ? p.Substring(peq + 1).Trim() : null;
                if (k.Length == 0)
                    continue;
                switch (k)
                {
                    case "domain":
                        if (!string.IsNullOrEmpty(v))
                            domain = v.ToLowerInvariant();
                        break;
                    case "path":
                        if (!string.IsNullOrEmpty(v))
                            path = v;
                        break;
                    case "secure":
                        secure = true;
                        break;
                    case "max-age":
                        {
                            long seconds;
                            if (v != null && long.TryParse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out seconds))
                            {
                                maxAgeSet = true;
                                expires = seconds <= 0 ? now.AddSeconds(-1) :
                                    (seconds > 315360000000L ? DateTime.MaxValue : now.AddSeconds(seconds));
                            }
                            break;
                        }
                    case "expires":
                        if (!maxAgeSet && v != null)
                        {
                            DateTime? parsed = ParseHttpDate(v.Trim('"'));
                            if (parsed.HasValue)
                                expires = parsed;
                        }
                        break;
                }
            }

            string host = url.Host;
            string erhn = host.IndexOf('.') >= 0 ? host : host + ".local";
            bool domainSpecified = domain != null;
            if (domain == null)
            {
                domain = erhn;
            }
            else
            {
                if (!domain.StartsWith("."))
                    domain = "." + domain;
                // set_ok_domain: the request host must domain-match the cookie domain
                if (!("." + erhn).EndsWith(domain, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            if (path == null)
            {
                path = url.Path;
                int slash = path.LastIndexOf('/');
                if (slash >= 0)
                    path = path.Substring(0, slash);
                if (path.Length == 0)
                    path = "/";
            }
            cookie = new Cookie(name, value, domain, path)
            {
                DomainSpecified = domainSpecified,
                Secure = secure,
                Expires = expires
            };
            expired = expires.HasValue && expires.Value <= now;
            return true;
        }

        private static readonly string[] Months = { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };

        /// <summary>Lenient HTTP date parsing like http.cookiejar.http2time. Returns UTC.</summary>
        public static DateTime? ParseHttpDate(string text)
        {
            // e.g. "Wed, 21 Oct 2015 07:28:00 GMT", "Wednesday, 21-Oct-15 07:28:00 GMT", "Wed Oct 21 07:28:00 2015"
            Match m = Regex.Match(text, @"(\d{1,2})[\s-]+([A-Za-z]{3})[A-Za-z]*[\s-]+(\d{2,4})\s+(\d{1,2}):(\d{2})(?::(\d{2}))?");
            int day, year, hour, minute, second = 0;
            string mon;
            if (m.Success)
            {
                day = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                mon = m.Groups[2].Value.ToLowerInvariant();
                year = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                hour = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
                minute = int.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture);
                if (m.Groups[6].Success)
                    second = int.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture);
            }
            else
            {
                m = Regex.Match(text, @"([A-Za-z]{3})[A-Za-z]*\s+(\d{1,2})\s+(\d{1,2}):(\d{2})(?::(\d{2}))?\s+(\d{4})");
                if (!m.Success)
                    return null;
                mon = m.Groups[1].Value.ToLowerInvariant();
                day = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                hour = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                minute = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
                if (m.Groups[5].Success)
                    second = int.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture);
                year = int.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture);
            }
            int month = Array.IndexOf(Months, mon) + 1;
            if (month <= 0)
                return null;
            if (year < 100)
                year += year < 70 ? 2000 : 1900;
            try
            {
                return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        #endregion
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using InstaloaderSharp.Json;
using InstaloaderSharp.Python;

namespace InstaloaderSharp.Http
{
    /// <summary>Counterpart of requests.exceptions.RequestException.</summary>
    public class RequestException : Exception
    {
        public RequestException(string message) : base(message) { }
        public RequestException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>Counterpart of requests.exceptions.ConnectionError.</summary>
    public class RequestConnectionException : RequestException
    {
        public RequestConnectionException(string message) : base(message) { }
    }

    /// <summary>Counterpart of requests.exceptions.Timeout.</summary>
    public class RequestTimeoutException : RequestException
    {
        public RequestTimeoutException(string message) : base(message) { }
    }

    /// <summary>A URL split into its parts (without using System.Uri, which alters some paths).</summary>
    public sealed class ParsedUrl
    {
        public string Scheme;
        public string Host;
        public int Port;
        public string PathAndQuery;
        public string Path;
        public string Query;

        public static ParsedUrl Parse(string url)
        {
            int schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
            if (schemeEnd <= 0)
                throw new RequestException(string.Format("Invalid URL {0}: No scheme supplied.", PyStr.Repr(url)));
            var result = new ParsedUrl { Scheme = url.Substring(0, schemeEnd).ToLowerInvariant() };
            if (result.Scheme != "http" && result.Scheme != "https")
                throw new RequestException(string.Format("No connection adapters were found for {0}", PyStr.Repr(url)));
            string rest = url.Substring(schemeEnd + 3);
            int fragment = rest.IndexOf('#');
            if (fragment >= 0)
                rest = rest.Substring(0, fragment);
            int pathStart = rest.IndexOfAny(new[] { '/', '?' });
            string authority = pathStart >= 0 ? rest.Substring(0, pathStart) : rest;
            string pathQuery = pathStart >= 0 ? rest.Substring(pathStart) : "/";
            if (pathQuery.StartsWith("?"))
                pathQuery = "/" + pathQuery;
            int at = authority.LastIndexOf('@');
            if (at >= 0)
                authority = authority.Substring(at + 1);
            int port = result.Scheme == "https" ? 443 : 80;
            string host = authority;
            int colon = authority.LastIndexOf(':');
            if (colon >= 0 && authority.IndexOf(']') < colon)
            {
                host = authority.Substring(0, colon);
                int p;
                if (int.TryParse(authority.Substring(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out p))
                    port = p;
            }
            if (host.Length == 0)
                throw new RequestException(string.Format("Invalid URL {0}: No host supplied", PyStr.Repr(url)));
            result.Host = host.ToLowerInvariant();
            result.Port = port;
            result.PathAndQuery = pathQuery;
            int q = pathQuery.IndexOf('?');
            result.Path = q >= 0 ? pathQuery.Substring(0, q) : pathQuery;
            result.Query = q >= 0 ? pathQuery.Substring(q + 1) : "";
            return result;
        }

        public string Origin
        {
            get
            {
                bool defaultPort = (Scheme == "https" && Port == 443) || (Scheme == "http" && Port == 80);
                return Scheme + "://" + Host + (defaultPort ? "" : ":" + Port.ToString(CultureInfo.InvariantCulture));
            }
        }

        public override string ToString()
        {
            return Origin + PathAndQuery;
        }
    }

    /// <summary>URL quoting helpers corresponding to urllib.parse and requests.utils.</summary>
    public static class UrlUtil
    {
        private const string AlwaysSafe = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_.-~";

        /// <summary>urllib.parse.quote(s, safe).</summary>
        public static string Quote(string s, string safe = "/")
        {
            var sb = new StringBuilder();
            foreach (byte b in Encoding.UTF8.GetBytes(s))
            {
                char c = (char)b;
                if (b < 0x80 && (AlwaysSafe.IndexOf(c) >= 0 || safe.IndexOf(c) >= 0))
                    sb.Append(c);
                else
                    sb.AppendFormat(CultureInfo.InvariantCulture, "%{0:X2}", b);
            }
            return sb.ToString();
        }

        /// <summary>urllib.parse.quote_plus(s).</summary>
        public static string QuotePlus(string s)
        {
            if (s.IndexOf(' ') < 0)
                return Quote(s, "");
            return Quote(s, " ").Replace(' ', '+');
        }

        /// <summary>urllib.parse.unquote(s).</summary>
        public static string Unquote(string s)
        {
            if (s.IndexOf('%') < 0)
                return s;
            var bytes = new List<byte>();
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                int hi, lo;
                if (s[i] == '%' && i + 2 < s.Length &&
                    (hi = HexValue(s[i + 1])) >= 0 && (lo = HexValue(s[i + 2])) >= 0)
                {
                    bytes.Add((byte)(hi * 16 + lo));
                    i += 2;
                    continue;
                }
                if (bytes.Count > 0)
                {
                    sb.Append(Encoding.UTF8.GetString(bytes.ToArray()));
                    bytes.Clear();
                }
                sb.Append(s[i]);
            }
            if (bytes.Count > 0)
                sb.Append(Encoding.UTF8.GetString(bytes.ToArray()));
            return sb.ToString();
        }

        private static int HexValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        /// <summary>requests.utils.requote_uri: quotes characters that are not allowed in URLs.</summary>
        public static string RequoteUri(string uri)
        {
            const string safeWithPercent = "!#$%&'()*+,/:;=?@[]~";
            const string safeWithoutPercent = "!#$&'()*+,/:;=?@[]~";
            string unquoted = UnquoteUnreserved(uri);
            if (unquoted == null)
                return Quote(uri, safeWithoutPercent);
            return Quote(unquoted, safeWithPercent);
        }

        private static string UnquoteUnreserved(string uri)
        {
            string[] parts = uri.Split('%');
            var sb = new StringBuilder(parts[0]);
            for (int i = 1; i < parts.Length; i++)
            {
                string h = parts[i];
                if (h.Length >= 2 && HexValue(h[0]) >= 0 && HexValue(h[1]) >= 0)
                {
                    char c = (char)(HexValue(h[0]) * 16 + HexValue(h[1]));
                    if (c < 0x80 && AlwaysSafe.IndexOf(c) >= 0)
                        sb.Append(c).Append(h.Substring(2));
                    else
                        sb.Append('%').Append(h);
                }
                else if (h.Length >= 2)
                {
                    // Invalid percent-escape sequence
                    return null;
                }
                else
                {
                    sb.Append('%').Append(h);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// urllib.parse.urlencode as used by requests for params and form data: None values are skipped,
        /// other values are converted with Python's str().
        /// </summary>
        public static string UrlEncode(JObject data)
        {
            var parts = new List<string>();
            if (data == null)
                return "";
            foreach (var kv in data)
            {
                if (kv.Value.IsNone())
                    continue;
                var arr = kv.Value as JArray;
                if (arr != null)
                {
                    foreach (JToken item in arr)
                        if (!item.IsNone())
                            parts.Add(QuotePlus(kv.Key) + "=" + QuotePlus(PyStr.Str(item)));
                    continue;
                }
                parts.Add(QuotePlus(kv.Key) + "=" + QuotePlus(PyStr.Str(kv.Value)));
            }
            return string.Join("&", parts);
        }

        /// <summary>Appends encoded params to a URL like requests' PreparedRequest.prepare_url.</summary>
        public static string PrepareUrl(string url, JObject parameters)
        {
            string enc = UrlEncode(parameters);
            int fragmentIndex = url.IndexOf('#');
            string fragment = fragmentIndex >= 0 ? url.Substring(fragmentIndex) : "";
            string withoutFragment = fragmentIndex >= 0 ? url.Substring(0, fragmentIndex) : url;
            if (enc.Length > 0)
            {
                int q = withoutFragment.IndexOf('?');
                if (q >= 0 && q < withoutFragment.Length - 1)
                    withoutFragment = withoutFragment + "&" + enc;
                else if (q >= 0)
                    withoutFragment = withoutFragment + enc;
                else
                    withoutFragment = withoutFragment + "?" + enc;
            }
            ParsedUrl parsed = ParsedUrl.Parse(withoutFragment);
            // requests normalizes an empty path to '/'
            string full = parsed.Origin + parsed.PathAndQuery;
            return RequoteUri(full) + fragment;
        }

        /// <summary>Resolves a (possibly relative) redirect location against a base URL.</summary>
        public static string Resolve(string baseUrl, string location)
        {
            if (location.StartsWith("//"))
                return ParsedUrl.Parse(baseUrl).Scheme + ":" + location;
            if (location.IndexOf("://", StringComparison.Ordinal) > 0)
                return location;
            ParsedUrl b = ParsedUrl.Parse(baseUrl);
            if (location.StartsWith("/"))
                return b.Origin + location;
            if (location.StartsWith("?"))
                return b.Origin + b.Path + location;
            string dir = b.Path.Substring(0, b.Path.LastIndexOf('/') + 1);
            return b.Origin + dir + location;
        }
    }

    /// <summary>
    /// Case-insensitive, insertion-ordered header dictionary, like requests' CaseInsensitiveDict.
    /// Response headers may contain repeated names; the indexer then returns the values joined with ", ".
    /// </summary>
    public sealed class HttpHeaders : IEnumerable<KeyValuePair<string, string>>
    {
        private readonly List<KeyValuePair<string, string>> _items = new List<KeyValuePair<string, string>>();

        public HttpHeaders() { }

        public HttpHeaders(IEnumerable<KeyValuePair<string, string>> items)
        {
            foreach (var kv in items)
                _items.Add(kv);
        }

        public int Count { get { return _items.Count; } }

        private int IndexOf(string name)
        {
            for (int i = 0; i < _items.Count; i++)
                if (string.Equals(_items[i].Key, name, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        /// <summary>Gets (joined values; KeyError if missing) or sets (replacing all occurrences) a header.</summary>
        public string this[string name]
        {
            get
            {
                string value = Get(name);
                if (value == null)
                    throw new PyKeyError(name);
                return value;
            }
            set
            {
                if (value == null)
                {
                    Remove(name);
                    return;
                }
                int index = IndexOf(name);
                if (index < 0)
                {
                    _items.Add(new KeyValuePair<string, string>(name, value));
                    return;
                }
                // requests keeps the most recently set spelling of the key
                _items[index] = new KeyValuePair<string, string>(name, value);
                for (int i = _items.Count - 1; i > index; i--)
                    if (string.Equals(_items[i].Key, name, StringComparison.OrdinalIgnoreCase))
                        _items.RemoveAt(i);
            }
        }

        /// <summary>Value of the header (multiple values joined with ", "), or null.</summary>
        public string Get(string name)
        {
            List<string> values = GetAll(name);
            if (values.Count == 0)
                return null;
            return string.Join(", ", values);
        }

        public List<string> GetAll(string name)
        {
            var values = new List<string>();
            foreach (var kv in _items)
                if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
                    values.Add(kv.Value);
            return values;
        }

        public bool ContainsKey(string name)
        {
            return IndexOf(name) >= 0;
        }

        /// <summary>Appends a header without replacing existing ones (for response headers).</summary>
        public void Append(string name, string value)
        {
            _items.Add(new KeyValuePair<string, string>(name, value));
        }

        public bool Remove(string name)
        {
            bool removed = false;
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_items[i].Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    _items.RemoveAt(i);
                    removed = true;
                }
            }
            return removed;
        }

        /// <summary>dict.update(): sets each given header.</summary>
        public void Update(IEnumerable<KeyValuePair<string, string>> other)
        {
            foreach (var kv in other)
                this[kv.Key] = kv.Value;
        }

        public void Add(string name, string value)
        {
            this[name] = value;
        }

        public HttpHeaders Copy()
        {
            return new HttpHeaders(_items);
        }

        /// <summary>Items with repeated names merged (as iterating a CaseInsensitiveDict yields).</summary>
        public List<KeyValuePair<string, string>> Merged()
        {
            var result = new List<KeyValuePair<string, string>>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _items)
            {
                if (seen.Add(kv.Key))
                    result.Add(new KeyValuePair<string, string>(kv.Key, Get(kv.Key)));
            }
            return result;
        }

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}

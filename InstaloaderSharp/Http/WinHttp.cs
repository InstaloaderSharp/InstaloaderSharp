using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using InstaloaderSharp.Util;

namespace InstaloaderSharp.Http
{
    internal static class WinHttpNative
    {
        public const uint WINHTTP_ACCESS_TYPE_DEFAULT_PROXY = 0;
        public const uint WINHTTP_ACCESS_TYPE_NO_PROXY = 1;
        public const uint WINHTTP_ACCESS_TYPE_NAMED_PROXY = 3;
        public const uint WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY = 4;

        public const uint WINHTTP_FLAG_SECURE = 0x00800000;
        public const uint WINHTTP_FLAG_ESCAPE_DISABLE = 0x00000040;
        public const uint WINHTTP_FLAG_ESCAPE_DISABLE_QUERY = 0x00000080;

        public const uint WINHTTP_ADDREQ_FLAG_ADD = 0x20000000;
        public const uint WINHTTP_ADDREQ_FLAG_REPLACE = 0x80000000;

        public const uint WINHTTP_QUERY_STATUS_CODE = 19;
        public const uint WINHTTP_QUERY_STATUS_TEXT = 20;
        public const uint WINHTTP_QUERY_RAW_HEADERS_CRLF = 22;
        public const uint WINHTTP_QUERY_FLAG_NUMBER = 0x20000000;

        public const uint WINHTTP_OPTION_SECURE_PROTOCOLS = 84;
        public const uint WINHTTP_OPTION_DISABLE_FEATURE = 63;
        public const uint WINHTTP_OPTION_REDIRECT_POLICY = 88;
        public const uint WINHTTP_OPTION_ENABLE_HTTP_PROTOCOL = 133;
        public const uint WINHTTP_OPTION_HTTP_PROTOCOL_USED = 134;
        public const uint WINHTTP_OPTION_MAX_RESPONSE_HEADER_SIZE = 91;

        public const uint WINHTTP_DISABLE_COOKIES = 0x00000001;
        public const uint WINHTTP_DISABLE_REDIRECTS = 0x00000002;
        public const uint WINHTTP_DISABLE_AUTHENTICATION = 0x00000004;
        public const uint WINHTTP_OPTION_REDIRECT_POLICY_NEVER = 0;
        public const uint WINHTTP_PROTOCOL_FLAG_HTTP2 = 0x1;

        public const uint WINHTTP_FLAG_SECURE_PROTOCOL_TLS1 = 0x00000080;
        public const uint WINHTTP_FLAG_SECURE_PROTOCOL_TLS1_1 = 0x00000200;
        public const uint WINHTTP_FLAG_SECURE_PROTOCOL_TLS1_2 = 0x00000800;
        public const uint WINHTTP_FLAG_SECURE_PROTOCOL_TLS1_3 = 0x00002000;

        public const int ERROR_INSUFFICIENT_BUFFER = 122;
        public const int ERROR_WINHTTP_TIMEOUT = 12002;
        public const int ERROR_WINHTTP_OPERATION_CANCELLED = 12017;
        public const int ERROR_WINHTTP_INCORRECT_HANDLE_STATE = 12019;
        public const int ERROR_WINHTTP_INVALID_HANDLE = 6;

        [DllImport("winhttp.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr WinHttpOpen(string pszAgentW, uint dwAccessType, string pszProxyW,
                                                string pszProxyBypassW, uint dwFlags);

        [DllImport("winhttp.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr WinHttpConnect(IntPtr hSession, string pswzServerName, ushort nServerPort,
                                                   uint dwReserved);

        [DllImport("winhttp.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr WinHttpOpenRequest(IntPtr hConnect, string pwszVerb, string pwszObjectName,
                                                       string pwszVersion, string pwszReferrer,
                                                       IntPtr ppwszAcceptTypes, uint dwFlags);

        [DllImport("winhttp.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WinHttpAddRequestHeaders(IntPtr hRequest, string pwszHeaders, uint dwHeadersLength,
                                                           uint dwModifiers);

        [DllImport("winhttp.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WinHttpSendRequest(IntPtr hRequest, IntPtr lpszHeaders, uint dwHeadersLength,
                                                     byte[] lpOptional, uint dwOptionalLength, uint dwTotalLength,
                                                     IntPtr dwContext);

        [DllImport("winhttp.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WinHttpReceiveResponse(IntPtr hRequest, IntPtr lpReserved);

        [DllImport("winhttp.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WinHttpQueryHeaders(IntPtr hRequest, uint dwInfoLevel, string pwszName,
                                                      StringBuilder lpBuffer, ref uint lpdwBufferLength,
                                                      IntPtr lpdwIndex);

        [DllImport("winhttp.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "WinHttpQueryHeaders")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WinHttpQueryHeadersNumber(IntPtr hRequest, uint dwInfoLevel, string pwszName,
                                                            ref uint lpBuffer, ref uint lpdwBufferLength,
                                                            IntPtr lpdwIndex);

        [DllImport("winhttp.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WinHttpReadData(IntPtr hRequest, byte[] lpBuffer, uint dwNumberOfBytesToRead,
                                                  out uint lpdwNumberOfBytesRead);

        [DllImport("winhttp.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WinHttpSetOption(IntPtr hInternet, uint dwOption, ref uint lpBuffer,
                                                   uint dwBufferLength);

        [DllImport("winhttp.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WinHttpQueryOption(IntPtr hInternet, uint dwOption, ref uint lpBuffer,
                                                     ref uint lpdwBufferLength);

        [DllImport("winhttp.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WinHttpSetTimeouts(IntPtr hInternet, int nResolveTimeout, int nConnectTimeout,
                                                     int nSendTimeout, int nReceiveTimeout);

        [DllImport("winhttp.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WinHttpCloseHandle(IntPtr hInternet);
    }

    /// <summary>A handle pair (connection, request) of one WinHTTP request that can be closed from any thread.</summary>
    internal sealed class WinHttpRequestHandle : IDisposable
    {
        private readonly object _lock = new object();
        private IntPtr _connect;
        private IntPtr _request;
        private bool _closed;

        private static readonly HashSet<WinHttpRequestHandle> Active = new HashSet<WinHttpRequestHandle>();

        static WinHttpRequestHandle()
        {
            Interrupt.Interrupted += () =>
            {
                List<WinHttpRequestHandle> handles;
                lock (Active)
                    handles = new List<WinHttpRequestHandle>(Active);
                foreach (WinHttpRequestHandle h in handles)
                    h.Dispose();
            };
        }

        public WinHttpRequestHandle(IntPtr connect, IntPtr request)
        {
            _connect = connect;
            _request = request;
            lock (Active)
                Active.Add(this);
        }

        public IntPtr Request
        {
            get
            {
                lock (_lock)
                {
                    if (_closed)
                        throw new RequestCancelledException();
                    return _request;
                }
            }
        }

        public bool IsClosed { get { lock (_lock) return _closed; } }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_closed)
                    return;
                _closed = true;
                if (_request != IntPtr.Zero)
                    WinHttpNative.WinHttpCloseHandle(_request);
                if (_connect != IntPtr.Zero)
                    WinHttpNative.WinHttpCloseHandle(_connect);
                _request = IntPtr.Zero;
                _connect = IntPtr.Zero;
            }
            lock (Active)
                Active.Remove(this);
        }
    }

    internal sealed class RequestCancelledException : Exception
    {
        public RequestCancelledException() : base("The request was cancelled") { }
    }

    /// <summary>Raw response as received from WinHTTP.</summary>
    internal sealed class WinHttpRawResponse
    {
        public int StatusCode;
        public string Reason;
        public string HttpVersion;
        public List<KeyValuePair<string, string>> Headers = new List<KeyValuePair<string, string>>();
        public WinHttpRequestHandle Handle;
    }

    /// <summary>Stream reading the response body of a WinHTTP request.</summary>
    internal sealed class WinHttpBodyStream : Stream
    {
        private readonly WinHttpRequestHandle _handle;
        private bool _eof;

        public WinHttpBodyStream(WinHttpRequestHandle handle)
        {
            _handle = handle;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_eof || count == 0)
                return 0;
            Interrupt.Check();
            var temp = offset == 0 ? buffer : new byte[count];
            uint read;
            bool ok;
            try
            {
                ok = WinHttpNative.WinHttpReadData(_handle.Request, temp, (uint)count, out read);
            }
            catch (RequestCancelledException)
            {
                Interrupt.Check();
                throw new RequestException("Connection closed");
            }
            if (!ok)
            {
                int error = Marshal.GetLastWin32Error();
                Interrupt.Check();
                throw WinHttpClient.ErrorToException(error);
            }
            if (read == 0)
            {
                _eof = true;
                return 0;
            }
            if (!ReferenceEquals(temp, buffer))
                Buffer.BlockCopy(temp, 0, buffer, offset, (int)read);
            return (int)read;
        }

        protected override void Dispose(bool disposing)
        {
            _handle.Dispose();
            base.Dispose(disposing);
        }

        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return false; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { throw new NotSupportedException(); } }
        public override long Position
        {
            get { throw new NotSupportedException(); }
            set { throw new NotSupportedException(); }
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
        public override void SetLength(long value) { throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
    }

    /// <summary>Process-wide WinHTTP session (connection pool) used by all <see cref="Session"/> objects.</summary>
    internal static class WinHttpClient
    {
        private static IntPtr _session = IntPtr.Zero;
        private static readonly object SessionLock = new object();

        private static IntPtr GetSession()
        {
            lock (SessionLock)
            {
                if (_session != IntPtr.Zero)
                    return _session;
                string proxy = Environment.GetEnvironmentVariable("HTTPS_PROXY") ??
                               Environment.GetEnvironmentVariable("https_proxy") ??
                               Environment.GetEnvironmentVariable("ALL_PROXY") ??
                               Environment.GetEnvironmentVariable("all_proxy");
                IntPtr session = IntPtr.Zero;
                if (!string.IsNullOrEmpty(proxy))
                {
                    string p = proxy;
                    int scheme = p.IndexOf("://", StringComparison.Ordinal);
                    if (scheme >= 0)
                        p = p.Substring(scheme + 3);
                    p = p.TrimEnd('/');
                    string bypass = Environment.GetEnvironmentVariable("NO_PROXY") ??
                                    Environment.GetEnvironmentVariable("no_proxy");
                    if (bypass != null)
                        bypass = bypass.Replace(',', ';');
                    session = WinHttpNative.WinHttpOpen("", WinHttpNative.WINHTTP_ACCESS_TYPE_NAMED_PROXY, p,
                                                        string.IsNullOrEmpty(bypass) ? null : bypass, 0);
                }
                if (session == IntPtr.Zero)
                    session = WinHttpNative.WinHttpOpen("", WinHttpNative.WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY, null, null, 0);
                if (session == IntPtr.Zero)
                    session = WinHttpNative.WinHttpOpen("", WinHttpNative.WINHTTP_ACCESS_TYPE_DEFAULT_PROXY, null, null, 0);
                if (session == IntPtr.Zero)
                    throw new RequestException("Unable to initialize WinHTTP: " + ErrorMessage(Marshal.GetLastWin32Error()));
                uint protocols = WinHttpNative.WINHTTP_FLAG_SECURE_PROTOCOL_TLS1_2 | WinHttpNative.WINHTTP_FLAG_SECURE_PROTOCOL_TLS1_3;
                if (!WinHttpNative.WinHttpSetOption(session, WinHttpNative.WINHTTP_OPTION_SECURE_PROTOCOLS, ref protocols, 4))
                {
                    protocols = WinHttpNative.WINHTTP_FLAG_SECURE_PROTOCOL_TLS1_2 | WinHttpNative.WINHTTP_FLAG_SECURE_PROTOCOL_TLS1_1;
                    WinHttpNative.WinHttpSetOption(session, WinHttpNative.WINHTTP_OPTION_SECURE_PROTOCOLS, ref protocols, 4);
                }
                // Python's http.client._MAXHEADERS is raised to 200 for logging in; allow large header blocks.
                uint maxHeaderSize = 256 * 1024;
                WinHttpNative.WinHttpSetOption(session, WinHttpNative.WINHTTP_OPTION_MAX_RESPONSE_HEADER_SIZE, ref maxHeaderSize, 4);
                _session = session;
                return _session;
            }
        }

        public static string ErrorMessage(int error)
        {
            switch (error)
            {
                case 12001: return "Out of handles";
                case 12002: return "The operation timed out";
                case 12004: return "An internal error occurred";
                case 12005: return "The URL is invalid";
                case 12006: return "The URL scheme could not be recognized or is not supported";
                case 12007: return "The server name could not be resolved";
                case 12017: return "The operation was cancelled";
                case 12029: return "A connection with the server could not be established";
                case 12030: return "The connection with the server was terminated abnormally";
                case 12031: return "The connection with the server was reset";
                case 12150: return "The requested header could not be located";
                case 12152: return "The server returned an invalid or unrecognized response";
                case 12175: return "A security error occurred (SSL/TLS)";
                case 12178: return "The proxy server could not be reached";
            }
            return new Win32Exception(error).Message;
        }

        public static Exception ErrorToException(int error)
        {
            string message = string.Format("WinHTTP error {0}: {1}", error, ErrorMessage(error));
            if (error == WinHttpNative.ERROR_WINHTTP_TIMEOUT)
                return new RequestTimeoutException(message);
            return new RequestConnectionException(message);
        }

        private static Exception LastError()
        {
            int error = Marshal.GetLastWin32Error();
            Interrupt.Check();
            return ErrorToException(error);
        }

        /// <summary>Sends a request; returns once the response headers have been received.</summary>
        public static WinHttpRawResponse Send(string method, ParsedUrl url, List<KeyValuePair<string, string>> headers,
                                              byte[] body, bool http2, double? timeoutSeconds)
        {
            Interrupt.Check();
            IntPtr session = GetSession();
            IntPtr connect = WinHttpNative.WinHttpConnect(session, url.Host, (ushort)url.Port, 0);
            if (connect == IntPtr.Zero)
                throw LastError();
            uint flags = WinHttpNative.WINHTTP_FLAG_ESCAPE_DISABLE | WinHttpNative.WINHTTP_FLAG_ESCAPE_DISABLE_QUERY;
            if (url.Scheme == "https")
                flags |= WinHttpNative.WINHTTP_FLAG_SECURE;
            IntPtr request = WinHttpNative.WinHttpOpenRequest(connect, method, url.PathAndQuery, null, null, IntPtr.Zero, flags);
            if (request == IntPtr.Zero)
            {
                Exception ex = LastError();
                WinHttpNative.WinHttpCloseHandle(connect);
                throw ex;
            }
            var handle = new WinHttpRequestHandle(connect, request);
            try
            {
                uint disable = WinHttpNative.WINHTTP_DISABLE_COOKIES | WinHttpNative.WINHTTP_DISABLE_REDIRECTS |
                               WinHttpNative.WINHTTP_DISABLE_AUTHENTICATION;
                WinHttpNative.WinHttpSetOption(handle.Request, WinHttpNative.WINHTTP_OPTION_DISABLE_FEATURE, ref disable, 4);
                uint redirectPolicy = WinHttpNative.WINHTTP_OPTION_REDIRECT_POLICY_NEVER;
                WinHttpNative.WinHttpSetOption(handle.Request, WinHttpNative.WINHTTP_OPTION_REDIRECT_POLICY, ref redirectPolicy, 4);
                if (http2)
                {
                    uint protocol = WinHttpNative.WINHTTP_PROTOCOL_FLAG_HTTP2;
                    WinHttpNative.WinHttpSetOption(handle.Request, WinHttpNative.WINHTTP_OPTION_ENABLE_HTTP_PROTOCOL, ref protocol, 4);
                }
                int timeoutMs = 0;
                if (timeoutSeconds.HasValue)
                    timeoutMs = (int)Math.Min(int.MaxValue, Math.Max(1, timeoutSeconds.Value * 1000));
                WinHttpNative.WinHttpSetTimeouts(handle.Request, timeoutMs, timeoutMs, timeoutMs, timeoutMs);

                if (headers.Count > 0)
                {
                    var sb = new StringBuilder();
                    foreach (var kv in headers)
                    {
                        sb.Append(kv.Key).Append(": ").Append(kv.Value).Append("\r\n");
                    }
                    // Only ADD: the request handle is fresh, and with REPLACE an empty value means "remove the
                    // header", which fails with ERROR_WINHTTP_HEADER_NOT_FOUND (12150) if it is not present.
                    if (!WinHttpNative.WinHttpAddRequestHeaders(handle.Request, sb.ToString(), uint.MaxValue,
                                                                 WinHttpNative.WINHTTP_ADDREQ_FLAG_ADD))
                        throw LastError();
                }
                uint bodyLength = body == null ? 0 : (uint)body.Length;
                if (!WinHttpNative.WinHttpSendRequest(handle.Request, IntPtr.Zero, 0, body, bodyLength, bodyLength, IntPtr.Zero))
                    throw LastError();
                if (!WinHttpNative.WinHttpReceiveResponse(handle.Request, IntPtr.Zero))
                    throw LastError();

                var result = new WinHttpRawResponse { Handle = handle };
                uint status = 0;
                uint size = 4;
                if (!WinHttpNative.WinHttpQueryHeadersNumber(handle.Request,
                        WinHttpNative.WINHTTP_QUERY_STATUS_CODE | WinHttpNative.WINHTTP_QUERY_FLAG_NUMBER,
                        null, ref status, ref size, IntPtr.Zero))
                    throw LastError();
                result.StatusCode = (int)status;
                string raw = QueryString(handle.Request, WinHttpNative.WINHTTP_QUERY_RAW_HEADERS_CRLF) ?? "";
                string[] lines = raw.Split(new[] { "\r\n" }, StringSplitOptions.None);
                if (lines.Length > 0)
                {
                    string statusLine = lines[0];
                    string[] parts = statusLine.Split(new[] { ' ' }, 3);
                    result.HttpVersion = parts.Length > 0 ? parts[0] : "";
                    result.Reason = parts.Length > 2 ? parts[2].Trim() : "";
                }
                uint used = 0;
                uint usedSize = 4;
                if (WinHttpNative.WinHttpQueryOption(handle.Request, WinHttpNative.WINHTTP_OPTION_HTTP_PROTOCOL_USED, ref used, ref usedSize)
                    && (used & WinHttpNative.WINHTTP_PROTOCOL_FLAG_HTTP2) != 0)
                    result.HttpVersion = "HTTP/2";
                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line.Length == 0)
                        continue;
                    int colon = line.IndexOf(':');
                    if (colon <= 0)
                        continue;
                    result.Headers.Add(new KeyValuePair<string, string>(line.Substring(0, colon).Trim(),
                                                                        line.Substring(colon + 1).Trim()));
                }
                if (string.IsNullOrEmpty(result.Reason))
                    result.Reason = HttpStatus.ReasonPhrase(result.StatusCode);
                return result;
            }
            catch (RequestCancelledException)
            {
                handle.Dispose();
                Interrupt.Check();
                throw new RequestConnectionException("Connection aborted");
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        private static string QueryString(IntPtr request, uint infoLevel)
        {
            uint size = 0;
            WinHttpNative.WinHttpQueryHeaders(request, infoLevel, null, null, ref size, IntPtr.Zero);
            int error = Marshal.GetLastWin32Error();
            if (error != WinHttpNative.ERROR_INSUFFICIENT_BUFFER || size == 0)
                return null;
            var sb = new StringBuilder((int)(size / 2) + 1);
            if (!WinHttpNative.WinHttpQueryHeaders(request, infoLevel, null, sb, ref size, IntPtr.Zero))
                return null;
            return sb.ToString();
        }
    }

    /// <summary>Standard HTTP reason phrases (HTTP/2 does not transmit them).</summary>
    public static class HttpStatus
    {
        public static string ReasonPhrase(int code)
        {
            switch (code)
            {
                case 100: return "Continue";
                case 101: return "Switching Protocols";
                case 200: return "OK";
                case 201: return "Created";
                case 202: return "Accepted";
                case 203: return "Non-Authoritative Information";
                case 204: return "No Content";
                case 205: return "Reset Content";
                case 206: return "Partial Content";
                case 300: return "Multiple Choices";
                case 301: return "Moved Permanently";
                case 302: return "Found";
                case 303: return "See Other";
                case 304: return "Not Modified";
                case 307: return "Temporary Redirect";
                case 308: return "Permanent Redirect";
                case 400: return "Bad Request";
                case 401: return "Unauthorized";
                case 402: return "Payment Required";
                case 403: return "Forbidden";
                case 404: return "Not Found";
                case 405: return "Method Not Allowed";
                case 406: return "Not Acceptable";
                case 408: return "Request Timeout";
                case 409: return "Conflict";
                case 410: return "Gone";
                case 411: return "Length Required";
                case 412: return "Precondition Failed";
                case 413: return "Content Too Large";
                case 414: return "URI Too Long";
                case 415: return "Unsupported Media Type";
                case 416: return "Range Not Satisfiable";
                case 429: return "Too Many Requests";
                case 500: return "Internal Server Error";
                case 501: return "Not Implemented";
                case 502: return "Bad Gateway";
                case 503: return "Service Unavailable";
                case 504: return "Gateway Timeout";
            }
            return "";
        }
    }
}

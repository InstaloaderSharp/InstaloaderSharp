using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using InstaloaderSharp.Http;
using InstaloaderSharp.Json;
using InstaloaderSharp.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InstaloaderSharp.Tests
{
    /// <summary>Tests of the WinHTTP-based HTTP layer against a local HTTP server (no internet access needed).</summary>
    [TestClass]
    public class HttpTests
    {
        private static HttpListener _listener;
        private static string _base;
        private static readonly List<RecordedRequest> Requests = new List<RecordedRequest>();

        /// <summary>A copy of a received request (<see cref="HttpListenerRequest"/> is disposed with its response).</summary>
        private sealed class RecordedRequest
        {
            public string Path;
            public string Method;
            public NameValueCollection Headers;
            public string Body;
        }

        [ClassInitialize]
        public static void StartServer(TestContext context)
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            _base = "http://127.0.0.1:" + port + "/";
            _listener = new HttpListener();
            _listener.Prefixes.Add(_base);
            _listener.Start();
            new Thread(Serve) { IsBackground = true }.Start();
        }

        [ClassCleanup]
        public static void StopServer()
        {
            _listener.Close();
        }

        private static void Serve()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = _listener.GetContext();
                }
                catch (HttpListenerException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                string path = ctx.Request.Url.AbsolutePath;
                var recorded = new RecordedRequest
                {
                    Path = path,
                    Method = ctx.Request.HttpMethod,
                    Headers = new NameValueCollection(ctx.Request.Headers),
                    Body = ctx.Request.HasEntityBody ? new StreamReader(ctx.Request.InputStream).ReadToEnd() : null
                };
                lock (Requests)
                    Requests.Add(recorded);
                HttpListenerResponse resp = ctx.Response;
                switch (path)
                {
                    case "/redirect":
                        resp.StatusCode = 302;
                        resp.AddHeader("Location", "/target/user.");
                        resp.AppendHeader("Set-Cookie", "mid=XYZ; Path=/");
                        resp.Close();
                        break;
                    case "/missing":
                        resp.StatusCode = 404;
                        resp.Close();
                        break;
                    case "/slow":
                        Thread.Sleep(5000);
                        resp.Close();
                        break;
                    default:
                        {
                            byte[] data = Encoding.UTF8.GetBytes("{\"status\": \"ok\", \"path\": \"" + path + "\", \"text\": \"caf\\u00e9\"}");
                            var ms = new MemoryStream();
                            using (var gz = new GZipStream(ms, CompressionMode.Compress, true))
                                gz.Write(data, 0, data.Length);
                            resp.AddHeader("Content-Encoding", "gzip");
                            resp.ContentType = "application/json; charset=utf-8";
                            byte[] body = ms.ToArray();
                            resp.OutputStream.Write(body, 0, body.Length);
                            resp.Close();
                            break;
                        }
                }
            }
        }

        private static RecordedRequest LastRequest(string path)
        {
            lock (Requests)
                return Requests.FindLast(r => r.Path == path);
        }

        [TestMethod]
        public void SendsSessionHeadersIncludingEmptyOnes()
        {
            var session = new Session();
            session.Headers["User-Agent"] = "TestAgent/1.0";
            session.Headers["X-Empty"] = "";
            session.Headers["X-Full"] = "1";
            Response response = session.Get(_base + "headers");
            Assert.AreEqual(200, response.StatusCode);
            RecordedRequest request = LastRequest("/headers");
            Assert.AreEqual("TestAgent/1.0", request.Headers["User-Agent"]);
            Assert.AreEqual("", request.Headers["X-Empty"]);
            Assert.AreEqual("1", request.Headers["X-Full"]);
        }

        [TestMethod]
        public void FollowsRedirectsAndKeepsCookies()
        {
            var session = new Session();
            session.Cookies.Set("sessionid", "abc");
            Response response = session.Get(_base + "redirect");
            Assert.AreEqual(200, response.StatusCode);
            Assert.AreEqual(1, response.History.Count);
            Assert.AreEqual(_base + "target/user.", response.Url, "trailing dot of the path is kept");
            Assert.AreEqual("XYZ", session.Cookies.Get("mid"));
            string cookie = LastRequest("/target/user.").Headers["Cookie"];
            StringAssert.Contains(cookie, "sessionid=abc");
            StringAssert.Contains(cookie, "mid=XYZ");
        }

        [TestMethod]
        public void DecodesGzipAndJson()
        {
            JToken json = new Session().Get(_base + "json").Json();
            Assert.AreEqual("ok", (string)json["status"]);
            Assert.AreEqual("caf" + (char)0xE9, (string)json["text"]);
        }

        [TestMethod]
        public void PostsFormData()
        {
            new Session().Post(_base + "post", new JObject { { "enc_password", "#PWD:0:1:p@ss word&" }, { "username", "me" } });
            RecordedRequest request = LastRequest("/post");
            Assert.AreEqual("POST", request.Method);
            Assert.AreEqual("application/x-www-form-urlencoded", request.Headers["Content-Type"]);
            Assert.AreEqual("enc_password=%23PWD%3A0%3A1%3Ap%40ss+word%26&username=me", request.Body);
        }

        [TestMethod]
        public void ReportsStatusCodes()
        {
            Response response = new Session().Get(_base + "missing");
            Assert.AreEqual(404, response.StatusCode);
            Assert.AreEqual("Not Found", response.Reason);
        }

        [TestMethod]
        [ExpectedException(typeof(RequestConnectionException))]
        public void ConnectionErrorsRaiseRequestExceptions()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            new Session().Get("http://127.0.0.1:" + port + "/");
        }

        [TestMethod]
        public void InterruptAbortsSleepAndRequests()
        {
            var timer = Stopwatch.StartNew();
            new Thread(() =>
            {
                Thread.Sleep(300);
                Interrupt.Raise();
            }) { IsBackground = true }.Start();
            try
            {
                new Session().Get(_base + "slow");
                Assert.Fail();
            }
            catch (KeyboardInterruptException)
            {
            }
            Assert.IsTrue(timer.ElapsedMilliseconds < 4000, "the request should be aborted");

            timer.Restart();
            new Thread(() =>
            {
                Thread.Sleep(300);
                Interrupt.Raise();
            }) { IsBackground = true }.Start();
            try
            {
                Interrupt.Sleep(10);
                Assert.Fail();
            }
            catch (KeyboardInterruptException)
            {
            }
            Assert.IsTrue(timer.ElapsedMilliseconds < 4000, "the sleep should be interrupted");
            Assert.IsFalse(Interrupt.IsPending, "the interruption is consumed by the exception");
        }
    }
}

using System.Collections.Generic;
using InstaloaderSharp.Http;
using InstaloaderSharp.Json;
using InstaloaderSharp.Python;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InstaloaderSharp.Tests
{
    [TestClass]
    public class CookieJarTests
    {
        private static readonly ParsedUrl Www = ParsedUrl.Parse("https://www.instagram.com/api/v1/x/");

        [TestMethod]
        public void CookiesWithoutDomainAreSentEverywhere()
        {
            CookieJar jar = CookieJar.FromDict(new Dictionary<string, string> { { "sessionid", "" }, { "ig_pr", "1" } });
            Assert.AreEqual("ig_pr=1; sessionid=", jar.GetCookieHeader(ParsedUrl.Parse("https://scontent.cdninstagram.com/a.jpg")));
        }

        [TestMethod]
        public void SetCookieHeadersAreParsedLikeHttpCookiejar()
        {
            var jar = new CookieJar();
            jar.ExtractCookies(Www, new[]
            {
                "csrftoken=abc; Domain=.instagram.com; Path=/; Secure; Max-Age=31449600",
                "rur=\"NHA\\05412345\"; Domain=instagram.com; Path=/; HttpOnly",
                "hostonly=1",
                "other=x; Domain=.example.com"
            });
            Assert.AreEqual("abc", jar.Get("csrftoken"));
            Assert.AreEqual("\"NHA\\05412345\"", jar.Get("rur"));
            Assert.IsNull(jar.Get("other"), "cookie for a foreign domain must be rejected");
            string header = jar.GetCookieHeader(ParsedUrl.Parse("https://i.instagram.com/"));
            StringAssert.Contains(header, "csrftoken=abc");
            Assert.IsFalse(header.Contains("hostonly"), "host-only cookie must not be sent to other hosts");
            string httpHeader = jar.GetCookieHeader(ParsedUrl.Parse("http://www.instagram.com/"));
            Assert.IsTrue(httpHeader == null || !httpHeader.Contains("csrftoken"), "secure cookie must not be sent over http");
        }

        [TestMethod]
        public void ExpiredSetCookieDeletesCookie()
        {
            var jar = new CookieJar();
            jar.ExtractCookies(Www, new[] { "sessionid=abc; Domain=.instagram.com; Path=/" });
            jar.ExtractCookies(Www, new[] { "sessionid=\"\"; Domain=.instagram.com; expires=Thu, 01-Jan-1970 00:00:00 GMT; Max-Age=0; Path=/" });
            Assert.IsNull(jar.Get("sessionid"));
        }

        [TestMethod]
        public void GetDictPrefersCookiesOfLaterDomains()
        {
            // As requests' dict_from_cookiejar: iteration is sorted by domain, later values win
            var jar = new CookieJar();
            jar.Set("csrftoken", "");
            jar.ExtractCookies(Www, new[] { "csrftoken=real; Domain=.instagram.com; Path=/" });
            Assert.AreEqual("real", jar.ToDictionary()["csrftoken"]);
        }

        [TestMethod]
        public void IndexerRaisesKeyErrorForMissingOrEmptyCookies()
        {
            var jar = new CookieJar();
            jar.Set("empty", "");
            foreach (string name in new[] { "missing", "empty" })
            {
                try
                {
                    string unused = jar[name];
                    Assert.Fail(name);
                }
                catch (PyKeyError)
                {
                }
            }
        }

        [TestMethod]
        public void HttpDates()
        {
            Assert.AreEqual(new System.DateTime(2015, 10, 21, 7, 28, 0, System.DateTimeKind.Utc),
                            CookieJar.ParseHttpDate("Wed, 21 Oct 2015 07:28:00 GMT"));
            Assert.AreEqual(new System.DateTime(2015, 10, 21, 7, 28, 0, System.DateTimeKind.Utc),
                            CookieJar.ParseHttpDate("Wednesday, 21-Oct-15 07:28:00 GMT"));
            Assert.IsNull(CookieJar.ParseHttpDate("not a date"));
        }
    }

    [TestClass]
    public class UrlTests
    {
        [TestMethod]
        public void QuotingLikeUrllib()
        {
            Assert.AreEqual("%23PWD_INSTAGRAM_BROWSER%3A0%3A1%3Ap%40ss+word%26", UrlUtil.QuotePlus("#PWD_INSTAGRAM_BROWSER:0:1:p@ss word&"));
            Assert.AreEqual("https%3A//www.instagram.com/x/", UrlUtil.Quote("https://www.instagram.com/x/"));
            Assert.AreEqual("a b/c", UrlUtil.Unquote("a%20b%2Fc"));
        }

        [TestMethod]
        public void ParametersAreEncodedLikeRequests()
        {
            var parameters = new JObject
            {
                { "query_hash", "abc" },
                { "variables", "{\"id\":\"1\"}" },
                { "flag", true },
                { "n", 12 },
                { "skipped", JValue.Null }
            };
            Assert.AreEqual("https://www.instagram.com/graphql/query?query_hash=abc&variables=%7B%22id%22%3A%221%22%7D&flag=True&n=12",
                            UrlUtil.PrepareUrl("https://www.instagram.com/graphql/query", parameters));
            Assert.AreEqual("https://i.instagram.com/api/v1/feed/reels_media/?reel_ids=highlight:1&a=1",
                            UrlUtil.PrepareUrl("https://i.instagram.com/api/v1/feed/reels_media/?reel_ids=highlight:1", new JObject { { "a", 1 } }));
        }

        [TestMethod]
        public void UrlsKeepTrailingDotsAndAreRequoted()
        {
            // System.Uri would remove the dot of a username like "user."
            Assert.AreEqual("/user./", ParsedUrl.Parse("https://www.instagram.com/user./").Path);
            Assert.AreEqual("https://example.com/a%20b?c=%7Cd", UrlUtil.RequoteUri("https://example.com/a b?c=|d"));
            Assert.AreEqual("https://example.com/~x", UrlUtil.RequoteUri("https://example.com/%7Ex"));
        }

        [TestMethod]
        public void RedirectsAreResolved()
        {
            Assert.AreEqual("https://www.instagram.com/accounts/login/", UrlUtil.Resolve("https://www.instagram.com/x/y", "/accounts/login/"));
            Assert.AreEqual("https://www.instagram.com/x/z", UrlUtil.Resolve("https://www.instagram.com/x/y", "z"));
            Assert.AreEqual("https://other.com/", UrlUtil.Resolve("https://www.instagram.com/x/y", "https://other.com/"));
        }
    }
}

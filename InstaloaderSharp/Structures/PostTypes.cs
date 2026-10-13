using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using InstaloaderSharp.Json;
using InstaloaderSharp.Python;

namespace InstaloaderSharp
{
    /// <summary>Item of a Sidecar Post.</summary>
    public sealed class PostSidecarNode : IPyObject
    {
        public PostSidecarNode(bool isVideo, string displayUrl, string videoUrl)
        {
            IsVideo = isVideo;
            DisplayUrl = displayUrl;
            VideoUrl = videoUrl;
        }

        /// <summary>Whether this node is a video.</summary>
        public bool IsVideo { get; private set; }

        /// <summary>URL of image or video thumbnail.</summary>
        public string DisplayUrl { get; private set; }

        /// <summary>URL of video or null.</summary>
        public string VideoUrl { get; private set; }

        public string PyStr() { return PyRepr(); }

        public string PyRepr()
        {
            return string.Format("PostSidecarNode(is_video={0}, display_url={1}, video_url={2})",
                                 Python.PyStr.Repr(IsVideo), Python.PyStr.Repr(DisplayUrl), Python.PyStr.Repr(VideoUrl));
        }

        public override string ToString() { return PyRepr(); }
    }

    /// <summary>Answer to a comment of a post.</summary>
    public sealed class PostCommentAnswer : IPyObject
    {
        public PostCommentAnswer(long id, DateTime createdAtUtc, string text, Profile owner, long likesCount)
        {
            Id = id;
            CreatedAtUtc = createdAtUtc;
            Text = text;
            Owner = owner;
            LikesCount = likesCount;
        }

        /// <summary>ID number of comment.</summary>
        public long Id { get; private set; }

        /// <summary>When comment was created (UTC).</summary>
        public DateTime CreatedAtUtc { get; private set; }

        /// <summary>Comment text.</summary>
        public string Text { get; private set; }

        /// <summary>Owner Profile of the comment.</summary>
        public Profile Owner { get; private set; }

        /// <summary>Number of likes on comment.</summary>
        public long LikesCount { get; private set; }

        public string PyStr() { return PyRepr(); }

        public string PyRepr()
        {
            return string.Format("PostCommentAnswer(id={0}, created_at_utc={1}, text={2}, owner={3}, likes_count={4})",
                                 Id, Python.PyStr.Repr(CreatedAtUtc), Python.PyStr.Repr(Text), Python.PyStr.Repr(Owner),
                                 LikesCount);
        }

        public override string ToString() { return PyRepr(); }
    }

    /// <summary>A comment of a post.</summary>
    public sealed class PostComment : IPyObject
    {
        private readonly InstaloaderContext _context;
        private readonly JObject _node;
        private readonly IEnumerable<PostCommentAnswer> _answers;
        private readonly Post _post;

        public PostComment(InstaloaderContext context, JObject node, IEnumerable<PostCommentAnswer> answers, Post post)
        {
            _context = context;
            _node = node;
            _answers = answers;
            _post = post;
        }

        public static PostComment FromIphoneStruct(InstaloaderContext context, JObject media,
                                                   IEnumerable<PostCommentAnswer> answers, Post post)
        {
            return new PostComment(
                context,
                new JObject
                {
                    { "id", media["pk"].ToBigInteger() },
                    { "created_at", media["created_at"] },
                    { "text", media["text"] },
                    { "edge_liked_by", new JObject { { "count", media["comment_like_count"] } } },
                    { "iphone_struct", media }
                },
                answers,
                post);
        }

        /// <summary>ID number of comment.</summary>
        public long Id { get { return _node["id"].ToInt64(); } }

        /// <summary>When comment was created (UTC).</summary>
        public DateTime CreatedAtUtc
        {
            get { return DateTime.SpecifyKind(PyDateTime.UtcFromTimestamp(_node["created_at"].ToDouble()).Wall, DateTimeKind.Utc); }
        }

        /// <summary>Comment text.</summary>
        public string Text { get { return (string)_node["text"]; } }

        /// <summary>Owner Profile of the comment.</summary>
        public Profile Owner
        {
            get
            {
                if (_node.ContainsKey("iphone_struct"))
                    return Profile.FromIphoneStruct(_context, (JObject)_node["iphone_struct"]["user"]);
                return new Profile(_context, (JObject)_node["owner"]);
            }
        }

        /// <summary>Number of likes on comment.</summary>
        public long LikesCount
        {
            get { return _node.Get("edge_liked_by", new JObject()).Get("count", 0).ToInt64(); }
        }

        /// <summary>Iterator which yields all PostCommentAnswer for the comment.</summary>
        public IEnumerable<PostCommentAnswer> Answers { get { return _answers; } }

        /// <summary>Iterate over all likes of a comment. A Profile instance of each like is yielded.</summary>
        public IEnumerable<Profile> Likes
        {
            get
            {
                if (LikesCount != 0)
                {
                    return new NodeIterator<Profile>(
                        _context,
                        "5f0b1f6281e72053cbc07909c8d154ae",
                        d => (JObject)d["data"]["comment"]["edge_liked_by"],
                        n => new Profile(_context, n),
                        new JObject { { "comment_id", Id } },
                        string.Format("https://www.instagram.com/p/{0}/", _post.Shortcode));
                }
                return new List<Profile>();
            }
        }

        public string PyStr() { return PyRepr(); }

        public string PyRepr()
        {
            return string.Format("<PostComment {0} of {1}>", Id, _post.Shortcode);
        }

        public override string ToString() { return PyRepr(); }
    }

    /// <summary>Location of a post.</summary>
    public sealed class PostLocation : IPyObject
    {
        public PostLocation(long id, string name, string slug, bool? hasPublicPage, JToken lat, JToken lng)
        {
            Id = id;
            Name = name;
            Slug = slug;
            HasPublicPage = hasPublicPage;
            LatToken = lat ?? JValue.Null;
            LngToken = lng ?? JValue.Null;
        }

        /// <summary>ID number of location.</summary>
        public long Id { get; private set; }

        /// <summary>Location name.</summary>
        public string Name { get; private set; }

        /// <summary>URL friendly variant of location name.</summary>
        public string Slug { get; private set; }

        /// <summary>Whether location has a public page.</summary>
        public bool? HasPublicPage { get; private set; }

        /// <summary>Latitude (or null).</summary>
        public double? Lat { get { return (double?)LatToken; } }

        /// <summary>Longitude (or null).</summary>
        public double? Lng { get { return (double?)LngToken; } }

        internal JToken LatToken { get; private set; }
        internal JToken LngToken { get; private set; }

        public JObject AsDict()
        {
            return new JObject
            {
                { "id", Id },
                { "name", Name },
                { "slug", Slug },
                { "has_public_page", HasPublicPage },
                { "lat", LatToken },
                { "lng", LngToken }
            };
        }

        public string PyStr() { return PyRepr(); }

        public string PyRepr()
        {
            return string.Format("PostLocation(id={0}, name={1}, slug={2}, has_public_page={3}, lat={4}, lng={5})",
                                 Id, Python.PyStr.Repr(Name), Python.PyStr.Repr(Slug), Python.PyStr.Repr(HasPublicPage),
                                 Python.PyStr.Repr(LatToken), Python.PyStr.Repr(LngToken));
        }

        public override string ToString() { return PyRepr(); }
    }

    /// <summary>Helpers shared by the structures.</summary>
    internal static class StructureHelpers
    {
        // This regular expression is by MiguelX413
        internal static readonly Regex HashtagRegex = new Regex(@"(?:#)((?:[\p{L}\p{N}_]){1,150})");

        // This regular expression is modified from jStassen, adjusted to use Python's \w to
        // support Unicode and a word/beginning of string delimiter at the beginning to ensure
        // that no email addresses join the list of mentions. (Compiled with re.ASCII in Python.)
        // http://blog.jstassen.com/2016/03/code-regex-for-instagram-username-and-hashtags/
        internal static readonly Regex MentionRegex = new Regex(
            @"(?:^|[^a-zA-Z0-9_]|_)(?:@)([a-zA-Z0-9_](?:(?:[a-zA-Z0-9_]|(?:\.(?!\.))){0,28}(?:[a-zA-Z0-9_]))?)");

        private static readonly Regex SeParamRegex = new Regex(@"([?&])se=\d+&?");

        internal static List<string> FindAll(Regex regex, string text)
        {
            var result = new List<string>();
            foreach (Match m in regex.Matches(text))
                result.Add(m.Groups[1].Value);
            return result;
        }

        internal static string OptionalNormalize(string s)
        {
            return s == null ? null : s.Normalize(NormalizationForm.FormC);
        }

        /// <summary>re.sub(r'([?&])se=\d+&?', r'\1', url).rstrip('&')</summary>
        internal static string StripSeParam(string url)
        {
            return SeParamRegex.Replace(url, "$1").TrimEnd('&');
        }

        /// <summary>The "printable caption": single line, '/' replaced, elliptified after 30 characters.</summary>
        internal static string Elliptify(string caption)
        {
            var lines = new List<string>();
            foreach (string s in PyStr.SplitLines(caption))
                if (s.Length > 0)
                    lines.Add(s.Replace("/", Chars.DivisionSlash));
            string pcaption = PyStr.Strip(string.Join(" ", lines));
            return PyStr.Len(pcaption) > 31 ? PyStr.Slice(pcaption, null, 30) + Chars.Ellipsis : pcaption;
        }

        internal static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>datetime.utcfromtimestamp(ts), as UTC DateTime.</summary>
        internal static DateTime UtcFromTimestamp(JToken ts)
        {
            return DateTime.SpecifyKind(PyDateTime.UtcFromTimestamp(ts.ToDouble()).Wall, DateTimeKind.Utc);
        }

        /// <summary>datetime.fromtimestamp(ts).astimezone(), as DateTimeOffset.</summary>
        internal static DateTimeOffset LocalFromTimestamp(JToken ts)
        {
            return PyDateTime.LocalFromTimestamp(ts.ToDouble());
        }

        /// <summary>datetime.fromtimestamp(ts) (naive local time), as local DateTime.</summary>
        internal static DateTime NaiveLocalFromTimestamp(JToken ts)
        {
            return DateTime.SpecifyKind(PyDateTime.FromTimestamp(ts.ToDouble()).Wall, DateTimeKind.Local);
        }

        /// <summary>Python's dict.fromkeys(list): de-duplicates, preserving order.</summary>
        internal static List<string> Dedupe(List<string> items)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<string>();
            foreach (string item in items)
                if (seen.Add(item))
                    result.Add(item);
            return result;
        }

        /// <summary>
        /// Selects the video URL with the largest Content-Length among the candidates (Post.video_url logic).
        /// </summary>
        internal static string BestVideoUrl(InstaloaderContext context, List<string> versionUrls, object owner)
        {
            versionUrls = Dedupe(versionUrls);
            if (versionUrls.Count == 0)
                return null;
            if (versionUrls.Count == 1)
                return versionUrls[0];
            var candidates = new List<KeyValuePair<BigInteger, string>>();
            for (int idx = 0; idx < versionUrls.Count; idx++)
            {
                string versionUrl = versionUrls[idx];
                try
                {
                    var resp = context.Head(versionUrl, allowRedirects: true);
                    string length = resp.Headers.Get("Content-Length") ?? "0";
                    candidates.Add(new KeyValuePair<BigInteger, string>(PyInt.Parse(length), versionUrl));
                }
                catch (Exception err) when (err is InstaloaderException || err is PyKeyError || err is PyIndexError)
                {
                    context.Error(string.Format("Video URL candidate {0}/{1} for {2}: {3}", idx + 1, versionUrls.Count,
                                                PyStr.Str(owner), err.Message));
                }
            }
            if (candidates.Count == 0)
                // All candidates fail: Fallback to default URL and handle errors later at the actual download attempt
                return versionUrls[0];
            candidates.Sort((a, b) =>
            {
                int c = a.Key.CompareTo(b.Key);
                return c != 0 ? c : PyStr.CompareCodePoints(a.Value, b.Value);
            });
            return candidates[candidates.Count - 1].Value;
        }

        internal static string MediaTypeName(JToken mediaType, bool strict)
        {
            JTokenType t = mediaType == null ? JTokenType.Null : mediaType.Type;
            if (t == JTokenType.Integer || t == JTokenType.Boolean)
            {
                switch ((long)mediaType.ToBigInteger())
                {
                    case 1: return "GraphImage";
                    case 2: return "GraphVideo";
                    case 8: return "GraphSidecar";
                }
            }
            if (strict)
                throw new PyKeyError((object)mediaType ?? JValue.Null);
            return null;
        }
    }

    /// <summary>Special characters used for file names and captions.</summary>
    internal static class Chars
    {
        /// <summary>U+2215 DIVISION SLASH</summary>
        public static readonly string DivisionSlash = ((char)0x2215).ToString();

        /// <summary>U+2026 HORIZONTAL ELLIPSIS</summary>
        public static readonly string Ellipsis = ((char)0x2026).ToString();

        /// <summary>U+2024 ONE DOT LEADER</summary>
        public static readonly string OneDotLeader = ((char)0x2024).ToString();
    }
}

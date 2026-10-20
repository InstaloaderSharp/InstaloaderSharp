using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using InstaloaderSharp.Compression;
using InstaloaderSharp.Json;
using InstaloaderSharp.Python;

namespace InstaloaderSharp
{
    /// <summary>
    /// An Hashtag.
    ///
    /// Analogous to <see cref="Profile"/>, get an instance with <c>Hashtag.FromName(L.Context, HASHTAG)</c>.
    /// To then download the Hashtag's Posts, iterate over <see cref="GetPosts"/>.
    ///
    /// Also, this class implements == and is hashable.
    /// </summary>
    public class Hashtag : IJsonExportable, IPyObject
    {
        private readonly InstaloaderContext _context;
        private JObject _node;
        private bool _hasFullMetadata;

        public Hashtag(InstaloaderContext context, JObject node)
        {
            if (!node.ContainsKey("name"))
                throw new ArgumentException("Hashtag node must contain 'name'");
            _context = context;
            _node = node;
            _hasFullMetadata = false;
        }

        public InstaloaderContext Context { get { return _context; } }

        /// <summary>The raw node structure.</summary>
        public JObject Node { get { return _node; } }

        /// <summary>
        /// Create a Hashtag instance from a given hashtag name, without preceding '#'. Raises an Exception if there is
        /// no hashtag with the given name.
        /// </summary>
        /// <exception cref="QueryReturnedNotFoundException">If the hashtag does not exist.</exception>
        public static Hashtag FromName(InstaloaderContext context, string name)
        {
            var hashtag = new Hashtag(context, new JObject { { "name", Python.PyStr.Lower(name) } });
            hashtag.ObtainMetadata();
            return hashtag;
        }

        /// <summary>Hashtag name lowercased, without preceding '#'</summary>
        public string Name { get { return Python.PyStr.Lower((string)_node["name"]); } }

        private JObject Query(JObject parameters)
        {
            JObject p = parameters.Copy();
            p["tag_name"] = Name;
            JObject jsonResponse = _context.GetIphoneJson("api/v1/tags/web_info/", p);
            return jsonResponse.ContainsKey("graphql")
                ? (JObject)jsonResponse["graphql"]["hashtag"]
                : (JObject)jsonResponse["data"];
        }

        private void ObtainMetadata()
        {
            if (!_hasFullMetadata)
            {
                _node = Query(new JObject { { "__a", 1 }, { "__d", "dis" } });
                _hasFullMetadata = true;
            }
        }

        public string NodeType { get { return "Hashtag"; } }

        public JObject AsDict()
        {
            JObject jsonNode = _node.Copy();
            // remove posts
            jsonNode.Pop("edge_hashtag_to_top_posts");
            jsonNode.Pop("top");
            jsonNode.Pop("edge_hashtag_to_media");
            jsonNode.Pop("recent");
            return jsonNode;
        }

        public string PyStr() { return PyRepr(); }

        public string PyRepr() { return string.Format("<Hashtag #{0}>", Name); }

        public override string ToString() { return PyRepr(); }

        public override bool Equals(object obj)
        {
            var other = obj as Hashtag;
            return other != null && Name == other.Name;
        }

        public override int GetHashCode()
        {
            return Name.GetHashCode();
        }

        private JToken Metadata(params string[] keys)
        {
            try
            {
                JToken d = _node;
                foreach (string key in keys)
                    d = d[key];
                return d;
            }
            catch (PyKeyError)
            {
                ObtainMetadata();
                JToken d = _node;
                foreach (string key in keys)
                    d = d[key];
                return d;
            }
        }

        public long Hashtagid { get { return Metadata("id").ToInt64(); } }

        public string ProfilePicUrl { get { return (string)Metadata("profile_pic_url"); } }

        public string Description { get { return (string)Metadata("description"); } }

        public bool AllowFollowing { get { return Metadata("allow_following").Truthy; } }

        public bool IsFollowing
        {
            get
            {
                try
                {
                    return (bool)Metadata("is_following");
                }
                catch (PyKeyError)
                {
                    return Metadata("following").Truthy;
                }
            }
        }

        /// <summary>Yields the top posts of the hashtag.</summary>
        public IEnumerable<Post> GetTopPosts()
        {
            JToken edges;
            try
            {
                edges = Metadata("edge_hashtag_to_top_posts", "edges");
            }
            catch (PyKeyError)
            {
                edges = null;
            }
            if (edges != null)
            {
                foreach (JToken edge in edges.AsIterable())
                    yield return new Post(_context, (JObject)edge["node"]);
                yield break;
            }
            foreach (Post post in new SectionIterator<Post>(
                _context,
                d => (JObject)d["data"]["top"],
                m => Post.FromIphoneStruct(_context, m),
                string.Format("explore/tags/{0}/", Name),
                (JObject)Metadata("top")))
                yield return post;
        }

        /// <summary>
        /// The count of all media associated with this hashtag.
        ///
        /// The number of posts with a certain hashtag may differ from the number of posts that can actually be
        /// accessed, as the hashtag count might include private posts
        /// </summary>
        public long? Mediacount
        {
            get
            {
                try
                {
                    return (long?)Metadata("edge_hashtag_to_media", "count");
                }
                catch (PyKeyError)
                {
                    return (long?)Metadata("media_count");
                }
            }
        }

        /// <summary>
        /// Yields the recent posts associated with this hashtag.
        ///
        /// Deprecated since 4.9: Use <see cref="GetPostsResumable"/> as this method may return incorrect results
        /// (issue #1457).
        /// </summary>
        public IEnumerable<Post> GetPosts()
        {
            JToken conn = null;
            try
            {
                Metadata("edge_hashtag_to_media", "edges");
                Metadata("edge_hashtag_to_media", "page_info");
                conn = Metadata("edge_hashtag_to_media");
            }
            catch (PyKeyError)
            {
                conn = null;
            }
            if (conn != null)
            {
                foreach (JToken edge in conn["edges"].AsIterable())
                    yield return new Post(_context, (JObject)edge["node"]);
                while (conn["page_info"]["has_next_page"].Truthy)
                {
                    JObject data = Query(new JObject { { "__a", 1 }, { "max_id", conn["page_info"]["end_cursor"] } });
                    conn = data["edge_hashtag_to_media"];
                    foreach (JToken edge in conn["edges"].AsIterable())
                        yield return new Post(_context, (JObject)edge["node"]);
                }
                yield break;
            }
            foreach (Post post in new SectionIterator<Post>(
                _context,
                d => (JObject)d["data"]["recent"],
                m => Post.FromIphoneStruct(_context, m),
                string.Format("explore/tags/{0}/", Name),
                (JObject)Metadata("recent")))
                yield return post;
        }

        /// <summary>Yields all posts, i.e. all most recent posts and the top posts, in almost-chronological order.</summary>
        public IEnumerable<Post> GetAllPosts()
        {
            List<Post> top = GetTopPosts().Take(9).ToList();
            // sorted(..., key=date_utc, reverse=True) is stable
            List<Post> sortedTop = top.Select((p, i) => new KeyValuePair<int, Post>(i, p))
                                      .OrderByDescending(kv => kv.Value.DateUtc)
                                      .ThenBy(kv => kv.Key)
                                      .Select(kv => kv.Value)
                                      .ToList();
            IEnumerator<Post> sortedTopPosts = sortedTop.GetEnumerator();
            IEnumerator<Post> otherPosts = ((IEnumerable<Post>)GetPostsResumable()).GetEnumerator();
            Func<IEnumerator<Post>, Post> next = e => e.MoveNext() ? e.Current : null;
            Post nextTop = next(sortedTopPosts);
            Post nextOther = next(otherPosts);
            while (nextTop != null || nextOther != null)
            {
                if (nextOther == null)
                {
                    yield return nextTop;
                    while (sortedTopPosts.MoveNext())
                        yield return sortedTopPosts.Current;
                    break;
                }
                if (nextTop == null)
                {
                    yield return nextOther;
                    while (otherPosts.MoveNext())
                        yield return otherPosts.Current;
                    break;
                }
                if (nextTop.Equals(nextOther))
                {
                    yield return nextTop;
                    nextTop = next(sortedTopPosts);
                    nextOther = next(otherPosts);
                    continue;
                }
                if (nextTop.DateUtc > nextOther.DateUtc)
                {
                    yield return nextTop;
                    nextTop = next(sortedTopPosts);
                }
                else
                {
                    yield return nextOther;
                    nextOther = next(otherPosts);
                }
            }
        }

        /// <summary>Get the recent posts of the hashtag in a resumable fashion.</summary>
        public NodeIterator<Post> GetPostsResumable()
        {
            return new NodeIterator<Post>(
                _context, "9b498c08113f1e09617a1703c22b2f32",
                d => (JObject)d["data"]["hashtag"]["edge_hashtag_to_media"],
                n => new Post(_context, n),
                new JObject { { "tag_name", Name } },
                string.Format("https://www.instagram.com/explore/tags/{0}/", Name));
        }
    }

    /// <summary>
    /// An invocation of this class triggers a search on Instagram for the provided search string.
    ///
    /// Provides methods to access the search results as profiles (Profile), locations (PostLocation) and
    /// hashtags.
    /// </summary>
    public class TopSearchResults
    {
        private readonly InstaloaderContext _context;
        private readonly string _searchstring;
        private readonly JObject _node;
        private bool _placesSearched;

        /// <param name="context">InstaloaderContext used to send the query for the search. Must be logged in.</param>
        /// <param name="searchstring">String to search for with Instagram's "top search".</param>
        /// <exception cref="LoginRequiredException">If the context is not logged in.</exception>
        public TopSearchResults(InstaloaderContext context, string searchstring)
        {
            _context = context;
            _searchstring = searchstring;
            // Instagram has retired the website's top search, web/search/topsearch/, which Instaloader 4.15.4 uses
            // (404). The search of Instagram's app answers logged-in sessions only, with a list of users and
            // hashtags; places have a search of their own, which GetLocations() sends when it is first used.
            if (!context.IsLoggedIn)
                throw new LoginRequiredException("Searching Instagram requires login.");
            JObject result = context.GetIphoneJson("api/v1/fbsearch/topsearch_flat/",
                                                   new JObject
                                                   {
                                                       { "query", searchstring },
                                                       { "context", "blended" },
                                                       { "search_surface", "top_search_page" }
                                                   });
            // Sort the entries into the lists of the former top search, which the methods below read.
            var users = new JArray();
            var hashtags = new JArray();
            var places = new JArray();
            foreach (JToken entry in result.Get("list", new JArray()).AsIterable())
            {
                if (entry.Get("user") is JObject)
                {
                    users.Add(entry);
                }
                else if (entry.Get("hashtag") is JObject)
                {
                    // Hashtag.Mediacount reads the count from here; without it, it loads the hashtag's page.
                    var hashtag = (JObject)entry["hashtag"];
                    if (!hashtag.ContainsKey("edge_hashtag_to_media") && hashtag.ContainsKey("media_count"))
                        hashtag["edge_hashtag_to_media"] = new JObject { { "count", hashtag["media_count"] } };
                    hashtags.Add(entry);
                }
                else if (entry.Get("place") is JObject)
                {
                    places.Add(entry);
                }
            }
            _node = new JObject { { "users", users }, { "hashtags", hashtags }, { "places", places } };
        }

        /// <summary>
        /// The places of the search. The app's top search returns none, so they are searched separately, once. If
        /// that fails, the error is reported and no places are returned, so that the other results stay usable.
        /// </summary>
        private JArray Places()
        {
            var places = (JArray)_node["places"];
            if (places.Count == 0 && !_placesSearched)
            {
                _placesSearched = true;
                try
                {
                    JObject result = _context.GetIphoneJson("api/v1/fbsearch/places/", new JObject { { "query", _searchstring } });
                    foreach (JToken item in result.Get("items", new JArray()).AsIterable())
                        places.Add(new JObject { { "place", item } });
                }
                catch (ConnectionException err)
                {
                    _context.Error(string.Format("Searching places for \"{0}\" failed: {1}", _searchstring, err.Message));
                }
            }
            return places;
        }

        /// <summary>Provides the Profile instances from the search result.</summary>
        public IEnumerable<Profile> GetProfiles()
        {
            foreach (JToken user in _node.Get("users", new JArray()).AsIterable())
            {
                JObject userNode = (JObject)user["user"];
                if (userNode.ContainsKey("pk"))
                    userNode["id"] = userNode["pk"];
                yield return new Profile(_context, userNode);
            }
        }

        /// <summary>Provides all profile names from the search result that start with the search string.</summary>
        public IEnumerable<string> GetPrefixedUsernames()
        {
            foreach (JToken user in _node.Get("users", new JArray()).AsIterable())
            {
                string username = (string)user.Get("user", new JObject()).Get("username", "");
                if (username.StartsWith(_searchstring, StringComparison.Ordinal))
                    yield return username;
            }
        }

        /// <summary>Provides instances of PostLocation from the search result.</summary>
        public IEnumerable<PostLocation> GetLocations()
        {
            foreach (JToken location in Places())
            {
                JToken place = location.Get("place", new JObject());
                string slug = (string)(place.Get("slug") ?? JValue.Null);
                JToken loc = place.Get("location", new JObject());
                yield return new PostLocation(loc["pk"].ToInt64(), (string)loc["name"], slug, null,
                                              loc.Get("lat") ?? JValue.Null, loc.Get("lng") ?? JValue.Null);
            }
        }

        /// <summary>Provides the hashtags from the search result as strings.</summary>
        public IEnumerable<string> GetHashtagStrings()
        {
            foreach (JToken hashtag in _node.Get("hashtags", new JArray()).AsIterable())
            {
                JToken name = hashtag.Get("hashtag", new JObject()).Get("name");
                if (name != null && name.Truthy)
                    yield return (string)name;
            }
        }

        /// <summary>Provides the hashtags from the search result.</summary>
        public IEnumerable<Hashtag> GetHashtags()
        {
            foreach (JToken hashtag in _node.Get("hashtags", new JArray()).AsIterable())
            {
                JToken node = hashtag.Get("hashtag", new JObject());
                if (node.ContainsKey("name"))
                    yield return new Hashtag(_context, (JObject)node);
            }
        }

        /// <summary>The string that was searched for on Instagram to produce this TopSearchResults instance.</summary>
        public string Searchstring { get { return _searchstring; } }
    }

    /// <summary>
    /// A download target that is a path (Python's pathlib.Path) rather than a plain string. Unlike a string target,
    /// it is not sanitized when used in a filename pattern.
    /// </summary>
    public sealed class TargetPath : IPyObject
    {
        public TargetPath(params string[] parts)
        {
            var nonEmpty = new List<string>();
            foreach (string p in parts)
                if (!string.IsNullOrEmpty(p))
                    nonEmpty.Add(p);
            Value = nonEmpty.Count == 0 ? "." : PyOs.Join(nonEmpty[0], nonEmpty.Skip(1).ToArray());
        }

        public string Value { get; private set; }

        public string PyStr() { return Value; }

        public string PyRepr()
        {
            return string.Format("{0}('{1}')", PyOs.IsWindows ? "WindowsPath" : "PosixPath", Value.Replace('\\', '/'));
        }

        public override string ToString() { return Value; }

        public override bool Equals(object obj)
        {
            var other = obj as TargetPath;
            return other != null && other.Value == Value;
        }

        public override int GetHashCode() { return Value.GetHashCode(); }
    }

    /// <summary>Pseudo-structure for pictures that are not associated with a Post or StoryItem, used for filenames.</summary>
    public class TitlePic
    {
        private readonly Profile _profile;
        private readonly object _target;
        private readonly string _typename;
        private readonly string _filename;
        private readonly DateTimeOffset? _dateUtc;

        public TitlePic(Profile profile, object target, string typename, string filename, DateTimeOffset? dateUtc)
        {
            _profile = profile;
            _target = target;
            _typename = typename;
            _filename = filename;
            _dateUtc = dateUtc;
        }

        /// <summary>The profile name (string) or the target (string or TargetPath).</summary>
        public object Profile
        {
            get { return _profile != null ? (object)Python.PyStr.Lower(_profile.Username) : _target; }
        }

        public object OwnerUsername { get { return Profile; } }

        public object OwnerId
        {
            get { return _profile != null ? (object)_profile.Userid.ToString(CultureInfo.InvariantCulture) : _target; }
        }

        public object Target { get { return _target; } }

        public string Typename { get { return _typename; } }

        public string Filename { get { return _filename; } }

        public DateTimeOffset? DateUtc { get { return _dateUtc; } }

        public DateTimeOffset? Date { get { return DateUtc; } }

        public DateTimeOffset? DateLocal
        {
            get { return _dateUtc.HasValue ? _dateUtc.Value.ToLocalTime() : (DateTimeOffset?)null; }
        }
    }

    /// <summary>Saving and loading of Instaloader JSON structures (module-level functions of structures.py).</summary>
    public static class Structures
    {
        /// <summary>
        /// Returns Instaloader JSON structure for a Post, Profile, StoryItem, Hashtag or FrozenNodeIterator so that
        /// it can be loaded by <see cref="LoadStructure"/>.
        /// </summary>
        public static JObject GetJsonStructure(IJsonExportable structure)
        {
            return new JObject
            {
                { "node", structure.AsDict() },
                {
                    "instaloader", new JObject
                    {
                        { "version", Instaloader.InstaloaderVersion },
                        { "node_type", structure.NodeType }
                    }
                }
            };
        }

        /// <summary>
        /// Saves a Post, Profile, StoryItem, Hashtag or FrozenNodeIterator to a '.json' or '.json.xz' file such that
        /// it can later be loaded by <see cref="LoadStructureFromFile"/>.
        ///
        /// If the specified filename ends in '.xz', the file will be LZMA compressed. Otherwise, a pretty-printed JSON
        /// file will be created.
        /// </summary>
        public static void SaveStructureToFile(IJsonExportable structure, string filename)
        {
            JObject jsonStructure = GetJsonStructure(structure);
            bool compress = filename.EndsWith(".xz");
            if (compress)
            {
                string json = PyJson.Dumps(jsonStructure, null, ",", ":");
                File.WriteAllBytes(filename, Xz.Compress(Encoding.ASCII.GetBytes(json), XzCheck.None));
            }
            else
            {
                string json = PyJson.Dumps(jsonStructure, 4, null, null, true);
                PyOs.WriteText(filename, json, PyOs.LocaleEncoding);
            }
        }

        /// <summary>
        /// Loads a Post, Profile, StoryItem, Hashtag or FrozenNodeIterator from a json structure.
        /// </summary>
        public static IJsonExportable LoadStructure(InstaloaderContext context, JObject jsonStructure)
        {
            if (jsonStructure.ContainsKey("node") && jsonStructure.ContainsKey("instaloader") &&
                jsonStructure["instaloader"].ContainsKey("node_type"))
            {
                string nodeType = (string)jsonStructure["instaloader"]["node_type"];
                switch (nodeType)
                {
                    case "Post":
                        return new Post(context, (JObject)jsonStructure["node"]);
                    case "Profile":
                        return new Profile(context, (JObject)jsonStructure["node"]);
                    case "StoryItem":
                        return new StoryItem(context, (JObject)jsonStructure["node"]);
                    case "Hashtag":
                        return new Hashtag(context, (JObject)jsonStructure["node"]);
                    case "FrozenNodeIterator":
                        {
                            var node = (JObject)jsonStructure["node"];
                            if (!node.ContainsKey("first_node"))
                                node["first_node"] = JValue.Null;
                            return FrozenNodeIterator.FromDict(node);
                        }
                }
            }
            else if (jsonStructure.ContainsKey("shortcode"))
            {
                // Post JSON created with Instaloader v3
                return Post.FromShortcode(context, (string)jsonStructure["shortcode"]);
            }
            throw new InvalidArgumentException("Passed json structure is not an Instaloader JSON");
        }

        /// <summary>
        /// Loads a Post, Profile, StoryItem, Hashtag or FrozenNodeIterator from a '.json' or '.json.xz' file that has
        /// been saved by <see cref="SaveStructureToFile"/>.
        /// </summary>
        public static IJsonExportable LoadStructureFromFile(InstaloaderContext context, string filename)
        {
            bool compressed = filename.EndsWith(".xz");
            string text;
            if (compressed)
            {
                byte[] data = Xz.DecompressFile(filename);
                text = DecodeText(data);
            }
            else
            {
                text = DecodeText(File.ReadAllBytes(filename));
            }
            JToken jsonStructure = PyJson.Loads(text);
            var obj = jsonStructure as JObject;
            if (obj == null)
                throw new InvalidArgumentException("Passed json structure is not an Instaloader JSON");
            return LoadStructure(context, obj);
        }

        /// <summary>
        /// Decodes the bytes of a JSON file. Instaloader writes pure ASCII; UTF-8 is tried first, then the locale
        /// encoding (which Python uses when reading).
        /// </summary>
        private static string DecodeText(byte[] data)
        {
            try
            {
                return new UTF8Encoding(false, true).GetString(data);
            }
            catch (DecoderFallbackException)
            {
                return PyOs.LocaleEncoding.GetString(data);
            }
        }
    }
}

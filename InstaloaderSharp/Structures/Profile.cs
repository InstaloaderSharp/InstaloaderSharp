using System;
using System.Collections.Generic;
using System.Text;
using InstaloaderSharp.Json;
using InstaloaderSharp.Python;

namespace InstaloaderSharp
{
    /// <summary>
    /// An Instagram Profile.
    ///
    /// Provides methods for accessing profile properties, as well as <see cref="GetPosts"/> and for own profile
    /// <see cref="GetSavedPosts"/>.
    ///
    /// Get instances with <see cref="Post.OwnerProfile"/>, <see cref="StoryItem.OwnerProfile"/>,
    /// <see cref="GetFollowees"/>, <see cref="GetFollowers"/> or
    /// <c>Profile.FromUsername(L.Context, USERNAME)</c>.
    ///
    /// Also, this class implements == and is hashable.
    /// </summary>
    public class Profile : IJsonExportable, IPyObject
    {
        private readonly InstaloaderContext _context;
        private bool? _hasPublicStory;
        private JObject _node;
        private bool _hasFullMetadata;
        private JObject _iphoneStruct;

        public Profile(InstaloaderContext context, JObject node)
        {
            if (!node.ContainsKey("username"))
                throw new ArgumentException("Profile node must contain 'username'");
            if (!node.ContainsKey("id") && !node.ContainsKey("pk"))
                throw new ArgumentException("Profile node must contain 'id' or 'pk'");
            _context = context;
            _hasPublicStory = null;
            _node = node;
            _hasFullMetadata = false;
            _iphoneStruct = null;
            if (node.ContainsKey("iphone_struct"))
                // if loaded from JSON with LoadStructureFromFile()
                _iphoneStruct = node["iphone_struct"] as JObject;
        }

        public InstaloaderContext Context { get { return _context; } }

        /// <summary>The raw node structure.</summary>
        public JObject Node { get { return _node; } }

        /// <summary>Create a Profile instance from a given username, raise exception if it does not exist.</summary>
        /// <exception cref="ProfileNotExistsException">If the profile does not exist.</exception>
        public static Profile FromUsername(InstaloaderContext context, string username)
        {
            // Resolve the profile through its public web page, which Instagram renders with the
            // profile embedded. The web_profile_info endpoint previously used here is refused
            // with HTTP 400 for some professional profiles, and with HTTP 401 or 429 for some
            // sessions, both anonymously and when logged in.
            string path = string.Format("{0}/", Python.PyStr.Lower(username));
            List<JObject> page;
            try
            {
                page = context.GetPageData(path);
            }
            catch (QueryReturnedNotFoundException)
            {
                page = new List<JObject>();
            }
            catch (ConnectionException)
            {
                if (!context.IsLoggedIn)
                    throw;
                page = new List<JObject>();
            }
            // The page embeds two queries for the profile: one for its details and one for its posts.
            var user = new JObject();
            foreach (JObject data in page)
            {
                JToken part = data.Get("xig_user_by_username").Or(new JObject());
                user.Update(part.AsObject());
            }
            if (user.Get("pk").Or(null) != null)
            {
                user.Pop("polaris_ordered_timeline_connection");
                // The page's "id" is not the user ID, which is "pk".
                JObject node = user.Copy();
                node["id"] = user["pk"];
                if (context.IsLoggedIn)
                {
                    // The page shows the profile as a logged-out visitor sees it. Leave the missing
                    // fields, notably the viewer's relationship to the profile, such as
                    // followed_by_viewer, to the GraphQL profile query in ObtainMetadata().
                    return new Profile(context, node);
                }
                // Anonymously, the page is all there is: web_profile_info and the GraphQL profile
                // query refuse logged-out requests, so do not let missing fields fall back to them.
                var profile = new Profile(context, NormalizeProfileData(node));
                profile._hasFullMetadata = true;
                return profile;
            }

            if (context.IsLoggedIn)
            {
                // Instagram does not show restricted profiles, such as age-restricted ones, to logged-out
                // visitors, so the page embeds no profile. Loaded with the session, the page does not embed
                // the profile either, but names its ID. (Instaloader 4.15.4 reports that the profile does not
                // exist. Instagram's top search, which could find the ID too, is answered with 404.)
                long? profileId = context.GetProfileIdFromPage(path);
                if (profileId != null)
                {
                    Profile profile = LoadByIdLoggedIn(context, profileId.Value);
                    if (profile != null && string.Equals(profile.Username, username, StringComparison.OrdinalIgnoreCase))
                        return profile;
                }
            }

            throw new ProfileNotExistsException(string.Format("Profile {0} does not exist.", username));
        }

        /// <summary>
        /// Loads the profile with the given ID with the GraphQL profile query of logged-in sessions, or returns null
        /// if there is no such profile.
        /// </summary>
        private static Profile LoadByIdLoggedIn(InstaloaderContext context, long profileId)
        {
            var profile = new Profile(context, new JObject { { "id", profileId }, { "username", "" } });
            try
            {
                profile.ObtainMetadata();
            }
            catch (ProfileNotExistsException)
            {
                return null;
            }
            return profile.Username.Length > 0 ? profile : null;
        }

        /// <summary>
        /// Create a Profile instance from a given userid. If possible, use <see cref="FromUsername"/> or constructor
        /// directly rather than this method, since it requires more requests.
        /// </summary>
        /// <exception cref="ProfileNotExistsException">If the profile does not exist.</exception>
        public static Profile FromId(InstaloaderContext context, long profileId)
        {
            Profile cached;
            if (context.ProfileIdCache.TryGetValue(profileId, out cached))
                return cached;
            Profile profile;
            if (context.IsLoggedIn)
            {
                // The GraphQL profile query takes the ID. (Instaloader 4.15.4 uses api/v1/users/ID/info/, which
                // Instagram answered with 429 for logged-in sessions in October 2026.)
                profile = LoadByIdLoggedIn(context, profileId);
                if (profile == null)
                    throw new ProfileNotExistsException("No profile found, the user may have blocked you (ID: " +
                                                        profileId + ").");
                context.ProfileIdCache[profileId] = profile;
                return profile;
            }
            // Resolve the current username from the user id, then load the full profile.
            // The GraphQL user query previously used here started responding with HTTP 400.
            JToken user;
            try
            {
                user = context.GetJson(string.Format("api/v1/users/{0}/info/", profileId), new JObject()).Get("user");
            }
            catch (QueryReturnedNotFoundException)
            {
                user = null;
            }
            if (user != null && user.Truthy && user.Get("username").Or(null) != null)
                profile = FromUsername(context, (string)user["username"]);
            else
                throw new ProfileNotExistsException("No profile found, the user may have blocked you (ID: " +
                                                    profileId + ").");
            context.ProfileIdCache[profileId] = profile;
            return profile;
        }

        /// <summary>Create a profile from a given iphone_struct.</summary>
        public static Profile FromIphoneStruct(InstaloaderContext context, JObject media)
        {
            return new Profile(context, new JObject
            {
                { "id", media["pk"] },
                { "username", media["username"] },
                { "is_private", media["is_private"] },
                { "full_name", media["full_name"] },
                { "profile_pic_url_hd", media["profile_pic_url"] },
                { "iphone_struct", media }
            });
        }

        /// <summary>Return own profile if logged-in.</summary>
        public static Profile OwnProfile(InstaloaderContext context)
        {
            if (!context.IsLoggedIn)
                throw new LoginRequiredException("Login required to access own profile.");
            return new Profile(context, (JObject)context.GraphqlQuery("d6f4427fbe92d846298cf93df0b937d3", new JObject())["data"]["user"]);
        }

        public string NodeType { get { return "Profile"; } }

        public JObject AsDict()
        {
            JObject jsonNode = _node.Copy();
            // remove posts to avoid "Circular reference detected" exception
            jsonNode.Pop("edge_media_collections");
            jsonNode.Pop("edge_owner_to_timeline_media");
            jsonNode.Pop("edge_saved_media");
            jsonNode.Pop("edge_felix_video_timeline");
            if (_iphoneStruct != null && _iphoneStruct.Truthy)
                jsonNode["iphone_struct"] = _iphoneStruct;
            return jsonNode;
        }

        internal void ObtainMetadata()
        {
            try
            {
                if (!_hasFullMetadata)
                {
                    if (!_context.IsLoggedIn)
                    {
                        // Anonymous access: the web_profile_info endpoint returns the full
                        // node in the legacy format and still works, unlike the GraphQL
                        // profile query.
                        JToken data;
                        try
                        {
                            data = _context.GetJson("api/v1/users/web_profile_info/",
                                                    new JObject { { "username", Username } }).Get("data");
                        }
                        catch (QueryReturnedNotFoundException err)
                        {
                            throw new ProfileNotExistsException(string.Format("Profile {0} does not exist.", Username), err);
                        }
                        JToken userData = data != null && data.Truthy ? data.Get("user") : null;
                        if (userData.IsNone())
                            throw new ProfileNotExistsException(string.Format("Profile {0} does not exist.", Username));
                        _node = userData.AsObject();
                        _hasFullMetadata = true;
                        return;
                    }
                    JToken userId = _node.Get("id").Or(null) ?? _node.Get("pk");
                    var variables = new JObject
                    {
                        { "id", Python.PyStr.Str(userId) },
                        { "render_surface", "PROFILE" },
                        { "__relay_internal__pv__PolarisCannesGuardianExperienceEnabledrelayprovider", true },
                        { "__relay_internal__pv__PolarisCASB976ProfileEnabledrelayprovider", false },
                        { "__relay_internal__pv__PolarisRepostsConsumptionEnabledrelayprovider", false },
                        { "__relay_internal__pv__PolarisWebSchoolsEnabledrelayprovider", false },
                        { "enable_integrity_filters", true }
                    };
                    JObject result = _context.DocIdGraphqlQuery("27937681195819736", variables);
                    if (result == null)
                        throw new QueryReturnedNotFoundException("GraphQL query returned None");
                    JToken user = result.Get("data", new JObject()).Get("user");
                    if (user.IsNone())
                        throw new ProfileNotExistsException(string.Format("Profile {0} does not exist.", Username));
                    _node = NormalizeProfileData(user.AsObject());
                    _hasFullMetadata = true;
                }
            }
            catch (Exception err) when (err is QueryReturnedNotFoundException || err is PyKeyError)
            {
                // Instaloader 4.15.4 suggests similar profiles from Instagram's top search here, which is answered
                // with 404.
                throw new ProfileNotExistsException(string.Format("Profile {0} does not exist.", Username), err);
            }
        }

        /// <summary>Normalize PolarisProfilePageContentQuery response to match legacy format.</summary>
        private static JObject NormalizeProfileData(JObject userData)
        {
            JObject normalized = userData.Copy();
            if (!normalized.ContainsKey("id") && normalized.ContainsKey("pk"))
                normalized["id"] = normalized["pk"];
            // The public profile page calls the post count all_media_count.
            if (!normalized.ContainsKey("media_count") && normalized.ContainsKey("all_media_count"))
                normalized["media_count"] = normalized["all_media_count"];
            if (!normalized.ContainsKey("edge_owner_to_timeline_media") && normalized.ContainsKey("media_count"))
                normalized["edge_owner_to_timeline_media"] = new JObject { { "count", normalized["media_count"] } };
            if (!normalized.ContainsKey("edge_felix_video_timeline"))
                normalized["edge_felix_video_timeline"] = new JObject { { "count", 0 } };
            if (!normalized.ContainsKey("edge_followed_by") && normalized.ContainsKey("follower_count"))
                normalized["edge_followed_by"] = new JObject { { "count", normalized["follower_count"] } };
            if (!normalized.ContainsKey("edge_follow") && normalized.ContainsKey("following_count"))
                normalized["edge_follow"] = new JObject { { "count", normalized["following_count"] } };
            if (!normalized.ContainsKey("is_business_account") && normalized.ContainsKey("is_business"))
                normalized["is_business_account"] = normalized["is_business"];
            if (!normalized.ContainsKey("business_category_name") && normalized.ContainsKey("category"))
                normalized["business_category_name"] = normalized["category"];
            JToken friendship = normalized.Get("friendship_status", new JObject()).Or(new JObject());
            if (!normalized.ContainsKey("followed_by_viewer"))
                normalized["followed_by_viewer"] = friendship.Get("following", false);
            if (!normalized.ContainsKey("follows_viewer"))
                normalized["follows_viewer"] = friendship.Get("followed_by", false);
            if (!normalized.ContainsKey("blocked_by_viewer"))
                normalized["blocked_by_viewer"] = friendship.Get("blocking", false);
            if (!normalized.ContainsKey("has_blocked_viewer"))
                normalized["has_blocked_viewer"] = false;
            if (!normalized.ContainsKey("has_requested_viewer"))
                normalized["has_requested_viewer"] = friendship.Get("incoming_request", false);
            if (!normalized.ContainsKey("requested_by_viewer"))
                normalized["requested_by_viewer"] = friendship.Get("outgoing_request", false);
            if (!normalized.ContainsKey("profile_pic_url_hd"))
            {
                JToken hdInfo = normalized.Get("hd_profile_pic_url_info");
                if (hdInfo != null && hdInfo.Truthy && hdInfo.ContainsKey("url"))
                    normalized["profile_pic_url_hd"] = hdInfo["url"];
                else if (normalized.ContainsKey("profile_pic_url"))
                    normalized["profile_pic_url_hd"] = normalized["profile_pic_url"];
            }
            return normalized;
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

        private JObject IphoneStruct
        {
            get
            {
                if (!_context.IphoneSupport)
                    throw new IPhoneSupportDisabledException("iPhone support is disabled.");
                if (!_context.IsLoggedIn)
                    throw new LoginRequiredException("Login required to access iPhone profile info endpoint.");
                if (_iphoneStruct == null || !_iphoneStruct.Truthy)
                {
                    JObject data = _context.GetIphoneJson(string.Format("api/v1/users/{0}/info/", Userid), new JObject());
                    _iphoneStruct = (JObject)data["user"];
                }
                return _iphoneStruct;
            }
        }

        /// <summary>User ID</summary>
        public long Userid { get { return Metadata("id").ToInt64(); } }

        /// <summary>Profile Name</summary>
        public string Username { get { return Python.PyStr.Lower((string)Metadata("username")); } }

        public string PyStr() { return PyRepr(); }

        public string PyRepr() { return string.Format("<Profile {0} ({1})>", Username, Userid); }

        public override string ToString() { return PyRepr(); }

        public override bool Equals(object obj)
        {
            var other = obj as Profile;
            return other != null && Userid == other.Userid;
        }

        public override int GetHashCode()
        {
            return Userid.GetHashCode();
        }

        public bool IsPrivate { get { return (bool)Metadata("is_private"); } }

        public bool FollowedByViewer { get { return (bool)Metadata("followed_by_viewer"); } }

        public long? Mediacount { get { return (long?)Metadata("edge_owner_to_timeline_media", "count"); } }

        public long? Igtvcount { get { return (long?)Metadata("edge_felix_video_timeline", "count"); } }

        public long? Followers { get { return (long?)Metadata("edge_followed_by", "count"); } }

        public long? Followees { get { return (long?)Metadata("edge_follow", "count"); } }

        public string ExternalUrl { get { return (string)Metadata("external_url"); } }

        public bool IsBusinessAccount { get { return (bool)Metadata("is_business_account"); } }

        public string BusinessCategoryName { get { return (string)Metadata("business_category_name"); } }

        public string Biography
        {
            get
            {
                JToken bio = Metadata("biography");
                if (bio.Type != JTokenType.String)
                    throw new PyTypeError(string.Format("normalize() argument 2 must be str, not {0}", bio.PyTypeName));
                return ((string)bio).Normalize(NormalizationForm.FormC);
            }
        }

        /// <summary>List of all lowercased hashtags (without preceding #) that occur in the Profile's biography.</summary>
        public List<string> BiographyHashtags
        {
            get
            {
                string bio = Biography;
                if (string.IsNullOrEmpty(bio))
                    return new List<string>();
                return StructureHelpers.FindAll(StructureHelpers.HashtagRegex, Python.PyStr.Lower(bio));
            }
        }

        /// <summary>List of all lowercased profiles that are mentioned in the Profile's biography, without preceding @.</summary>
        public List<string> BiographyMentions
        {
            get
            {
                string bio = Biography;
                if (string.IsNullOrEmpty(bio))
                    return new List<string>();
                return StructureHelpers.FindAll(StructureHelpers.MentionRegex, Python.PyStr.Lower(bio));
            }
        }

        public bool BlockedByViewer { get { return (bool)Metadata("blocked_by_viewer"); } }

        public bool FollowsViewer { get { return (bool)Metadata("follows_viewer"); } }

        public string FullName { get { return (string)Metadata("full_name"); } }

        public bool HasBlockedViewer { get { return (bool)Metadata("has_blocked_viewer"); } }

        /// <summary>Deprecated since 4.0.6: Always returns true since issue #153.</summary>
        public bool HasHighlightReels { get { return true; } }

        public bool HasPublicStory
        {
            get
            {
                if (_hasPublicStory != true)
                {
                    ObtainMetadata();
                    // query rate might be limited:
                    JObject data = _context.GraphqlQuery("9ca88e465c3f866a76f7adee3871bdd8",
                                                         new JObject
                                                         {
                                                             { "user_id", Userid },
                                                             { "include_chaining", false },
                                                             { "include_reel", false },
                                                             { "include_suggested_users", false },
                                                             { "include_logged_out_extras", true },
                                                             { "include_highlight_reels", false }
                                                         },
                                                         string.Format("https://www.instagram.com/{0}/", Username));
                    _hasPublicStory = (bool)data["data"]["user"]["has_public_story"];
                }
                return _hasPublicStory.Value;
            }
        }

        /// <summary>
        /// Deprecated since 4.0.6.
        ///
        /// Some stories are private. This property determines if the Profile has at least one story which can be
        /// viewed using the associated InstaloaderContext, i.e. the viewer has privileges to view it.
        /// </summary>
        public bool HasViewableStory
        {
            get { return HasPublicStory || FollowedByViewer && HasHighlightReels; }
        }

        public bool HasRequestedViewer { get { return (bool)Metadata("has_requested_viewer"); } }

        public bool IsVerified { get { return (bool)Metadata("is_verified"); } }

        public bool RequestedByViewer { get { return (bool)Metadata("requested_by_viewer"); } }

        /// <summary>
        /// Return URL of profile picture. If logged in, the HD version is returned, otherwise a lower-quality version.
        /// </summary>
        public string ProfilePicUrl
        {
            get
            {
                // The logged-in profile query already returns the HD picture, so there is no need
                // to ask the iPhone API, which fails for some profiles.
                try
                {
                    return (string)_node["hd_profile_pic_url_info"]["url"];
                }
                catch (Exception ex) when (ex is PyKeyError || ex is PyTypeError)
                {
                }
                if (_context.IphoneSupport && _context.IsLoggedIn)
                {
                    try
                    {
                        return (string)IphoneStruct["hd_profile_pic_url_info"]["url"];
                    }
                    catch (Exception err) when (err is InstaloaderException || err is PyKeyError)
                    {
                        _context.Error(string.Format("Unable to fetch high quality profile pic: {0}", err.Message));
                        return (string)Metadata("profile_pic_url_hd");
                    }
                }
                return (string)Metadata("profile_pic_url_hd");
            }
        }

        /// <summary>Return URL of lower-quality profile picture.</summary>
        public string ProfilePicUrlNoIphone
        {
            get { return (string)Metadata("profile_pic_url_hd"); }
        }

        /// <summary>Deprecated since 4.0.3: Use <see cref="ProfilePicUrl"/>.</summary>
        public string GetProfilePicUrl()
        {
            return ProfilePicUrl;
        }

        /// <summary>Retrieve all posts from a profile.</summary>
        public NodeIterator<Post> GetPosts()
        {
            ObtainMetadata();
            return new NodeIterator<Post>(
                context: _context,
                edgeExtractor: d => (JObject)d["data"]["xdt_api__v1__feed__user_timeline_graphql_connection"],
                nodeWrapper: n => Post.FromIphoneStruct(_context, n),
                // The query and variables that the web app sends for a profile's posts tab
                // (PolarisProfilePostsTabContentQuery_connection). It serves both logged-in and
                // anonymous sessions. Instagram answers the previous logged-in query,
                // 7898261790222653, with "execution error" or a redirect, and the previous
                // anonymous query, 7950326061742207, with "require_login".
                queryVariables: new JObject
                {
                    {
                        "data", new JObject
                        {
                            { "count", 12 },
                            { "include_reel_media_seen_timestamp", true },
                            { "include_relationship_info", true },
                            { "latest_besties_reel_media", true },
                            { "latest_reel_media", true }
                        }
                    },
                    { "first", 12 },
                    { "include_multi_captions", true },
                    { "username", Username },
                    { "__relay_internal__pv__PolarisMultiCaptionCarouselEnabledrelayprovider", true },
                    { "__relay_internal__pv__PolarisShortDramaEnabledrelayprovider", false },
                    { "__relay_internal__pv__PolarisReelsRecoDebugOverlayEnabledrelayprovider", false }
                },
                queryReferer: string.Format("https://www.instagram.com/{0}/", Username),
                isFirst: MakeIsNewestChecker(),
                docId: "28975909992013618",
                queryHash: null);
        }

        /// <summary>Get Posts that are marked as saved by the user.</summary>
        public NodeIterator<Post> GetSavedPosts()
        {
            if (Username != _context.Username)
                throw new LoginRequiredException(string.Format("Login as {0} required to get that profile's saved posts.", Username));

            return new NodeIterator<Post>(
                _context,
                "f883d95537fbcd400f466f63d42bd8a1",
                d => (JObject)d["data"]["user"]["edge_saved_media"],
                n => new Post(_context, n),
                new JObject { { "id", Userid } },
                string.Format("https://www.instagram.com/{0}/", Username));
        }

        /// <summary>Retrieve all posts where a profile is tagged.</summary>
        public NodeIterator<Post> GetTaggedPosts()
        {
            ObtainMetadata();
            return new NodeIterator<Post>(
                _context,
                "e31a871f7301132ceaab56507a66bbb7",
                d => (JObject)d["data"]["user"]["edge_user_to_photos_of_you"],
                n => new Post(_context, n, n["owner"]["id"].ToInt64() == Userid ? this : null),
                new JObject { { "id", Userid } },
                string.Format("https://www.instagram.com/{0}/", Username),
                isFirst: MakeIsNewestChecker());
        }

        /// <summary>
        /// doc_id of the query of the web app's reels tab (PolarisProfileReelsTabContentQuery_connection). Sent with
        /// the web app's request fields (see InstaloaderContext.DocIdGraphqlQuery), the older query of Instaloader
        /// 4.15.4, 7845543455542541, is answered without reels (null).
        /// </summary>
        internal const string ReelsDocId = "28728438306809751";

        /// <summary>Variables of the reels tab query for the profile with the given ID.</summary>
        internal static JObject ReelsQueryVariables(string userId)
        {
            return new JObject
            {
                {
                    "data", new JObject
                    {
                        { "include_feed_video", true },
                        { "page_size", 12 },
                        { "target_user_id", userId }
                    }
                },
                { "first", 12 },
                { "id", userId },
                { "__relay_internal__pv__PolarisShortDramaEnabledrelayprovider", false }
            };
        }

        /// <summary>The connection of reels in a response to the reels tab query, or null if there is none.</summary>
        internal static JObject ReelsConnection(JObject response)
        {
            return response.Get("data").Or(new JObject()).Get("fetch__XDTUserDict").Or(new JObject()).Get("clips_connection") as JObject;
        }

        /// <summary>Retrieve all reels from a profile.</summary>
        public NodeIterator<Post> GetReels()
        {
            ObtainMetadata();
            return new NodeIterator<Post>(
                context: _context,
                edgeExtractor: d =>
                {
                    JObject connection = ReelsConnection(d);
                    if (connection == null)
                        throw new BadResponseException("Instagram's response to the reels query contains no reels.");
                    return connection;
                },
                // Reels post info is incomplete relative to regular posts so we create a Post from the shortcode
                // and fetch the additional metadata with an additional API request per Reel
                nodeWrapper: n => Post.FromShortcode(_context, (string)n["media"]["code"]),
                queryVariables: ReelsQueryVariables(Userid.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                queryReferer: string.Format("https://www.instagram.com/{0}/", Username),
                isFirst: MakeIsNewestChecker(),
                docId: ReelsDocId,
                queryHash: null);
        }

        /// <summary>Retrieve all IGTV posts.</summary>
        public NodeIterator<Post> GetIgtvPosts()
        {
            ObtainMetadata();
            return new NodeIterator<Post>(
                _context,
                "bc78b344a68ed16dd5d7f264681c4c76",
                d => (JObject)d["data"]["user"]["edge_felix_video_timeline"],
                n => new Post(_context, n, this),
                new JObject { { "id", Userid } },
                string.Format("https://www.instagram.com/{0}/channel/", Username),
                (JObject)Metadata("edge_felix_video_timeline"),
                MakeIsNewestChecker());
        }

        internal static Func<Post, Post, bool> MakeIsNewestChecker()
        {
            return (post, first) => first == null || post.DateLocal > first.DateLocal;
        }

        /// <summary>
        /// Retrieve list of hashtags followed by given profile.
        /// To use this, one needs to be logged in and private profiles has to be followed.
        /// </summary>
        /// <remarks>
        /// Instagram removed the possibility to follow hashtags in December 2024, so this is always empty.
        /// (Instaloader 4.15.4 queries the discontinued connection and fails with a TypeError.)
        /// </remarks>
        public IEnumerable<Hashtag> GetFollowedHashtags()
        {
            if (!_context.IsLoggedIn)
                throw new LoginRequiredException("Login required to get a profile's followers.");
            return new List<Hashtag>();
        }

        /// <summary>
        /// Retrieve list of followers of given profile.
        /// To use this, one needs to be logged in and private profiles has to be followed.
        /// </summary>
        /// <remarks>
        /// Uses the friendships API of Instagram's web app. The GraphQL query of Instaloader 4.15.4 no longer
        /// returns any profiles.
        /// </remarks>
        public IEnumerable<Profile> GetFollowers()
        {
            if (!_context.IsLoggedIn)
                throw new LoginRequiredException("Login required to get a profile's followers.");
            ObtainMetadata();
            return Friendships("followers");
        }

        /// <summary>
        /// Retrieve list of followees (followings) of given profile.
        /// To use this, one needs to be logged in and private profiles has to be followed.
        /// </summary>
        /// <remarks>
        /// Uses the friendships API of Instagram's web app. The GraphQL query of Instaloader 4.15.4 no longer
        /// returns any profiles.
        /// </remarks>
        public IEnumerable<Profile> GetFollowees()
        {
            if (!_context.IsLoggedIn)
                throw new LoginRequiredException("Login required to get a profile's followees.");
            ObtainMetadata();
            return Friendships("following");
        }

        /// <summary>Pages through api/v1/friendships/ID/{following,followers}/.</summary>
        private IEnumerable<Profile> Friendships(string kind)
        {
            string path = string.Format("api/v1/friendships/{0}/{1}/", Userid, kind);
            string maxId = null;
            while (true)
            {
                var parameters = new JObject { { "count", 12 } };
                if (maxId != null)
                    parameters["max_id"] = maxId;
                JObject data = _context.GetJson(path, parameters);
                foreach (JToken user in data.Get("users", new JArray()).AsIterable())
                {
                    JObject node = user.AsObject().Copy();
                    if (node.Get("id").IsNone())
                        node["id"] = node["pk"];
                    yield return new Profile(_context, node);
                }
                JToken next = data.Get("next_max_id");
                if (next.IsNone() || !next.Truthy)
                    yield break;
                maxId = Python.PyStr.Str(next);
            }
        }

        /// <summary>
        /// Retrieve list of suggested / similar accounts for this profile.
        /// To use this, one needs to be logged in.
        /// </summary>
        public IEnumerable<Profile> GetSimilarAccounts()
        {
            if (!_context.IsLoggedIn)
                throw new LoginRequiredException("Login required to get a profile's similar accounts.");
            return GetSimilarAccountsImpl();
        }

        private IEnumerable<Profile> GetSimilarAccountsImpl()
        {
            ObtainMetadata();
            JObject data = _context.GraphqlQuery("ad99dd9d3646cc3c0dda65debcd266a7",
                                                 new JObject
                                                 {
                                                     { "user_id", Userid.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                                                     { "include_chaining", true }
                                                 },
                                                 string.Format("https://www.instagram.com/{0}/", Username));
            foreach (JToken edge in data["data"]["user"]["edge_chaining"]["edges"].AsIterable())
                yield return new Profile(_context, (JObject)edge["node"]);
        }
    }
}

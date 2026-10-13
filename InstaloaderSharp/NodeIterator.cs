using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using InstaloaderSharp.Compression;
using InstaloaderSharp.Json;
using InstaloaderSharp.Python;
using InstaloaderSharp.Util;

namespace InstaloaderSharp
{
    /// <summary>Structures that can be saved to and loaded from Instaloader JSON files.</summary>
    public interface IJsonExportable
    {
        /// <summary>The JSON node (Python's _asdict()).</summary>
        JObject AsDict();

        /// <summary>The node_type stored in the JSON file (the Python class name).</summary>
        string NodeType { get; }
    }

    /// <summary>A frozen NodeIterator, i.e. the information needed to resume an interrupted iteration.</summary>
    public sealed class FrozenNodeIterator : IJsonExportable
    {
        /// <summary>The GraphQL query_hash parameter.</summary>
        public string QueryHash { get; set; }

        /// <summary>The GraphQL query_variables parameter.</summary>
        public JObject QueryVariables { get; set; }

        /// <summary>The HTTP referer used for the GraphQL query.</summary>
        public string QueryReferer { get; set; }

        /// <summary>The username who created the iterator, or null.</summary>
        public string ContextUsername { get; set; }

        /// <summary>Number of items that have already been returned.</summary>
        public long TotalIndex { get; set; }

        /// <summary>Date (POSIX timestamp) when parts of the stored nodes might have expired.</summary>
        public JToken BestBefore { get; set; }

        /// <summary>The already-retrieved, yet-unprocessed edges and the page_info at time of freezing.</summary>
        public JToken RemainingData { get; set; }

        /// <summary>Node data of the first item, if an item has been produced.</summary>
        public JToken FirstNode { get; set; }

        /// <summary>The GraphQL doc_id parameter.</summary>
        public string DocId { get; set; }

        public string NodeType { get { return "FrozenNodeIterator"; } }

        public JObject AsDict()
        {
            return new JObject
            {
                { "query_hash", QueryHash },
                { "query_variables", QueryVariables },
                { "query_referer", QueryReferer },
                { "context_username", ContextUsername },
                { "total_index", TotalIndex },
                { "best_before", BestBefore ?? JValue.Null },
                { "remaining_data", RemainingData ?? JValue.Null },
                { "first_node", FirstNode ?? JValue.Null },
                { "doc_id", DocId }
            };
        }

        /// <summary>Python's FrozenNodeIterator(**node).</summary>
        public static FrozenNodeIterator FromDict(JObject node)
        {
            foreach (string required in new[] { "query_hash", "query_variables", "query_referer", "context_username",
                                                 "total_index", "best_before", "remaining_data" })
            {
                if (!node.ContainsKey(required))
                    throw new InvalidArgumentException(string.Format(
                        "FrozenNodeIterator.__new__() missing required argument: {0}", PyStr.Repr(required)));
            }
            JToken variables = node["query_variables"];
            return new FrozenNodeIterator
            {
                QueryHash = (string)node["query_hash"],
                QueryVariables = variables as JObject ?? new JObject(),
                QueryReferer = (string)node["query_referer"],
                ContextUsername = (string)node["context_username"],
                TotalIndex = node["total_index"].ToInt64(),
                BestBefore = node["best_before"],
                RemainingData = node["remaining_data"],
                FirstNode = node.Get("first_node") ?? JValue.Null,
                DocId = (string)(node.Get("doc_id") ?? JValue.Null)
            };
        }
    }

    /// <summary>Non-generic view of a <see cref="NodeIterator{T}"/>.</summary>
    public interface INodeIterator
    {
        /// <summary>Magic string for easily identifying a matching iterator file for resuming.</summary>
        string Magic { get; }

        /// <summary>Number of items that have already been returned.</summary>
        long TotalIndex { get; }

        /// <summary>Freeze the iterator for later resuming.</summary>
        FrozenNodeIterator Freeze();

        /// <summary>Use this iterator for resuming from earlier iteration.</summary>
        void Thaw(FrozenNodeIterator frozen);
    }

    /// <summary>
    /// Iterate the nodes within edges in a GraphQL pagination. Instances of this class are returned by many (but
    /// not all) of Instaloader's Post-returning functions (such as <see cref="Profile.GetPosts"/> etc.).
    ///
    /// What makes this iterator special is its ability to freeze/store its current state, e.g. to interrupt an
    /// iteration, and later thaw/resume from where it left off.
    ///
    /// A <see cref="FrozenNodeIterator"/> can only be thawn with a matching NodeIterator, i.e. a NodeIterator
    /// instance that has been constructed with the same parameters as the instance that is represented by the
    /// FrozenNodeIterator in question. Two NodeIterators are matching if and only if they have the same
    /// <see cref="Magic"/>.
    ///
    /// See also <see cref="NodeIteration.ResumableIteration"/> for a high-level helper that handles a resumable
    /// iteration.
    /// </summary>
    public class NodeIterator<T> : INodeIterator, IEnumerable<T>, IEnumerator<T> where T : class
    {
        private static readonly int GraphqlPageLengthValue = 12;
        private static readonly TimeSpan ShelfLife = TimeSpan.FromDays(29);

        private readonly InstaloaderContext _context;
        private readonly string _queryHash;
        private readonly string _docId;
        private readonly Func<JObject, JObject> _edgeExtractor;
        private readonly Func<JObject, T> _nodeWrapper;
        private readonly JObject _queryVariables;
        private readonly string _queryReferer;
        private int _pageIndex;
        private long _totalIndex;
        private JObject _data;
        private DateTime? _bestBefore; // UTC
        private JObject _firstNode;
        private readonly Func<T, T, bool> _isFirst;
        private T _current;

        public NodeIterator(InstaloaderContext context,
                            string queryHash,
                            Func<JObject, JObject> edgeExtractor,
                            Func<JObject, T> nodeWrapper,
                            JObject queryVariables = null,
                            string queryReferer = null,
                            JObject firstData = null,
                            Func<T, T, bool> isFirst = null,
                            string docId = null)
        {
            _context = context;
            _queryHash = queryHash;
            _docId = docId;
            _edgeExtractor = edgeExtractor;
            _nodeWrapper = nodeWrapper;
            _queryVariables = queryVariables ?? new JObject();
            _queryReferer = queryReferer;
            _pageIndex = 0;
            _totalIndex = 0;
            if (firstData != null)
            {
                _data = firstData;
                _bestBefore = DateTime.UtcNow + ShelfLife;
            }
            else
            {
                _data = Query();
            }
            _firstNode = null;
            _isFirst = isFirst;
        }

        private JObject Query(string after = null)
        {
            if (_docId != null)
                return QueryDocId(_docId, after);
            return QueryQueryHash(_queryHash, after);
        }

        private JObject QueryDocId(string docId, string after = null)
        {
            var paginationVariables = new JObject { { "__relay_internal__pv__PolarisFeedShareMenurelayprovider", false } };
            if (after != null)
            {
                paginationVariables["after"] = after;
                paginationVariables["before"] = JValue.Null;
                paginationVariables["first"] = 12;
                paginationVariables["last"] = JValue.Null;
            }
            JObject data = _edgeExtractor(
                _context.DocIdGraphqlQuery(docId, JObject.Merge(_queryVariables, paginationVariables), _queryReferer));
            _bestBefore = DateTime.UtcNow + ShelfLife;
            return data;
        }

        private JObject QueryQueryHash(string queryHash, string after = null)
        {
            var paginationVariables = new JObject { { "first", GraphqlPageLengthValue } };
            if (after != null)
                paginationVariables["after"] = after;
            JObject data = _edgeExtractor(
                _context.GraphqlQuery(queryHash, JObject.Merge(_queryVariables, paginationVariables), _queryReferer));
            _bestBefore = DateTime.UtcNow + ShelfLife;
            return data;
        }

        /// <summary>
        /// The edges of a page. Missing edges count as an empty page: Instagram no longer returns some
        /// connections (e.g. IGTV, which it retired), and Instaloader 4.15.4 fails with KeyError: 'edges' there.
        /// </summary>
        private static JArray Edges(JObject data)
        {
            return data.Get("edges") as JArray ?? new JArray();
        }

        /// <summary>Python's __next__; returns false at the end of the iteration (StopIteration).</summary>
        public bool MoveNext()
        {
            Interrupt.Check();
            JArray edges = Edges(_data);
            if (_pageIndex < edges.Count)
            {
                JObject node = (JObject)edges[_pageIndex]["node"];
                _pageIndex += 1;
                _totalIndex += 1;
                T item = _nodeWrapper(node);
                if (_isFirst != null)
                {
                    if (_isFirst(item, FirstItem))
                        _firstNode = node;
                }
                else
                {
                    if (_firstNode == null)
                        _firstNode = node;
                }
                _current = item;
                return true;
            }
            JToken pageInfo = _data.Get("page_info") ?? new JObject();
            if (pageInfo.Get("has_next_page").Or(null) != null)
            {
                JObject queryResponse = Query((string)_data["page_info"]["end_cursor"]);
                JArray newEdges = Edges(queryResponse);
                if (!JToken.DeepEquals(Edges(_data), newEdges) && newEdges.Count > 0)
                {
                    _pageIndex = 0;
                    _data = queryResponse;
                    return MoveNext();
                }
            }
            _current = null;
            return false;
        }

        public T Current { get { return _current; } }

        object IEnumerator.Current { get { return _current; } }

        public void Reset()
        {
            throw new NotSupportedException();
        }

        public void Dispose()
        {
        }

        public IEnumerator<T> GetEnumerator()
        {
            return this;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this;
        }

        /// <summary>The count as returned by Instagram. This is not always the total count this iterator will yield.</summary>
        public long? Count
        {
            get
            {
                if (_data == null)
                    return null;
                JToken count = _data.Get("count");
                return count.IsNone() ? (long?)null : count.ToInt64();
            }
        }

        /// <summary>Number of items that have already been returned.</summary>
        public long TotalIndex { get { return _totalIndex; } }

        /// <summary>Magic string for easily identifying a matching iterator file for resuming (hash of some parameters).</summary>
        public string Magic
        {
            get
            {
                var magicInput = new JArray { _queryHash, _queryVariables, _queryReferer, _context.Username };
                byte[] digest = Blake2b.Hash(Encoding.UTF8.GetBytes(PyJson.Dumps(magicInput)), 6);
                return Convert.ToBase64String(digest).Replace('+', '-').Replace('/', '_');
            }
        }

        /// <summary>
        /// If this iterator has produced any items, returns the first item produced.
        ///
        /// It is possible to override what is considered the first item (for example, to consider the
        /// newest item in case items are not in strict chronological order) by passing a callback
        /// function as the isFirst parameter when creating the class.
        /// </summary>
        public T FirstItem
        {
            get { return _firstNode != null ? _nodeWrapper(_firstNode) : null; }
        }

        public static int PageLength()
        {
            return GraphqlPageLengthValue;
        }

        private static double ToTimestamp(DateTime utc)
        {
            return (utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Ticks / 10 / 1e6;
        }

        /// <summary>Freeze the iterator for later resuming.</summary>
        public FrozenNodeIterator Freeze()
        {
            JObject remainingData = null;
            if (_data != null)
            {
                remainingData = _data.Copy();
                remainingData["edges"] = Edges(_data).Slice(Math.Max(_pageIndex - 1, 0));
            }
            return new FrozenNodeIterator
            {
                QueryHash = _queryHash,
                QueryVariables = _queryVariables,
                QueryReferer = _queryReferer,
                ContextUsername = _context.Username,
                TotalIndex = Math.Max(TotalIndex - 1, 0),
                BestBefore = _bestBefore.HasValue ? (JToken)ToTimestamp(_bestBefore.Value) : JValue.Null,
                RemainingData = (JToken)remainingData ?? JValue.Null,
                FirstNode = (JToken)_firstNode ?? JValue.Null,
                DocId = _docId
            };
        }

        /// <summary>Use this iterator for resuming from earlier iteration.</summary>
        /// <exception cref="InvalidArgumentException">
        /// If the iterator on which this method is called has already been used, or the given
        /// FrozenNodeIterator does not match, i.e. belongs to a different iteration.
        /// </exception>
        public void Thaw(FrozenNodeIterator frozen)
        {
            if (_totalIndex != 0 || _pageIndex != 0)
                throw new InvalidArgumentException("thaw() called on already-used iterator.");
            if (_queryHash != frozen.QueryHash ||
                !JToken.DeepEquals(_queryVariables, frozen.QueryVariables) ||
                _queryReferer != frozen.QueryReferer ||
                _context.Username != frozen.ContextUsername ||
                _docId != frozen.DocId)
                throw new InvalidArgumentException("Mismatching resume information.");
            if (frozen.BestBefore.IsNone() || !frozen.BestBefore.Truthy)
                throw new InvalidArgumentException("\"best before\" date missing.");
            if (frozen.RemainingData.IsNone())
                throw new InvalidArgumentException("\"remaining_data\" missing.");
            _totalIndex = frozen.TotalIndex;
            _bestBefore = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(
                (long)Math.Round(frozen.BestBefore.ToDouble() * 1e6) * 10);
            _data = frozen.RemainingData.AsObject();
            if (!frozen.FirstNode.IsNone())
                _firstNode = frozen.FirstNode.AsObject();
        }
    }

    /// <summary>Helpers for resumable iterations.</summary>
    public static class NodeIteration
    {
        /// <summary>
        /// High-level helper to handle a resumable iteration that can be interrupted with ^C
        /// (<see cref="KeyboardInterruptException"/>) or an <see cref="AbortDownloadException"/>.
        ///
        /// It automatically loads a previously-saved state into the iterator, saves the iterator's state when
        /// interrupted, and deletes the resume file upon completion.
        ///
        /// The body is called with (isResuming, startIndex).
        ///
        /// When the passed iterator is not a <see cref="INodeIterator"/>, it behaves as if ResumableIteration was
        /// not used, just executing the body.
        /// </summary>
        /// <param name="context">The InstaloaderContext.</param>
        /// <param name="iterator">The fresh NodeIterator.</param>
        /// <param name="load">Loads a FrozenNodeIterator from given path. The object is ignored if it has a different type.</param>
        /// <param name="save">Saves the given FrozenNodeIterator to the given path.</param>
        /// <param name="formatPath">Returns the path to the resume file for the given magic.</param>
        /// <param name="checkBbd">Whether to check the best before date and reject an expired FrozenNodeIterator.</param>
        /// <param name="enabled">Set to false to disable all functionality and simply execute the inner body.</param>
        /// <param name="body">The iteration.</param>
        public static void ResumableIteration(InstaloaderContext context,
                                              object iterator,
                                              Func<InstaloaderContext, string, object> load,
                                              Action<FrozenNodeIterator, string> save,
                                              Func<string, string> formatPath,
                                              bool checkBbd,
                                              bool enabled,
                                              Action<bool, long> body)
        {
            var nodeIterator = iterator as INodeIterator;
            if (!enabled || nodeIterator == null)
            {
                body(false, 0);
                return;
            }
            bool isResuming = false;
            long startIndex = 0;
            string resumeFilePath = formatPath(nodeIterator.Magic);
            bool resumeFileExists = PyOs.IsFile(resumeFilePath);
            if (resumeFileExists)
            {
                try
                {
                    object loaded = load(context, resumeFilePath);
                    var fni = loaded as FrozenNodeIterator;
                    if (fni == null)
                        throw new InvalidArgumentException("Invalid type.");
                    if (checkBbd && !fni.BestBefore.IsNone() && fni.BestBefore.Truthy &&
                        PyDateTime.FromTimestamp(fni.BestBefore.ToDouble()).CompareTo(PyDateTime.Now()) < 0)
                        throw new InvalidArgumentException("\"Best before\" date exceeded.");
                    nodeIterator.Thaw(fni);
                    isResuming = true;
                    startIndex = nodeIterator.TotalIndex;
                    context.Log(string.Format("Resuming from {0}.", resumeFilePath));
                }
                catch (Exception exc) when (exc is InvalidArgumentException || exc is LzmaException ||
                                            exc is JsonDecodeError || exc is EndOfStreamException)
                {
                    context.Error(string.Format("Warning: Not resuming from {0}: {1}", resumeFilePath, exc.Message));
                }
            }
            try
            {
                body(isResuming, startIndex);
            }
            catch (Exception)
            {
                string dir = PyOs.DirName(resumeFilePath);
                if (dir.Length > 0)
                    PyOs.MakeDirs(dir);
                save(nodeIterator.Freeze(), resumeFilePath);
                context.Log(string.Format("\nSaved resume information to {0}.", resumeFilePath));
                throw;
            }
            if (resumeFileExists)
            {
                File.Delete(resumeFilePath);
                context.Log(string.Format("Iteration complete, deleted resume information file {0}.", resumeFilePath));
            }
        }
    }

    /// <summary>Iterator for the new 'sections'-style responses.</summary>
    public class SectionIterator<T> : IEnumerable<T>, IEnumerator<T> where T : class
    {
        private readonly InstaloaderContext _context;
        private readonly Func<JObject, JObject> _sectionsExtractor;
        private readonly Func<JObject, T> _mediaWrapper;
        private readonly string _queryPath;
        private JObject _data;
        private int _pageIndex;
        private int _sectionIndex;
        private T _current;

        public SectionIterator(InstaloaderContext context,
                               Func<JObject, JObject> sectionsExtractor,
                               Func<JObject, T> mediaWrapper,
                               string queryPath,
                               JObject firstData = null)
        {
            _context = context;
            _sectionsExtractor = sectionsExtractor;
            _mediaWrapper = mediaWrapper;
            _queryPath = queryPath;
            _data = firstData != null && firstData.Truthy ? firstData : Query();
            _pageIndex = 0;
            _sectionIndex = 0;
        }

        private JObject Query(string maxId = null)
        {
            var parameters = new JObject { { "__a", 1 }, { "__d", "dis" } };
            if (maxId != null)
                parameters["max_id"] = maxId;
            return _sectionsExtractor(_context.GetJson(_queryPath, parameters));
        }

        public bool MoveNext()
        {
            Interrupt.Check();
            JArray sections = _data["sections"].AsArray();
            if (_pageIndex < sections.Count)
            {
                JArray medias = sections[_pageIndex]["layout_content"]["medias"].AsArray();
                JObject media = (JObject)medias[_sectionIndex]["media"];
                _sectionIndex += 1;
                if (_sectionIndex >= medias.Count)
                {
                    _sectionIndex = 0;
                    _pageIndex += 1;
                }
                _current = _mediaWrapper(media);
                return true;
            }
            if (_data["more_available"].Truthy)
            {
                JObject next = Query((string)_data["next_max_id"]);
                _pageIndex = 0;
                _sectionIndex = 0;
                _data = next;
                return MoveNext();
            }
            _current = null;
            return false;
        }

        public T Current { get { return _current; } }
        object IEnumerator.Current { get { return _current; } }
        public void Reset() { throw new NotSupportedException(); }
        public void Dispose() { }
        public IEnumerator<T> GetEnumerator() { return this; }
        IEnumerator IEnumerable.GetEnumerator() { return this; }
    }
}

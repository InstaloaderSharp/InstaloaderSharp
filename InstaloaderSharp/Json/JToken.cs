using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using InstaloaderSharp.Python;

namespace InstaloaderSharp.Json
{
    public enum JTokenType
    {
        Null,
        Boolean,
        Integer,
        Float,
        String,
        Array,
        Object
    }

    /// <summary>
    /// A JSON value as Python's json module represents it (dict, list, str, int, float, bool, None).
    ///
    /// Indexing behaves like in Python: a missing key raises <see cref="PyKeyError"/>, an index out of range
    /// raises <see cref="PyIndexError"/>, and indexing a value that is not a container raises
    /// <see cref="PyTypeError"/>.
    ///
    /// A C# null returned from <see cref="Get(string)"/> means "key is missing", while <see cref="JValue.Null"/>
    /// is JSON's null (Python's None).
    /// </summary>
    public abstract class JToken
    {
        public abstract JTokenType Type { get; }

        /// <summary>Name of the corresponding Python type.</summary>
        public string PyTypeName
        {
            get
            {
                switch (Type)
                {
                    case JTokenType.Null: return "NoneType";
                    case JTokenType.Boolean: return "bool";
                    case JTokenType.Integer: return "int";
                    case JTokenType.Float: return "float";
                    case JTokenType.String: return "str";
                    case JTokenType.Array: return "list";
                    default: return "dict";
                }
            }
        }

        public bool IsNull { get { return Type == JTokenType.Null; } }

        public virtual JToken this[string key]
        {
            get
            {
                if (Type == JTokenType.Array)
                    throw new PyTypeError("list indices must be integers or slices, not str");
                if (Type == JTokenType.String)
                    throw new PyTypeError("string indices must be integers, not 'str'");
                throw new PyTypeError(string.Format("'{0}' object is not subscriptable", PyTypeName));
            }
            set { throw new PyTypeError(string.Format("'{0}' object does not support item assignment", PyTypeName)); }
        }

        public virtual JToken this[int index]
        {
            get
            {
                if (Type == JTokenType.Object)
                    throw new PyKeyError(index);
                throw new PyTypeError(string.Format("'{0}' object is not subscriptable", PyTypeName));
            }
            set { throw new PyTypeError(string.Format("'{0}' object does not support item assignment", PyTypeName)); }
        }

        /// <summary>Python's dict.get(key): returns C# null if the key is missing.</summary>
        public virtual JToken Get(string key)
        {
            throw new PyAttributeError(string.Format("'{0}' object has no attribute 'get'", PyTypeName));
        }

        /// <summary>Python's dict.get(key, default).</summary>
        public JToken Get(string key, JToken defaultValue)
        {
            return Get(key) ?? defaultValue;
        }

        /// <summary>Python's <c>key in dict</c>.</summary>
        public virtual bool ContainsKey(string key)
        {
            throw new PyTypeError(string.Format("argument of type '{0}' is not iterable", PyTypeName));
        }

        /// <summary>Python's len().</summary>
        public virtual int Count
        {
            get { throw new PyTypeError(string.Format("object of type '{0}' has no len()", PyTypeName)); }
        }

        /// <summary>Python's bool(value).</summary>
        public abstract bool Truthy { get; }

        /// <summary>Follows the given keys, like <c>d[k1][k2]...</c>.</summary>
        public JToken Path(params string[] keys)
        {
            JToken d = this;
            foreach (string key in keys)
                d = d[key];
            return d;
        }

        public JObject AsObject()
        {
            var o = this as JObject;
            if (o == null)
                throw new PyTypeError(string.Format("expected dict, got '{0}'", PyTypeName));
            return o;
        }

        public JArray AsArray()
        {
            var a = this as JArray;
            if (a == null)
                throw new PyTypeError(string.Format("expected list, got '{0}'", PyTypeName));
            return a;
        }

        /// <summary>Python's int(value).</summary>
        public virtual BigInteger ToBigInteger()
        {
            throw new PyTypeError(string.Format("int() argument must be a string, a bytes-like object or a real number, not '{0}'", PyTypeName));
        }

        /// <summary>Python's int(value), as long.</summary>
        public long ToInt64()
        {
            BigInteger b = ToBigInteger();
            if (b > long.MaxValue || b < long.MinValue)
                throw new PyOverflowError("Python int too large to convert to C long");
            return (long)b;
        }

        /// <summary>Python's float(value).</summary>
        public virtual double ToDouble()
        {
            throw new PyTypeError(string.Format("float() argument must be a string or a real number, not '{0}'", PyTypeName));
        }

        /// <summary>The underlying .NET value for scalars (null, bool, long, BigInteger, double, string).</summary>
        public virtual object Value { get { return this; } }

        public override string ToString()
        {
            return PyJson.Dumps(this);
        }

        #region conversions

        public static implicit operator JToken(string value)
        {
            return value == null ? JValue.Null : new JValue(value);
        }

        public static implicit operator JToken(long value)
        {
            return new JValue(value);
        }

        public static implicit operator JToken(int value)
        {
            return new JValue((long)value);
        }

        public static implicit operator JToken(double value)
        {
            return new JValue(value);
        }

        public static implicit operator JToken(bool value)
        {
            return value ? JValue.True : JValue.False;
        }

        public static implicit operator JToken(long? value)
        {
            return value.HasValue ? new JValue(value.Value) : JValue.Null;
        }

        public static implicit operator JToken(double? value)
        {
            return value.HasValue ? new JValue(value.Value) : JValue.Null;
        }

        public static implicit operator JToken(bool? value)
        {
            return value.HasValue ? (value.Value ? JValue.True : JValue.False) : JValue.Null;
        }

        public static implicit operator JToken(BigInteger value)
        {
            return new JValue(value);
        }

        public static explicit operator string(JToken token)
        {
            if (token == null || token.IsNull)
                return null;
            if (token.Type == JTokenType.String)
                return (string)token.Value;
            return PyStr.Str(token);
        }

        public static explicit operator long(JToken token)
        {
            if (token == null)
                throw new PyTypeError("int() argument must be a string, a bytes-like object or a real number, not 'NoneType'");
            return token.ToInt64();
        }

        public static explicit operator int(JToken token)
        {
            return checked((int)(long)token);
        }

        public static explicit operator double(JToken token)
        {
            if (token == null)
                throw new PyTypeError("float() argument must be a string or a real number, not 'NoneType'");
            return token.ToDouble();
        }

        public static explicit operator bool(JToken token)
        {
            return token != null && token.Truthy;
        }

        public static explicit operator long?(JToken token)
        {
            if (token == null || token.IsNull)
                return null;
            return token.ToInt64();
        }

        public static explicit operator double?(JToken token)
        {
            if (token == null || token.IsNull)
                return null;
            return token.ToDouble();
        }

        public static explicit operator bool?(JToken token)
        {
            if (token == null || token.IsNull)
                return null;
            return token.Truthy;
        }

        #endregion

        /// <summary>Equality with the semantics of Python's == on JSON-like structures.</summary>
        public static bool DeepEquals(JToken a, JToken b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if (a == null || b == null)
                return false;
            bool aNum = a.Type == JTokenType.Integer || a.Type == JTokenType.Float || a.Type == JTokenType.Boolean;
            bool bNum = b.Type == JTokenType.Integer || b.Type == JTokenType.Float || b.Type == JTokenType.Boolean;
            if (aNum && bNum)
            {
                if (a.Type == JTokenType.Float || b.Type == JTokenType.Float)
                    return a.ToDouble() == b.ToDouble();
                return a.ToBigInteger() == b.ToBigInteger();
            }
            if (a.Type != b.Type)
                return false;
            switch (a.Type)
            {
                case JTokenType.Null:
                    return true;
                case JTokenType.String:
                    return string.Equals((string)a.Value, (string)b.Value, StringComparison.Ordinal);
                case JTokenType.Array:
                    {
                        var la = (JArray)a;
                        var lb = (JArray)b;
                        if (la.Count != lb.Count)
                            return false;
                        for (int i = 0; i < la.Count; i++)
                            if (!DeepEquals(la[i], lb[i]))
                                return false;
                        return true;
                    }
                case JTokenType.Object:
                    {
                        var oa = (JObject)a;
                        var ob = (JObject)b;
                        if (oa.Count != ob.Count)
                            return false;
                        foreach (var kv in oa)
                        {
                            JToken other = ob.Get(kv.Key);
                            if (other == null || !DeepEquals(kv.Value, other))
                                return false;
                        }
                        return true;
                    }
            }
            return false;
        }

        /// <summary>Deep copy.</summary>
        public abstract JToken DeepClone();
    }

    /// <summary>Extension helpers for <see cref="JToken"/>.</summary>
    public static class JTokenExtensions
    {
        /// <summary>True if the token is missing (C# null) or JSON null (Python None).</summary>
        public static bool IsNone(this JToken token)
        {
            return token == null || token.IsNull;
        }

        /// <summary>Python's <c>value or default</c>.</summary>
        public static JToken Or(this JToken token, JToken defaultValue)
        {
            return token != null && token.Truthy ? token : defaultValue;
        }

        /// <summary>
        /// Python's iteration over a value: items of a list, keys of a dict, characters of a str; TypeError
        /// otherwise.
        /// </summary>
        public static IEnumerable<JToken> AsIterable(this JToken token)
        {
            if (token == null)
                throw new PyTypeError("'NoneType' object is not iterable");
            switch (token.Type)
            {
                case JTokenType.Array:
                    return new List<JToken>((JArray)token);
                case JTokenType.Object:
                    {
                        var keys = new List<JToken>();
                        foreach (string key in ((JObject)token).Keys)
                            keys.Add(key);
                        return keys;
                    }
                case JTokenType.String:
                    {
                        var chars = new List<JToken>();
                        foreach (string cp in PyStr.CodePoints((string)token.Value))
                            chars.Add(cp);
                        return chars;
                    }
            }
            throw new PyTypeError(string.Format("'{0}' object is not iterable", token.PyTypeName));
        }
    }

    /// <summary>A scalar JSON value: null, bool, int, float or str.</summary>
    public sealed class JValue : JToken
    {
        public static readonly JValue Null = new JValue(JTokenType.Null, null);
        public static readonly JValue True = new JValue(JTokenType.Boolean, true);
        public static readonly JValue False = new JValue(JTokenType.Boolean, false);

        private readonly JTokenType _type;
        private readonly object _value;

        private JValue(JTokenType type, object value)
        {
            _type = type;
            _value = value;
        }

        public JValue(string value) : this(value == null ? JTokenType.Null : JTokenType.String, value) { }
        public JValue(long value) : this(JTokenType.Integer, value) { }
        public JValue(double value) : this(JTokenType.Float, value) { }

        public JValue(BigInteger value)
            : this(JTokenType.Integer,
                   value >= long.MinValue && value <= long.MaxValue ? (object)(long)value : value)
        {
        }

        public override JTokenType Type { get { return _type; } }

        public override object Value { get { return _value; } }

        public override bool Truthy
        {
            get
            {
                switch (_type)
                {
                    case JTokenType.Null: return false;
                    case JTokenType.Boolean: return (bool)_value;
                    case JTokenType.Integer: return _value is long ? (long)_value != 0 : !((BigInteger)_value).IsZero;
                    case JTokenType.Float: return (double)_value != 0.0;
                    case JTokenType.String: return ((string)_value).Length != 0;
                }
                return true;
            }
        }

        public override int Count
        {
            get
            {
                if (_type == JTokenType.String)
                    return PyStr.Len((string)_value);
                return base.Count;
            }
        }

        public override bool ContainsKey(string key)
        {
            if (_type == JTokenType.String)
                return ((string)_value).IndexOf(key, StringComparison.Ordinal) >= 0;
            return base.ContainsKey(key);
        }

        public override BigInteger ToBigInteger()
        {
            switch (_type)
            {
                case JTokenType.Boolean:
                    return (bool)_value ? BigInteger.One : BigInteger.Zero;
                case JTokenType.Integer:
                    return _value is long ? new BigInteger((long)_value) : (BigInteger)_value;
                case JTokenType.Float:
                    {
                        double d = (double)_value;
                        if (double.IsNaN(d))
                            throw new PyValueError("cannot convert float NaN to integer");
                        if (double.IsInfinity(d))
                            throw new PyOverflowError("cannot convert float infinity to integer");
                        return new BigInteger(Math.Truncate(d));
                    }
                case JTokenType.String:
                    return PyInt.Parse((string)_value);
            }
            return base.ToBigInteger();
        }

        public override double ToDouble()
        {
            switch (_type)
            {
                case JTokenType.Boolean:
                    return (bool)_value ? 1.0 : 0.0;
                case JTokenType.Integer:
                    return _value is long ? (long)_value : (double)(BigInteger)_value;
                case JTokenType.Float:
                    return (double)_value;
                case JTokenType.String:
                    {
                        double d;
                        string s = ((string)_value).Trim();
                        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                            return d;
                        throw new PyValueError("could not convert string to float: " + PyStr.Repr((string)_value));
                    }
            }
            return base.ToDouble();
        }

        public override JToken DeepClone()
        {
            return this;
        }
    }

    /// <summary>A JSON array (Python list).</summary>
    public sealed class JArray : JToken, IEnumerable<JToken>
    {
        private readonly List<JToken> _items;

        public JArray()
        {
            _items = new List<JToken>();
        }

        public JArray(IEnumerable<JToken> items)
        {
            _items = new List<JToken>();
            foreach (JToken item in items)
                _items.Add(item ?? JValue.Null);
        }

        public override JTokenType Type { get { return JTokenType.Array; } }

        public override bool Truthy { get { return _items.Count > 0; } }

        public override int Count { get { return _items.Count; } }

        public List<JToken> Items { get { return _items; } }

        public override JToken this[int index]
        {
            get
            {
                int i = index < 0 ? index + _items.Count : index;
                if (i < 0 || i >= _items.Count)
                    throw new PyIndexError("list index out of range");
                return _items[i];
            }
            set
            {
                int i = index < 0 ? index + _items.Count : index;
                if (i < 0 || i >= _items.Count)
                    throw new PyIndexError("list assignment index out of range");
                _items[i] = value ?? JValue.Null;
            }
        }

        public void Add(JToken item)
        {
            _items.Add(item ?? JValue.Null);
        }

        public void AddRange(IEnumerable<JToken> items)
        {
            foreach (JToken item in items)
                Add(item);
        }

        /// <summary>Python's <c>value in list</c>.</summary>
        public bool Contains(JToken value)
        {
            foreach (JToken item in _items)
                if (DeepEquals(item, value))
                    return true;
            return false;
        }

        /// <summary>Python's list[start:] slice.</summary>
        public JArray Slice(int start)
        {
            return Slice(start, _items.Count);
        }

        /// <summary>Python's list[start:end] slice.</summary>
        public JArray Slice(int start, int end)
        {
            int n = _items.Count;
            if (start < 0) start = Math.Max(0, start + n);
            if (end < 0) end = Math.Max(0, end + n);
            start = Math.Min(start, n);
            end = Math.Min(end, n);
            var result = new JArray();
            for (int i = start; i < end; i++)
                result._items.Add(_items[i]);
            return result;
        }

        public IEnumerator<JToken> GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public override JToken DeepClone()
        {
            var result = new JArray();
            foreach (JToken item in _items)
                result._items.Add(item.DeepClone());
            return result;
        }
    }

    /// <summary>A JSON object (Python dict) that preserves insertion order like Python's dict.</summary>
    public sealed class JObject : JToken, IEnumerable<KeyValuePair<string, JToken>>
    {
        private readonly List<string> _keys = new List<string>();
        private readonly Dictionary<string, JToken> _dict = new Dictionary<string, JToken>(StringComparer.Ordinal);

        public JObject() { }

        public JObject(IEnumerable<KeyValuePair<string, JToken>> items)
        {
            foreach (var kv in items)
                this[kv.Key] = kv.Value;
        }

        public override JTokenType Type { get { return JTokenType.Object; } }

        public override bool Truthy { get { return _keys.Count > 0; } }

        public override int Count { get { return _keys.Count; } }

        public IList<string> Keys { get { return _keys.AsReadOnly(); } }

        public IEnumerable<JToken> Values
        {
            get
            {
                foreach (string key in _keys)
                    yield return _dict[key];
            }
        }

        public override JToken this[string key]
        {
            get
            {
                JToken value;
                if (!_dict.TryGetValue(key, out value))
                    throw new PyKeyError(key);
                return value;
            }
            set
            {
                if (!_dict.ContainsKey(key))
                    _keys.Add(key);
                _dict[key] = value ?? JValue.Null;
            }
        }

        public override JToken Get(string key)
        {
            JToken value;
            return _dict.TryGetValue(key, out value) ? value : null;
        }

        public override bool ContainsKey(string key)
        {
            return _dict.ContainsKey(key);
        }

        /// <summary>Collection initializer support.</summary>
        public void Add(string key, JToken value)
        {
            this[key] = value;
        }

        /// <summary>Python's <c>del d[key]</c>, returns false if missing.</summary>
        public bool Remove(string key)
        {
            if (!_dict.Remove(key))
                return false;
            _keys.Remove(key);
            return true;
        }

        /// <summary>Python's dict.pop(key, None).</summary>
        public JToken Pop(string key)
        {
            JToken value = Get(key);
            if (value != null)
                Remove(key);
            return value;
        }

        /// <summary>Python's dict.update(other).</summary>
        public void Update(JObject other)
        {
            if (other == null)
                return;
            foreach (var kv in other.ToList())
                this[kv.Key] = kv.Value;
        }

        /// <summary>Python's dict.clear().</summary>
        public void Clear()
        {
            _keys.Clear();
            _dict.Clear();
        }

        /// <summary>Python's dict.copy() (shallow copy).</summary>
        public JObject Copy()
        {
            var result = new JObject();
            foreach (string key in _keys)
            {
                result._keys.Add(key);
                result._dict[key] = _dict[key];
            }
            return result;
        }

        /// <summary>Python's <c>{**a, **b}</c>.</summary>
        public static JObject Merge(JObject a, JObject b)
        {
            JObject result = a.Copy();
            result.Update(b);
            return result;
        }

        public List<KeyValuePair<string, JToken>> ToList()
        {
            var list = new List<KeyValuePair<string, JToken>>(_keys.Count);
            foreach (string key in _keys)
                list.Add(new KeyValuePair<string, JToken>(key, _dict[key]));
            return list;
        }

        public IEnumerator<KeyValuePair<string, JToken>> GetEnumerator()
        {
            return ToList().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public override JToken DeepClone()
        {
            var result = new JObject();
            foreach (string key in _keys)
                result[key] = _dict[key].DeepClone();
            return result;
        }
    }
}

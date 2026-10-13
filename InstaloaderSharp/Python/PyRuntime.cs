using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using InstaloaderSharp.Json;

namespace InstaloaderSharp.Python
{
    /// <summary>Python tuple.</summary>
    public sealed class PyTuple : IPyObject, IEnumerable<object>
    {
        public PyTuple(IEnumerable<object> items)
        {
            Items = items.ToList().AsReadOnly();
        }

        public IList<object> Items { get; private set; }

        public string PyStr() { return PyRepr(); }

        public string PyRepr()
        {
            if (Items.Count == 1)
                return "(" + Python.PyStr.Repr(Items[0]) + ",)";
            return "(" + string.Join(", ", Items.Select(Python.PyStr.Repr)) + ")";
        }

        public IEnumerator<object> GetEnumerator() { return Items.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }

    /// <summary>Python dict (insertion ordered, Python key equality).</summary>
    public sealed class PyDict : IPyObject
    {
        private readonly List<KeyValuePair<object, object>> _items = new List<KeyValuePair<object, object>>();

        public List<KeyValuePair<object, object>> Items { get { return _items; } }

        public int IndexOf(object key)
        {
            for (int i = 0; i < _items.Count; i++)
                if (PyRuntime.Eq(_items[i].Key, key))
                    return i;
            return -1;
        }

        public bool TryGet(object key, out object value)
        {
            int i = IndexOf(key);
            value = i >= 0 ? _items[i].Value : null;
            return i >= 0;
        }

        public void Set(object key, object value)
        {
            int i = IndexOf(key);
            if (i >= 0)
                _items[i] = new KeyValuePair<object, object>(_items[i].Key, value);
            else
                _items.Add(new KeyValuePair<object, object>(key, value));
        }

        public string PyStr() { return PyRepr(); }

        public string PyRepr()
        {
            return "{" + string.Join(", ", _items.Select(kv => Python.PyStr.Repr(kv.Key) + ": " + Python.PyStr.Repr(kv.Value))) + "}";
        }
    }

    /// <summary>A callable Python object (builtin function, bound method).</summary>
    public sealed class PyCallable : IPyObject
    {
        private readonly Func<List<object>, Dictionary<string, object>, object> _impl;

        public PyCallable(string name, Func<List<object>, Dictionary<string, object>, object> impl)
        {
            Name = name;
            _impl = impl;
        }

        public string Name { get; private set; }

        public object Call(List<object> args, Dictionary<string, object> kwargs)
        {
            return _impl(args, kwargs ?? new Dictionary<string, object>());
        }

        public string PyStr() { return PyRepr(); }

        public string PyRepr() { return string.Format("<built-in method {0}>", Name); }
    }

    /// <summary>Python iterator over a .NET enumerable (single pass).</summary>
    public sealed class PyIterator : IPyObject, IEnumerable<object>
    {
        private readonly IEnumerator _enumerator;

        public PyIterator(IEnumerable source)
        {
            _enumerator = source.GetEnumerator();
        }

        public IEnumerator<object> GetEnumerator()
        {
            while (_enumerator.MoveNext())
                yield return PyRuntime.ToPy(_enumerator.Current);
        }

        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }

        public string PyStr() { return PyRepr(); }

        public string PyRepr() { return "<generator object>"; }
    }

    /// <summary>The class datetime.datetime, which filter expressions may use.</summary>
    public sealed class PyDateTimeType : IPyObject
    {
        public static readonly PyDateTimeType Instance = new PyDateTimeType();

        private PyDateTimeType() { }

        public string PyStr() { return PyRepr(); }

        public string PyRepr() { return "<class 'datetime.datetime'>"; }
    }

    /// <summary>A datetime.timezone object (only as tzinfo of aware datetimes).</summary>
    public sealed class PyTimeZone : IPyObject
    {
        public PyTimeZone(TimeSpan offset, string name)
        {
            Offset = offset;
            Name = name;
        }

        public TimeSpan Offset { get; private set; }
        public string Name { get; private set; }

        public string PyStr()
        {
            if (Name != null)
                return Name;
            if (Offset == TimeSpan.Zero)
                return "UTC";
            TimeSpan abs = Offset.Duration();
            return string.Format(CultureInfo.InvariantCulture, "UTC{0}{1:00}:{2:00}", Offset < TimeSpan.Zero ? "-" : "+",
                                 (int)abs.TotalHours, abs.Minutes);
        }

        public string PyRepr()
        {
            return Offset == TimeSpan.Zero
                ? "datetime.timezone.utc"
                : string.Format(CultureInfo.InvariantCulture, "datetime.timezone(datetime.timedelta(seconds={0}))", (long)Offset.TotalSeconds);
        }
    }

    /// <summary>
    /// The dynamic semantics of Python on the values that filter expressions and format strings deal with:
    /// attribute access (mapping Python's snake_case names onto .NET members), subscripts, operators and calls.
    /// </summary>
    public static class PyRuntime
    {
        #region conversion

        /// <summary>Converts a .NET value to the value representation of this runtime.</summary>
        public static object ToPy(object v)
        {
            if (v == null)
                return null;
            if (v is string || v is bool || v is long || v is double || v is BigInteger || v is PyDateTime ||
                v is PyTimeDelta || v is IPyObject)
                return v;
            if (v is int || v is short || v is byte || v is sbyte || v is ushort || v is uint)
                return Convert.ToInt64(v, CultureInfo.InvariantCulture);
            if (v is ulong)
                return Normalize(new BigInteger((ulong)v));
            if (v is float || v is decimal)
                return Convert.ToDouble(v, CultureInfo.InvariantCulture);
            if (v is char)
                return v.ToString();
            if (v is DateTime || v is DateTimeOffset)
                return PyDateTime.FromObject(v);
            if (v is TimeSpan)
                return new PyTimeDelta((TimeSpan)v);
            var token = v as JToken;
            if (token != null)
                return FromJson(token);
            if (v is List<object>)
                return v;
            var dict = v as IDictionary;
            if (dict != null)
            {
                var d = new PyDict();
                foreach (DictionaryEntry kv in dict)
                    d.Set(ToPy(kv.Key), ToPy(kv.Value));
                return d;
            }
            var list = v as IList;
            if (list != null)
            {
                var result = new List<object>();
                foreach (object item in list)
                    result.Add(ToPy(item));
                return result;
            }
            var enumerable = v as IEnumerable;
            if (enumerable != null)
                return new PyIterator(enumerable);
            return v;
        }

        public static object FromJson(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Null: return null;
                case JTokenType.Boolean:
                case JTokenType.Integer:
                case JTokenType.Float:
                case JTokenType.String:
                    return token.Value;
                case JTokenType.Array:
                    {
                        var list = new List<object>();
                        foreach (JToken item in (JArray)token)
                            list.Add(FromJson(item));
                        return list;
                    }
                default:
                    {
                        var d = new PyDict();
                        foreach (var kv in (JObject)token)
                            d.Set(kv.Key, FromJson(kv.Value));
                        return d;
                    }
            }
        }

        public static object Normalize(BigInteger value)
        {
            if (value >= long.MinValue && value <= long.MaxValue)
                return (long)value;
            return value;
        }

        public static bool IsInt(object v)
        {
            return v is long || v is BigInteger || v is bool;
        }

        public static bool IsNumber(object v)
        {
            return IsInt(v) || v is double;
        }

        public static BigInteger ToBig(object v)
        {
            if (v is long) return (long)v;
            if (v is BigInteger) return (BigInteger)v;
            if (v is bool) return (bool)v ? 1 : 0;
            throw new PyTypeError(string.Format("'{0}' object cannot be interpreted as an integer", TypeName(v)));
        }

        public static double ToDouble(object v)
        {
            if (v is double) return (double)v;
            if (v is long) return (long)v;
            if (v is BigInteger) return (double)(BigInteger)v;
            if (v is bool) return (bool)v ? 1.0 : 0.0;
            throw new PyTypeError(string.Format("must be real number, not {0}", TypeName(v)));
        }

        public static int ToIndex(object v)
        {
            BigInteger b = ToBig(v);
            if (b > int.MaxValue || b < int.MinValue)
                throw new PyIndexError("cannot fit 'int' into an index-sized integer");
            return (int)b;
        }

        /// <summary>Python's type(v).__name__.</summary>
        public static string TypeName(object v)
        {
            if (v == null) return "NoneType";
            if (v is bool) return "bool";
            if (v is long || v is BigInteger) return "int";
            if (v is double) return "float";
            if (v is string) return "str";
            if (v is List<object>) return "list";
            if (v is PyTuple) return "tuple";
            if (v is PyDict) return "dict";
            if (v is PyDateTime) return "datetime";
            if (v is PyTimeDelta) return "timedelta";
            if (v is PyDateTimeType) return "type";
            if (v is PyCallable) return "builtin_function_or_method";
            if (v is PyIterator) return "generator";
            return v.GetType().Name;
        }

        #endregion

        #region truthiness and equality

        /// <summary>Python's bool(v).</summary>
        public static bool Truthy(object v)
        {
            if (v == null) return false;
            if (v is bool) return (bool)v;
            if (v is long) return (long)v != 0;
            if (v is BigInteger) return !((BigInteger)v).IsZero;
            if (v is double) return (double)v != 0.0;
            var s = v as string;
            if (s != null) return s.Length > 0;
            var list = v as List<object>;
            if (list != null) return list.Count > 0;
            var tuple = v as PyTuple;
            if (tuple != null) return tuple.Items.Count > 0;
            var dict = v as PyDict;
            if (dict != null) return dict.Items.Count > 0;
            var td = v as PyTimeDelta;
            if (td != null) return td.Ticks != 0;
            return true;
        }

        /// <summary>Python's ==.</summary>
        public static bool Eq(object a, object b)
        {
            if (a == null || b == null)
                return a == null && b == null;
            if (IsNumber(a) && IsNumber(b))
            {
                if (a is double || b is double)
                    return ToDouble(a) == ToDouble(b);
                return ToBig(a) == ToBig(b);
            }
            var sa = a as string;
            var sb = b as string;
            if (sa != null || sb != null)
                return sa != null && sb != null && string.Equals(sa, sb, StringComparison.Ordinal);
            var da = a as PyDateTime;
            var db = b as PyDateTime;
            if (da != null || db != null)
                return da != null && db != null && da.PyEquals(db);
            var ta = a as PyTimeDelta;
            var tb = b as PyTimeDelta;
            if (ta != null || tb != null)
                return ta != null && tb != null && ta.Ticks == tb.Ticks;
            var la = AsSequence(a);
            var lb = AsSequence(b);
            if (la != null && lb != null)
            {
                if ((a is PyTuple) != (b is PyTuple))
                    return false;
                if (la.Count != lb.Count)
                    return false;
                for (int i = 0; i < la.Count; i++)
                    if (!Eq(la[i], lb[i]))
                        return false;
                return true;
            }
            var dictA = a as PyDict;
            var dictB = b as PyDict;
            if (dictA != null && dictB != null)
            {
                if (dictA.Items.Count != dictB.Items.Count)
                    return false;
                foreach (var kv in dictA.Items)
                {
                    object other;
                    if (!dictB.TryGet(kv.Key, out other) || !Eq(kv.Value, other))
                        return false;
                }
                return true;
            }
            return a.Equals(b);
        }

        private static IList<object> AsSequence(object v)
        {
            var list = v as List<object>;
            if (list != null) return list;
            var tuple = v as PyTuple;
            if (tuple != null) return tuple.Items;
            return null;
        }

        /// <summary>Python's rich comparison (op is one of &lt; &lt;= &gt; &gt;=).</summary>
        public static int Compare(object a, object b, string op)
        {
            if (IsNumber(a) && IsNumber(b))
            {
                if (a is double || b is double)
                {
                    double x = ToDouble(a), y = ToDouble(b);
                    if (double.IsNaN(x) || double.IsNaN(y))
                        return int.MinValue; // all comparisons false
                    return x.CompareTo(y);
                }
                return ToBig(a).CompareTo(ToBig(b));
            }
            var sa = a as string;
            var sb = b as string;
            if (sa != null && sb != null)
                return PyStr.CompareCodePoints(sa, sb);
            var da = a as PyDateTime;
            var db = b as PyDateTime;
            if (da != null && db != null)
                return da.CompareTo(db);
            var ta = a as PyTimeDelta;
            var tb = b as PyTimeDelta;
            if (ta != null && tb != null)
                return ta.CompareTo(tb);
            var la = AsSequence(a);
            var lb = AsSequence(b);
            if (la != null && lb != null && (a is PyTuple) == (b is PyTuple))
            {
                for (int i = 0; i < Math.Min(la.Count, lb.Count); i++)
                {
                    if (!Eq(la[i], lb[i]))
                        return Compare(la[i], lb[i], op);
                }
                return la.Count.CompareTo(lb.Count);
            }
            throw new PyTypeError(string.Format("'{0}' not supported between instances of '{1}' and '{2}'",
                                                op, TypeName(a), TypeName(b)));
        }

        /// <summary>Python's <c>item in container</c>.</summary>
        public static bool Contains(object container, object item)
        {
            var s = container as string;
            if (s != null)
            {
                var si = item as string;
                if (si == null)
                    throw new PyTypeError(string.Format("'in <string>' requires string as left operand, not {0}", TypeName(item)));
                return s.IndexOf(si, StringComparison.Ordinal) >= 0;
            }
            var dict = container as PyDict;
            if (dict != null)
                return dict.IndexOf(item) >= 0;
            foreach (object element in Iterate(container))
                if (Eq(element, item))
                    return true;
            return false;
        }

        /// <summary>Python's iter(v).</summary>
        public static IEnumerable<object> Iterate(object v)
        {
            if (v == null)
                throw new PyTypeError("'NoneType' object is not iterable");
            var s = v as string;
            if (s != null)
                return PyStr.CodePoints(s).Cast<object>();
            var list = v as List<object>;
            if (list != null)
                return list.ToList();
            var tuple = v as PyTuple;
            if (tuple != null)
                return tuple.Items;
            var dict = v as PyDict;
            if (dict != null)
                return dict.Items.Select(kv => kv.Key).ToList();
            var iterator = v as PyIterator;
            if (iterator != null)
                return iterator;
            var enumerable = v as IEnumerable;
            if (enumerable != null && !(v is IPyObject))
                return new PyIterator(enumerable);
            throw new PyTypeError(string.Format("'{0}' object is not iterable", TypeName(v)));
        }

        #endregion

        #region attributes

        private static string NormalizeName(string name)
        {
            return name.Replace("_", "").ToLowerInvariant();
        }

        private sealed class MemberTable
        {
            public readonly Dictionary<string, PropertyInfo> Properties = new Dictionary<string, PropertyInfo>();
            public readonly Dictionary<string, List<MethodInfo>> Methods = new Dictionary<string, List<MethodInfo>>();
        }

        private static readonly Dictionary<Type, MemberTable> MemberTables = new Dictionary<Type, MemberTable>();

        private static MemberTable GetMembers(Type type)
        {
            lock (MemberTables)
            {
                MemberTable table;
                if (MemberTables.TryGetValue(type, out table))
                    return table;
                table = new MemberTable();
                const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;
                foreach (PropertyInfo p in type.GetProperties(flags))
                {
                    if (p.GetIndexParameters().Length > 0 || p.GetGetMethod() == null)
                        continue;
                    string key = NormalizeName(p.Name);
                    // Prefer the most derived declaration
                    if (!table.Properties.ContainsKey(key) || p.DeclaringType == type)
                        table.Properties[key] = p;
                }
                foreach (MethodInfo m in type.GetMethods(flags))
                {
                    if (m.IsSpecialName || m.DeclaringType == typeof(object) || m.IsGenericMethodDefinition)
                        continue;
                    if (m.Name == "PyStr" || m.Name == "PyRepr" || m.Name == "GetHashCode" || m.Name == "Equals" ||
                        m.Name == "ToString" || m.Name == "GetType")
                        continue;
                    string key = NormalizeName(m.Name);
                    List<MethodInfo> list;
                    if (!table.Methods.TryGetValue(key, out list))
                        table.Methods[key] = list = new List<MethodInfo>();
                    list.Add(m);
                }
                MemberTables[type] = table;
                return table;
            }
        }

        /// <summary>Python's hasattr(cls, name) for a structure class (properties and methods).</summary>
        public static bool TypeHasAttr(Type type, string name)
        {
            if (name.StartsWith("_"))
                return false;
            MemberTable table = GetMembers(type);
            string key = NormalizeName(name);
            return table.Properties.ContainsKey(key) || table.Methods.ContainsKey(key);
        }

        private static object Invoke(MethodBase method, object target, object[] args)
        {
            try
            {
                return method.Invoke(target, args);
            }
            catch (TargetInvocationException ex)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        /// <summary>Python's hasattr(obj, name).</summary>
        public static bool HasAttr(object obj, string name)
        {
            try
            {
                GetAttr(obj, name);
                return true;
            }
            catch (PyAttributeError)
            {
                return false;
            }
        }

        /// <summary>Python's getattr(obj, name).</summary>
        public static object GetAttr(object obj, string name)
        {
            object result;
            if (PyBuiltins.TryGetAttr(obj, name, out result))
                return result;
            if (obj == null || obj is string || obj is bool || obj is long || obj is double || obj is BigInteger ||
                obj is List<object> || obj is PyTuple || obj is PyDict || obj is PyDateTime || obj is PyTimeDelta ||
                obj is PyDateTimeType || name.StartsWith("_"))
                throw new PyAttributeError(string.Format("'{0}' object has no attribute {1}", TypeName(obj), PyStr.Repr(name)));
            MemberTable table = GetMembers(obj.GetType());
            string key = NormalizeName(name);
            PropertyInfo property;
            if (table.Properties.TryGetValue(key, out property))
                return ToPy(Invoke(property.GetGetMethod(), property.GetGetMethod().IsStatic ? null : obj, new object[0]));
            List<MethodInfo> methods;
            if (table.Methods.TryGetValue(key, out methods))
            {
                object target = obj;
                return new PyCallable(name, (args, kwargs) => CallMethod(methods, target, args, kwargs, name));
            }
            throw new PyAttributeError(string.Format("'{0}' object has no attribute {1}", TypeName(obj), PyStr.Repr(name)));
        }

        private static object CallMethod(List<MethodInfo> methods, object target, List<object> args,
                                         Dictionary<string, object> kwargs, string name)
        {
            foreach (MethodInfo m in methods.OrderBy(m => m.GetParameters().Length))
            {
                ParameterInfo[] parameters = m.GetParameters();
                if (args.Count > parameters.Length)
                    continue;
                var converted = new object[parameters.Length];
                bool ok = true;
                for (int i = 0; i < parameters.Length && ok; i++)
                {
                    object value;
                    string pyName = parameters[i].Name;
                    bool found = false;
                    if (i < args.Count)
                    {
                        value = args[i];
                        found = true;
                    }
                    else
                    {
                        value = null;
                        foreach (var kv in kwargs)
                        {
                            if (NormalizeName(kv.Key) == NormalizeName(pyName))
                            {
                                value = kv.Value;
                                found = true;
                            }
                        }
                    }
                    if (!found)
                    {
                        if (parameters[i].IsOptional)
                        {
                            converted[i] = parameters[i].DefaultValue;
                            continue;
                        }
                        ok = false;
                        break;
                    }
                    object c;
                    if (!TryConvert(value, parameters[i].ParameterType, out c))
                    {
                        ok = false;
                        break;
                    }
                    converted[i] = c;
                }
                if (ok)
                    return ToPy(Invoke(m, m.IsStatic ? null : target, converted));
            }
            throw new PyTypeError(string.Format("{0}() got invalid arguments", name));
        }

        private static bool TryConvert(object value, Type type, out object result)
        {
            result = null;
            Type underlying = Nullable.GetUnderlyingType(type) ?? type;
            if (value == null)
                return !type.IsValueType || Nullable.GetUnderlyingType(type) != null;
            if (underlying.IsInstanceOfType(value))
            {
                result = value;
                return true;
            }
            try
            {
                if (underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(short))
                {
                    if (!IsInt(value)) return false;
                    result = Convert.ChangeType((long)ToBig(value), underlying, CultureInfo.InvariantCulture);
                    return true;
                }
                if (underlying == typeof(double) || underlying == typeof(float))
                {
                    if (!IsNumber(value)) return false;
                    result = Convert.ChangeType(ToDouble(value), underlying, CultureInfo.InvariantCulture);
                    return true;
                }
                if (underlying == typeof(BigInteger))
                {
                    if (!IsInt(value)) return false;
                    result = ToBig(value);
                    return true;
                }
                if (underlying == typeof(string))
                {
                    result = value as string;
                    return result != null;
                }
                if (underlying == typeof(object))
                {
                    result = value;
                    return true;
                }
            }
            catch (OverflowException)
            {
                return false;
            }
            return false;
        }

        #endregion

        #region subscripts

        /// <summary>Python's obj[key] (key may be a slice given as PySlice).</summary>
        public static object GetItem(object obj, object key)
        {
            var slice = key as PySlice;
            var s = obj as string;
            if (s != null)
            {
                if (slice != null)
                    return string.Concat(slice.Apply(PyStr.CodePoints(s)));
                if (!IsInt(key))
                    throw new PyTypeError(string.Format("string indices must be integers, not '{0}'", TypeName(key)));
                List<string> cps = PyStr.CodePoints(s);
                return cps[NormalizeIndex(ToIndex(key), cps.Count, "string index out of range")];
            }
            IList<object> seq = AsSequence(obj);
            if (seq != null)
            {
                if (slice != null)
                {
                    List<object> items = slice.Apply(seq.ToList());
                    return obj is PyTuple ? (object)new PyTuple(items) : items;
                }
                if (!IsInt(key))
                    throw new PyTypeError(string.Format("{0} indices must be integers or slices, not {1}",
                                                        obj is PyTuple ? "tuple" : "list", TypeName(key)));
                return seq[NormalizeIndex(ToIndex(key), seq.Count, (obj is PyTuple ? "tuple" : "list") + " index out of range")];
            }
            var dict = obj as PyDict;
            if (dict != null)
            {
                object value;
                if (dict.TryGet(key, out value))
                    return value;
                throw new PyKeyError(key);
            }
            throw new PyTypeError(string.Format("'{0}' object is not subscriptable", TypeName(obj)));
        }

        private static int NormalizeIndex(int index, int count, string message)
        {
            int i = index < 0 ? index + count : index;
            if (i < 0 || i >= count)
                throw new PyIndexError(message);
            return i;
        }

        #endregion

        #region operators

        public static object Add(object a, object b)
        {
            if (IsNumber(a) && IsNumber(b))
            {
                if (a is double || b is double)
                    return ToDouble(a) + ToDouble(b);
                return Normalize(ToBig(a) + ToBig(b));
            }
            if (a is string && b is string)
                return (string)a + (string)b;
            if (a is List<object> && b is List<object>)
                return ((List<object>)a).Concat((List<object>)b).ToList();
            if (a is PyTuple && b is PyTuple)
                return new PyTuple(((PyTuple)a).Items.Concat(((PyTuple)b).Items));
            if (a is PyDateTime && b is PyTimeDelta)
                return ((PyDateTime)a).Add((PyTimeDelta)b);
            if (a is PyTimeDelta && b is PyDateTime)
                return ((PyDateTime)b).Add((PyTimeDelta)a);
            if (a is PyTimeDelta && b is PyTimeDelta)
                return new PyTimeDelta(((PyTimeDelta)a).Ticks + ((PyTimeDelta)b).Ticks);
            throw BinaryTypeError("+", a, b);
        }

        public static object Sub(object a, object b)
        {
            if (IsNumber(a) && IsNumber(b))
            {
                if (a is double || b is double)
                    return ToDouble(a) - ToDouble(b);
                return Normalize(ToBig(a) - ToBig(b));
            }
            if (a is PyDateTime && b is PyDateTime)
                return ((PyDateTime)a).Subtract((PyDateTime)b);
            if (a is PyDateTime && b is PyTimeDelta)
                return ((PyDateTime)a).Add(new PyTimeDelta(-((PyTimeDelta)b).Ticks));
            if (a is PyTimeDelta && b is PyTimeDelta)
                return new PyTimeDelta(((PyTimeDelta)a).Ticks - ((PyTimeDelta)b).Ticks);
            throw BinaryTypeError("-", a, b);
        }

        private static object Repeat(object seq, object count)
        {
            int n = Math.Max(0, ToIndex(count));
            var s = seq as string;
            if (s != null)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < n; i++)
                    sb.Append(s);
                return sb.ToString();
            }
            IList<object> items = AsSequence(seq);
            var result = new List<object>();
            for (int i = 0; i < n; i++)
                result.AddRange(items);
            return seq is PyTuple ? (object)new PyTuple(result) : result;
        }

        public static object Mul(object a, object b)
        {
            if (IsNumber(a) && IsNumber(b))
            {
                if (a is double || b is double)
                    return ToDouble(a) * ToDouble(b);
                return Normalize(ToBig(a) * ToBig(b));
            }
            if ((a is string || AsSequence(a) != null) && IsInt(b))
                return Repeat(a, b);
            if ((b is string || AsSequence(b) != null) && IsInt(a))
                return Repeat(b, a);
            if (a is PyTimeDelta && IsNumber(b))
                return new PyTimeDelta((long)(((PyTimeDelta)a).Ticks * ToDouble(b)));
            if (b is PyTimeDelta && IsNumber(a))
                return new PyTimeDelta((long)(((PyTimeDelta)b).Ticks * ToDouble(a)));
            throw BinaryTypeError("*", a, b);
        }

        public static object TrueDiv(object a, object b)
        {
            if (IsNumber(a) && IsNumber(b))
            {
                double y = ToDouble(b);
                if (y == 0.0)
                    throw new PyZeroDivisionError(IsInt(a) && IsInt(b) ? "division by zero" : "float division by zero");
                if (IsInt(a) && IsInt(b))
                    return (double)ToBig(a) / (double)ToBig(b);
                return ToDouble(a) / y;
            }
            if (a is PyTimeDelta && b is PyTimeDelta)
            {
                long t = ((PyTimeDelta)b).Ticks;
                if (t == 0)
                    throw new PyZeroDivisionError("division by zero");
                return (double)((PyTimeDelta)a).Ticks / t;
            }
            if (a is PyTimeDelta && IsNumber(b))
                return new PyTimeDelta((long)(((PyTimeDelta)a).Ticks / ToDouble(b)));
            throw BinaryTypeError("/", a, b);
        }

        public static object FloorDiv(object a, object b)
        {
            if (IsNumber(a) && IsNumber(b))
            {
                if (a is double || b is double)
                {
                    double y = ToDouble(b);
                    if (y == 0.0)
                        throw new PyZeroDivisionError("float floor division by zero");
                    return Math.Floor(ToDouble(a) / y);
                }
                BigInteger bx = ToBig(a), by = ToBig(b);
                if (by.IsZero)
                    throw new PyZeroDivisionError("integer division or modulo by zero");
                BigInteger q = BigInteger.Divide(bx, by);
                if ((bx % by != 0) && ((bx < 0) != (by < 0)))
                    q -= 1;
                return Normalize(q);
            }
            throw BinaryTypeError("//", a, b);
        }

        public static object Mod(object a, object b)
        {
            if (IsNumber(a) && IsNumber(b))
            {
                if (a is double || b is double)
                {
                    double x = ToDouble(a), y = ToDouble(b);
                    if (y == 0.0)
                        throw new PyZeroDivisionError("float modulo");
                    double m = x % y;
                    if (m != 0 && ((m < 0) != (y < 0)))
                        m += y;
                    return m;
                }
                BigInteger bx = ToBig(a), by = ToBig(b);
                if (by.IsZero)
                    throw new PyZeroDivisionError("integer division or modulo by zero");
                BigInteger r = bx % by;
                if (r != 0 && ((r < 0) != (by < 0)))
                    r += by;
                return Normalize(r);
            }
            if (a is string)
                return PrintfFormat((string)a, b);
            throw BinaryTypeError("%", a, b);
        }

        /// <summary>printf-style string formatting: format % values.</summary>
        public static string PrintfFormat(string format, object values)
        {
            var tuple = values as PyTuple;
            IList<object> args = tuple != null ? tuple.Items : new List<object> { values };
            var mapping = values as PyDict;
            int argIndex = 0;
            Func<object> nextArg = () =>
            {
                if (argIndex >= args.Count)
                    throw new PyTypeError("not enough arguments for format string");
                return args[argIndex++];
            };
            var sb = new StringBuilder();
            int i = 0;
            while (i < format.Length)
            {
                char c = format[i++];
                if (c != '%')
                {
                    sb.Append(c);
                    continue;
                }
                if (i >= format.Length)
                    throw new PyValueError("incomplete format");
                object value = null;
                bool haveValue = false;
                if (format[i] == '(')
                {
                    int close = format.IndexOf(')', i);
                    if (close < 0)
                        throw new PyValueError("incomplete format key");
                    if (mapping == null)
                        throw new PyTypeError("format requires a mapping");
                    value = GetItem(mapping, format.Substring(i + 1, close - i - 1));
                    haveValue = true;
                    i = close + 1;
                }
                string flags = "";
                while (i < format.Length && "-+ #0".IndexOf(format[i]) >= 0)
                    flags += format[i++];
                string width = "";
                if (i < format.Length && format[i] == '*')
                {
                    width = ToIndex(nextArg()).ToString(CultureInfo.InvariantCulture);
                    i++;
                }
                while (i < format.Length && char.IsDigit(format[i]))
                    width += format[i++];
                string precision = null;
                if (i < format.Length && format[i] == '.')
                {
                    i++;
                    precision = "";
                    if (i < format.Length && format[i] == '*')
                    {
                        precision = ToIndex(nextArg()).ToString(CultureInfo.InvariantCulture);
                        i++;
                    }
                    while (i < format.Length && char.IsDigit(format[i]))
                        precision += format[i++];
                    if (precision.Length == 0)
                        precision = "0";
                }
                while (i < format.Length && "hlL".IndexOf(format[i]) >= 0)
                    i++;
                if (i >= format.Length)
                    throw new PyValueError("incomplete format");
                char type = format[i++];
                if (type == '%')
                {
                    sb.Append('%');
                    continue;
                }
                if (!haveValue)
                    value = nextArg();
                string align = flags.Contains("-") ? "<" : ">";
                string spec;
                switch (type)
                {
                    case 's':
                    case 'r':
                    case 'a':
                        {
                            string s = type == 's' ? PyStr.Str(value) : PyStr.Repr(value);
                            spec = align + width + (precision != null ? "." + precision : "");
                            sb.Append(PyFormat.FormatString(s, spec));
                            continue;
                        }
                    case 'c':
                        {
                            string s = value is string ? (string)value : char.ConvertFromUtf32(ToIndex(value));
                            sb.Append(PyFormat.FormatString(s, align + width));
                            continue;
                        }
                    case 'd':
                    case 'i':
                    case 'u':
                        if (!IsNumber(value))
                            throw new PyTypeError(string.Format("%{0} format: a real number is required, not {1}", type, TypeName(value)));
                        value = value is double ? (object)Normalize(new BigInteger(Math.Truncate((double)value))) : value;
                        type = 'd';
                        break;
                    case 'x':
                    case 'X':
                    case 'o':
                        if (!IsInt(value))
                            throw new PyTypeError(string.Format("%{0} format: an integer is required, not {1}", type, TypeName(value)));
                        break;
                    case 'e':
                    case 'E':
                    case 'f':
                    case 'F':
                    case 'g':
                    case 'G':
                        if (!IsNumber(value))
                            throw new PyTypeError(string.Format("must be real number, not {0}", TypeName(value)));
                        value = ToDouble(value);
                        if (precision == null)
                            precision = "6";
                        break;
                    default:
                        throw new PyValueError(string.Format("unsupported format character '{0}'", type));
                }
                string sign = flags.Contains("+") ? "+" : flags.Contains(" ") ? " " : "";
                bool zero = flags.Contains("0") && !flags.Contains("-");
                spec = (zero ? "" : align) + sign + (flags.Contains("#") ? "#" : "") + (zero ? "0" : "") + width +
                       (precision != null && type != 'd' && type != 'x' && type != 'X' && type != 'o' ? "." + precision : "") + type;
                sb.Append(PyFormat.Format(value, spec));
            }
            if (mapping == null && argIndex < args.Count)
                throw new PyTypeError("not all arguments converted during string formatting");
            return sb.ToString();
        }

        public static object Pow(object a, object b)
        {
            if (IsNumber(a) && IsNumber(b))
            {
                if (IsInt(a) && IsInt(b))
                {
                    BigInteger exp = ToBig(b);
                    if (exp >= 0)
                    {
                        if (exp > 100000)
                            throw new PyOverflowError("exponent too large");
                        return Normalize(BigInteger.Pow(ToBig(a), (int)exp));
                    }
                }
                double x = ToDouble(a), y = ToDouble(b);
                if (x == 0.0 && y < 0)
                    throw new PyZeroDivisionError("zero to a negative power");
                return Math.Pow(x, y);
            }
            throw BinaryTypeError("** or pow()", a, b);
        }

        public static object BitOp(string op, object a, object b)
        {
            if (a is bool && b is bool)
            {
                bool x = (bool)a, y = (bool)b;
                switch (op)
                {
                    case "&": return x & y;
                    case "|": return x | y;
                    case "^": return x ^ y;
                }
            }
            if (IsInt(a) && IsInt(b))
            {
                BigInteger x = ToBig(a), y = ToBig(b);
                switch (op)
                {
                    case "&": return Normalize(x & y);
                    case "|": return Normalize(x | y);
                    case "^": return Normalize(x ^ y);
                    case "<<":
                        if (y < 0) throw new PyValueError("negative shift count");
                        return Normalize(x << (int)y);
                    case ">>":
                        if (y < 0) throw new PyValueError("negative shift count");
                        return Normalize(x >> (int)y);
                }
            }
            throw BinaryTypeError(op, a, b);
        }

        public static object Neg(object a)
        {
            if (a is double) return -(double)a;
            if (IsInt(a)) return Normalize(-ToBig(a));
            if (a is PyTimeDelta) return new PyTimeDelta(-((PyTimeDelta)a).Ticks);
            throw new PyTypeError(string.Format("bad operand type for unary -: '{0}'", TypeName(a)));
        }

        public static object Pos(object a)
        {
            if (a is double) return a;
            if (IsInt(a)) return Normalize(ToBig(a));
            if (a is PyTimeDelta) return a;
            throw new PyTypeError(string.Format("bad operand type for unary +: '{0}'", TypeName(a)));
        }

        public static object Invert(object a)
        {
            if (IsInt(a)) return Normalize(-ToBig(a) - 1);
            throw new PyTypeError(string.Format("bad operand type for unary ~: '{0}'", TypeName(a)));
        }

        private static PyTypeError BinaryTypeError(string op, object a, object b)
        {
            if (op == "+" && a is string)
                return new PyTypeError(string.Format("can only concatenate str (not \"{0}\") to str", TypeName(b)));
            return new PyTypeError(string.Format("unsupported operand type(s) for {0}: '{1}' and '{2}'", op, TypeName(a), TypeName(b)));
        }

        /// <summary>Python's obj(*args, **kwargs).</summary>
        public static object Call(object callable, List<object> args, Dictionary<string, object> kwargs)
        {
            var c = callable as PyCallable;
            if (c != null)
                return c.Call(args, kwargs);
            if (callable is PyDateTimeType)
                return PyBuiltins.DateTimeConstructor(args, kwargs);
            throw new PyTypeError(string.Format("'{0}' object is not callable", TypeName(callable)));
        }

        #endregion
    }

    /// <summary>A slice object (start:stop:step).</summary>
    public sealed class PySlice
    {
        public PySlice(object start, object stop, object step)
        {
            Start = start;
            Stop = stop;
            Step = step;
        }

        public object Start { get; private set; }
        public object Stop { get; private set; }
        public object Step { get; private set; }

        public List<T> Apply<T>(List<T> items)
        {
            int n = items.Count;
            int step = Step == null ? 1 : PyRuntime.ToIndex(Step);
            if (step == 0)
                throw new PyValueError("slice step cannot be zero");
            int start, stop;
            if (step > 0)
            {
                start = Start == null ? 0 : Clamp(PyRuntime.ToIndex(Start), n, 0, n);
                stop = Stop == null ? n : Clamp(PyRuntime.ToIndex(Stop), n, 0, n);
            }
            else
            {
                start = Start == null ? n - 1 : Clamp(PyRuntime.ToIndex(Start), n, -1, n - 1);
                stop = Stop == null ? -1 : Clamp(PyRuntime.ToIndex(Stop), n, -1, n - 1);
            }
            var result = new List<T>();
            if (step > 0)
                for (int i = start; i < stop; i += step)
                    result.Add(items[i]);
            else
                for (int i = start; i > stop; i += step)
                    result.Add(items[i]);
            return result;
        }

        private static int Clamp(int index, int n, int min, int max)
        {
            if (index < 0)
                index += n;
            if (index < min) return min;
            if (index > max) return max;
            return index;
        }
    }
}

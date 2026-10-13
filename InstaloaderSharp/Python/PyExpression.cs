using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;

namespace InstaloaderSharp.Python
{
    /// <summary>Raised for syntax errors in Python expressions.</summary>
    public class PySyntaxError : PyException
    {
        public PySyntaxError(string message) : base(message) { }
        public override string PythonName { get { return "SyntaxError"; } }
    }

    /// <summary>
    /// A Python expression (as accepted by eval()) in which every name except <c>datetime</c> refers to an
    /// attribute of an item, as Instaloader's --post-filter and --storyitem-filter work.
    /// </summary>
    public sealed class PyExpression
    {
        private readonly Node _root;

        /// <summary>The item attributes referenced by the expression, in order of appearance.</summary>
        public List<string> Names { get; private set; }

        private PyExpression(Node root, List<string> names)
        {
            _root = root;
            Names = names;
        }

        /// <summary>Parses an expression.</summary>
        /// <exception cref="PySyntaxError">If the expression is not valid Python.</exception>
        /// <exception cref="InvalidArgumentException">If the expression assigns variables.</exception>
        public static PyExpression Parse(string source)
        {
            var names = new List<string>();
            var parser = new Parser(source, names);
            Node root = parser.ParseTopLevel();
            return new PyExpression(root, names);
        }

        /// <summary>Evaluates the expression with the given item.</summary>
        public object Evaluate(object item)
        {
            return _root.Eval(item);
        }

        #region tokenizer

        private enum TokenKind
        {
            Name,
            Number,
            String,
            Op,
            End
        }

        private sealed class StringPart
        {
            public string Text;
            public bool IsFString;
            public bool IsRaw;
        }

        private sealed class Token
        {
            public TokenKind Kind;
            public string Text;
            public object Value;
            public StringPart Str;
            public int Pos;

            public override string ToString() { return Text; }
        }

        private static readonly string[] Operators =
        {
            "**=", "//=", ">>=", "<<=", "...", "**", "//", "<<", ">>", "<=", ">=", "==", "!=", ":=", "->", "+=", "-=",
            "*=", "/=", "%=", "&=", "|=", "^=", "@=",
            "+", "-", "*", "/", "%", "@", "&", "|", "^", "~", "<", ">", "(", ")", "[", "]", "{", "}", ",", ":", ".",
            ";", "=", "!"
        };

        private static List<Token> Tokenize(string s)
        {
            var tokens = new List<Token>();
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f')
                {
                    i++;
                    continue;
                }
                if (c == '\\' && i + 1 < s.Length && (s[i + 1] == '\n' || s[i + 1] == '\r'))
                {
                    i += 2;
                    continue;
                }
                if (c == '#')
                {
                    while (i < s.Length && s[i] != '\n')
                        i++;
                    continue;
                }
                int start = i;
                if (char.IsLetter(c) || c == '_' || c > 0x7F && char.IsLetterOrDigit(c))
                {
                    while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_'))
                        i++;
                    string word = s.Substring(start, i - start);
                    // string prefix?
                    if (i < s.Length && (s[i] == '\'' || s[i] == '"') && IsStringPrefix(word))
                    {
                        tokens.Add(ReadString(s, ref i, word, start));
                        continue;
                    }
                    tokens.Add(new Token { Kind = TokenKind.Name, Text = word, Pos = start });
                    continue;
                }
                if (char.IsDigit(c) || (c == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1])))
                {
                    tokens.Add(ReadNumber(s, ref i));
                    continue;
                }
                if (c == '\'' || c == '"')
                {
                    tokens.Add(ReadString(s, ref i, "", start));
                    continue;
                }
                string op = null;
                foreach (string candidate in Operators)
                {
                    if (string.CompareOrdinal(s, i, candidate, 0, candidate.Length) == 0)
                    {
                        op = candidate;
                        break;
                    }
                }
                if (op == null)
                    throw new PySyntaxError(string.Format("invalid character '{0}' (U+{1:X4})", c, (int)c));
                tokens.Add(new Token { Kind = TokenKind.Op, Text = op, Pos = start });
                i += op.Length;
            }
            tokens.Add(new Token { Kind = TokenKind.End, Text = "", Pos = s.Length });
            return tokens;
        }

        private static bool IsStringPrefix(string word)
        {
            switch (word.ToLowerInvariant())
            {
                case "r":
                case "u":
                case "f":
                case "b":
                case "rb":
                case "br":
                case "fr":
                case "rf":
                    return true;
            }
            return false;
        }

        private static Token ReadString(string s, ref int i, string prefix, int start)
        {
            string lower = prefix.ToLowerInvariant();
            if (lower.Contains("b"))
                throw new PySyntaxError("bytes literals are not supported in filters");
            bool raw = lower.Contains("r");
            bool fstring = lower.Contains("f");
            char quote = s[i];
            bool triple = i + 2 < s.Length && s[i + 1] == quote && s[i + 2] == quote;
            int bodyStart = i + (triple ? 3 : 1);
            int j = bodyStart;
            while (true)
            {
                if (j >= s.Length)
                    throw new PySyntaxError("unterminated string literal");
                char c = s[j];
                if (c == '\\')
                {
                    j += 2;
                    continue;
                }
                if (!triple && (c == '\n' || c == '\r'))
                    throw new PySyntaxError("unterminated string literal");
                if (c == quote && (!triple || (j + 2 < s.Length && s[j + 1] == quote && s[j + 2] == quote)))
                    break;
                j++;
            }
            string body = s.Substring(bodyStart, j - bodyStart);
            i = j + (triple ? 3 : 1);
            return new Token
            {
                Kind = TokenKind.String,
                Text = s.Substring(start, i - start),
                Pos = start,
                Str = new StringPart { Text = body, IsFString = fstring, IsRaw = raw }
            };
        }

        private static Token ReadNumber(string s, ref int i)
        {
            int start = i;
            if (s[i] == '0' && i + 1 < s.Length && "xXoObB".IndexOf(s[i + 1]) >= 0)
            {
                char kind = char.ToLowerInvariant(s[i + 1]);
                i += 2;
                int digitsStart = i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_'))
                    i++;
                string digits = s.Substring(digitsStart, i - digitsStart).Replace("_", "");
                int radix = kind == 'x' ? 16 : kind == 'o' ? 8 : 2;
                BigInteger value = BigInteger.Zero;
                if (digits.Length == 0)
                    throw new PySyntaxError("invalid syntax");
                foreach (char d in digits.ToLowerInvariant())
                {
                    int v = d >= '0' && d <= '9' ? d - '0' : d >= 'a' && d <= 'f' ? d - 'a' + 10 : 99;
                    if (v >= radix)
                        throw new PySyntaxError(string.Format("invalid digit '{0}' in literal", d));
                    value = value * radix + v;
                }
                return new Token { Kind = TokenKind.Number, Text = s.Substring(start, i - start), Value = PyRuntime.Normalize(value), Pos = start };
            }
            bool isFloat = false;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '_'))
                i++;
            if (i < s.Length && s[i] == '.')
            {
                isFloat = true;
                i++;
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '_'))
                    i++;
            }
            if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
            {
                int save = i;
                i++;
                if (i < s.Length && (s[i] == '+' || s[i] == '-'))
                    i++;
                if (i < s.Length && char.IsDigit(s[i]))
                {
                    isFloat = true;
                    while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '_'))
                        i++;
                }
                else
                {
                    i = save;
                }
            }
            if (i < s.Length && (s[i] == 'j' || s[i] == 'J'))
                throw new PySyntaxError("complex numbers are not supported in filters");
            string text = s.Substring(start, i - start);
            string clean = text.Replace("_", "");
            object val;
            if (isFloat)
                val = double.Parse(clean, NumberStyles.Float, CultureInfo.InvariantCulture);
            else
            {
                if (clean.Length > 1 && clean[0] == '0' && clean.Any(ch => ch != '0'))
                    throw new PySyntaxError("leading zeros in decimal integer literals are not permitted");
                val = PyRuntime.Normalize(BigInteger.Parse(clean, CultureInfo.InvariantCulture));
            }
            return new Token { Kind = TokenKind.Number, Text = text, Value = val, Pos = start };
        }

        #endregion

        #region AST

        private abstract class Node
        {
            public abstract object Eval(object item);
        }

        private sealed class Constant : Node
        {
            public object Value;
            public override object Eval(object item) { return Value; }
        }

        private sealed class ItemAttribute : Node
        {
            public string Name;
            public override object Eval(object item) { return PyRuntime.GetAttr(item, Name); }
        }

        private sealed class DateTimeName : Node
        {
            public override object Eval(object item) { return PyDateTimeType.Instance; }
        }

        private sealed class Attribute : Node
        {
            public Node Value;
            public string Name;
            public override object Eval(object item) { return PyRuntime.GetAttr(Value.Eval(item), Name); }
        }

        private sealed class Subscript : Node
        {
            public Node Value;
            public Node Key;
            public override object Eval(object item) { return PyRuntime.GetItem(Value.Eval(item), Key.Eval(item)); }
        }

        private sealed class SliceNode : Node
        {
            public Node Start, Stop, Step;

            public override object Eval(object item)
            {
                return new PySlice(Start == null ? null : Start.Eval(item), Stop == null ? null : Stop.Eval(item),
                                   Step == null ? null : Step.Eval(item));
            }
        }

        private sealed class Argument
        {
            public string Keyword;
            public bool Star;
            public bool DoubleStar;
            public Node Value;
        }

        private sealed class Call : Node
        {
            public Node Func;
            public List<Argument> Args;

            public override object Eval(object item)
            {
                object func = Func.Eval(item);
                var args = new List<object>();
                var kwargs = new Dictionary<string, object>();
                foreach (Argument a in Args)
                {
                    object v = a.Value.Eval(item);
                    if (a.Star)
                        args.AddRange(PyRuntime.Iterate(v));
                    else if (a.DoubleStar)
                    {
                        var d = v as PyDict;
                        if (d == null)
                            throw new PyTypeError("argument after ** must be a mapping");
                        foreach (var kv in d.Items)
                            kwargs[(string)kv.Key] = kv.Value;
                    }
                    else if (a.Keyword != null)
                        kwargs[a.Keyword] = v;
                    else
                        args.Add(v);
                }
                return PyRuntime.Call(func, args, kwargs);
            }
        }

        private sealed class BinOp : Node
        {
            public string Op;
            public Node Left, Right;

            public override object Eval(object item)
            {
                object a = Left.Eval(item);
                object b = Right.Eval(item);
                switch (Op)
                {
                    case "+": return PyRuntime.Add(a, b);
                    case "-": return PyRuntime.Sub(a, b);
                    case "*": return PyRuntime.Mul(a, b);
                    case "/": return PyRuntime.TrueDiv(a, b);
                    case "//": return PyRuntime.FloorDiv(a, b);
                    case "%": return PyRuntime.Mod(a, b);
                    case "**": return PyRuntime.Pow(a, b);
                    case "@": throw new PyTypeError("unsupported operand type(s) for @");
                    default: return PyRuntime.BitOp(Op, a, b);
                }
            }
        }

        private sealed class UnaryOp : Node
        {
            public string Op;
            public Node Operand;

            public override object Eval(object item)
            {
                object v = Operand.Eval(item);
                switch (Op)
                {
                    case "not": return !PyRuntime.Truthy(v);
                    case "-": return PyRuntime.Neg(v);
                    case "+": return PyRuntime.Pos(v);
                    default: return PyRuntime.Invert(v);
                }
            }
        }

        private sealed class BoolOp : Node
        {
            public bool IsAnd;
            public List<Node> Values;

            public override object Eval(object item)
            {
                object v = null;
                foreach (Node n in Values)
                {
                    v = n.Eval(item);
                    bool t = PyRuntime.Truthy(v);
                    if (IsAnd ? !t : t)
                        return v;
                }
                return v;
            }
        }

        private sealed class CompareNode : Node
        {
            public Node Left;
            public List<string> Ops;
            public List<Node> Comparators;

            public override object Eval(object item)
            {
                object a = Left.Eval(item);
                for (int i = 0; i < Ops.Count; i++)
                {
                    object b = Comparators[i].Eval(item);
                    if (!CompareOne(Ops[i], a, b))
                        return false;
                    a = b;
                }
                return true;
            }

            private static bool CompareOne(string op, object a, object b)
            {
                switch (op)
                {
                    case "==": return PyRuntime.Eq(a, b);
                    case "!=": return !PyRuntime.Eq(a, b);
                    case "in": return PyRuntime.Contains(b, a);
                    case "not in": return !PyRuntime.Contains(b, a);
                    case "is": return Is(a, b);
                    case "is not": return !Is(a, b);
                }
                int c = PyRuntime.Compare(a, b, op);
                if (c == int.MinValue)
                    return false;
                switch (op)
                {
                    case "<": return c < 0;
                    case "<=": return c <= 0;
                    case ">": return c > 0;
                    default: return c >= 0;
                }
            }

            private static bool Is(object a, object b)
            {
                if (a == null || b == null)
                    return a == null && b == null;
                if (a is bool && b is bool)
                    return (bool)a == (bool)b;
                if (a is string && b is string || PyRuntime.IsInt(a) && PyRuntime.IsInt(b) && !(a is bool) && !(b is bool))
                    return PyRuntime.Eq(a, b);
                return ReferenceEquals(a, b);
            }
        }

        private sealed class IfExp : Node
        {
            public Node Test, Body, OrElse;

            public override object Eval(object item)
            {
                return PyRuntime.Truthy(Test.Eval(item)) ? Body.Eval(item) : OrElse.Eval(item);
            }
        }

        private sealed class Display : Node
        {
            public string Kind; // list, tuple, set
            public List<Node> Items;
            public List<bool> Starred;

            public override object Eval(object item)
            {
                var values = new List<object>();
                for (int i = 0; i < Items.Count; i++)
                {
                    object v = Items[i].Eval(item);
                    if (Starred[i])
                        values.AddRange(PyRuntime.Iterate(v));
                    else
                        values.Add(v);
                }
                if (Kind == "tuple")
                    return new PyTuple(values);
                if (Kind == "set")
                {
                    var unique = new List<object>();
                    foreach (object v in values)
                        if (!unique.Any(u => PyRuntime.Eq(u, v)))
                            unique.Add(v);
                    return unique;
                }
                return values;
            }
        }

        private sealed class DictDisplay : Node
        {
            public List<Node> Keys;
            public List<Node> Values;

            public override object Eval(object item)
            {
                var d = new PyDict();
                for (int i = 0; i < Keys.Count; i++)
                {
                    if (Keys[i] == null)
                    {
                        var other = Values[i].Eval(item) as PyDict;
                        if (other == null)
                            throw new PyTypeError("'**' requires a mapping");
                        foreach (var kv in other.Items)
                            d.Set(kv.Key, kv.Value);
                    }
                    else
                    {
                        d.Set(Keys[i].Eval(item), Values[i].Eval(item));
                    }
                }
                return d;
            }
        }

        private sealed class FormattedValue
        {
            public string Literal;
            public Node Expr;
            public char? Conversion;
            public List<FormattedValue> Spec;
            public string DebugText;
        }

        private sealed class JoinedStr : Node
        {
            public List<FormattedValue> Parts;

            public override object Eval(object item)
            {
                return Render(Parts, item);
            }

            public static string Render(List<FormattedValue> parts, object item)
            {
                var sb = new StringBuilder();
                foreach (FormattedValue p in parts)
                {
                    if (p.Expr == null)
                    {
                        sb.Append(p.Literal);
                        continue;
                    }
                    object v = p.Expr.Eval(item);
                    char? conv = p.Conversion;
                    if (p.DebugText != null)
                    {
                        sb.Append(p.DebugText);
                        if (!conv.HasValue && (p.Spec == null || p.Spec.Count == 0))
                            conv = 'r';
                    }
                    if (conv.HasValue)
                        v = new PyFormatter().ConvertField(v, conv);
                    string spec = p.Spec == null ? "" : Render(p.Spec, item);
                    sb.Append(PyFormat.Format(v, spec));
                }
                return sb.ToString();
            }
        }

        #endregion

        #region parser

        private sealed class Parser
        {
            private readonly List<Token> _tokens;
            private readonly List<string> _names;
            private int _pos;

            public Parser(string source, List<string> names)
            {
                _tokens = Tokenize(source);
                _names = names;
            }

            private Token Peek { get { return _tokens[_pos]; } }

            private Token PeekAt(int offset)
            {
                return _tokens[Math.Min(_pos + offset, _tokens.Count - 1)];
            }

            private Token Next()
            {
                return _tokens[_pos++];
            }

            private bool IsOp(string op)
            {
                return Peek.Kind == TokenKind.Op && Peek.Text == op;
            }

            private bool IsKeyword(string kw)
            {
                return Peek.Kind == TokenKind.Name && Peek.Text == kw;
            }

            private bool AcceptOp(string op)
            {
                if (!IsOp(op))
                    return false;
                _pos++;
                return true;
            }

            private bool AcceptKeyword(string kw)
            {
                if (!IsKeyword(kw))
                    return false;
                _pos++;
                return true;
            }

            private void ExpectOp(string op)
            {
                if (!AcceptOp(op))
                    throw Error(string.Format("expected '{0}'", op));
            }

            private PySyntaxError Error(string message = "invalid syntax")
            {
                return new PySyntaxError(string.Format("{0} (at position {1}: {2})", message, Peek.Pos,
                                                       Peek.Kind == TokenKind.End ? "end of expression" : "'" + Peek.Text + "'"));
            }

            private static readonly HashSet<string> Keywords = new HashSet<string>
            {
                "False", "None", "True", "and", "as", "assert", "async", "await", "break", "class", "continue", "def",
                "del", "elif", "else", "except", "finally", "for", "from", "global", "if", "import", "in", "is",
                "lambda", "nonlocal", "not", "or", "pass", "raise", "return", "try", "while", "with", "yield"
            };

            public Node ParseTopLevel()
            {
                Node node = ParseTestList();
                if (Peek.Kind != TokenKind.End)
                    throw Error();
                return node;
            }

            private Node ParseTestList()
            {
                Node first = ParseStarOrExpression();
                if (!IsOp(","))
                    return first;
                var items = new List<Node> { first };
                var starred = new List<bool> { _lastStarred };
                while (AcceptOp(","))
                {
                    if (Peek.Kind == TokenKind.End || IsOp(")") || IsOp("]") || IsOp("}"))
                        break;
                    items.Add(ParseStarOrExpression());
                    starred.Add(_lastStarred);
                }
                return new Display { Kind = "tuple", Items = items, Starred = starred };
            }

            private bool _lastStarred;

            private Node ParseStarOrExpression()
            {
                _lastStarred = false;
                if (AcceptOp("*"))
                {
                    Node n = ParseBitOr();
                    _lastStarred = true;
                    return n;
                }
                return ParseNamedExpression();
            }

            private Node ParseNamedExpression()
            {
                if (Peek.Kind == TokenKind.Name && PeekAt(1).Kind == TokenKind.Op && PeekAt(1).Text == ":=")
                    throw new InvalidArgumentException(string.Format("Invalid filter: Modifying variables ({0}) not allowed.", Peek.Text));
                return ParseExpression();
            }

            private Node ParseExpression()
            {
                if (IsKeyword("lambda"))
                    throw new InvalidArgumentException("Invalid filter: lambda expressions are not allowed.");
                Node body = ParseOr();
                if (AcceptKeyword("if"))
                {
                    Node test = ParseOr();
                    if (!AcceptKeyword("else"))
                        throw Error("expected 'else' after 'if' expression");
                    Node orElse = ParseExpression();
                    return new IfExp { Test = test, Body = body, OrElse = orElse };
                }
                return body;
            }

            private Node ParseOr()
            {
                Node first = ParseAnd();
                if (!IsKeyword("or"))
                    return first;
                var values = new List<Node> { first };
                while (AcceptKeyword("or"))
                    values.Add(ParseAnd());
                return new BoolOp { IsAnd = false, Values = values };
            }

            private Node ParseAnd()
            {
                Node first = ParseNot();
                if (!IsKeyword("and"))
                    return first;
                var values = new List<Node> { first };
                while (AcceptKeyword("and"))
                    values.Add(ParseNot());
                return new BoolOp { IsAnd = true, Values = values };
            }

            private Node ParseNot()
            {
                if (AcceptKeyword("not"))
                    return new UnaryOp { Op = "not", Operand = ParseNot() };
                return ParseComparison();
            }

            private string ComparisonOperator()
            {
                if (Peek.Kind == TokenKind.Op)
                {
                    switch (Peek.Text)
                    {
                        case "<":
                        case ">":
                        case "==":
                        case ">=":
                        case "<=":
                        case "!=":
                            return Next().Text;
                    }
                    return null;
                }
                if (IsKeyword("in"))
                {
                    _pos++;
                    return "in";
                }
                if (IsKeyword("not") && PeekAt(1).Kind == TokenKind.Name && PeekAt(1).Text == "in")
                {
                    _pos += 2;
                    return "not in";
                }
                if (IsKeyword("is"))
                {
                    _pos++;
                    return AcceptKeyword("not") ? "is not" : "is";
                }
                return null;
            }

            private Node ParseComparison()
            {
                Node left = ParseBitOr();
                var ops = new List<string>();
                var comparators = new List<Node>();
                string op;
                while ((op = ComparisonOperator()) != null)
                {
                    ops.Add(op);
                    comparators.Add(ParseBitOr());
                }
                if (ops.Count == 0)
                    return left;
                return new CompareNode { Left = left, Ops = ops, Comparators = comparators };
            }

            private Node ParseBitOr()
            {
                Node left = ParseBitXor();
                while (IsOp("|"))
                {
                    _pos++;
                    left = new BinOp { Op = "|", Left = left, Right = ParseBitXor() };
                }
                return left;
            }

            private Node ParseBitXor()
            {
                Node left = ParseBitAnd();
                while (IsOp("^"))
                {
                    _pos++;
                    left = new BinOp { Op = "^", Left = left, Right = ParseBitAnd() };
                }
                return left;
            }

            private Node ParseBitAnd()
            {
                Node left = ParseShift();
                while (IsOp("&"))
                {
                    _pos++;
                    left = new BinOp { Op = "&", Left = left, Right = ParseShift() };
                }
                return left;
            }

            private Node ParseShift()
            {
                Node left = ParseArith();
                while (IsOp("<<") || IsOp(">>"))
                {
                    string op = Next().Text;
                    left = new BinOp { Op = op, Left = left, Right = ParseArith() };
                }
                return left;
            }

            private Node ParseArith()
            {
                Node left = ParseTerm();
                while (IsOp("+") || IsOp("-"))
                {
                    string op = Next().Text;
                    left = new BinOp { Op = op, Left = left, Right = ParseTerm() };
                }
                return left;
            }

            private Node ParseTerm()
            {
                Node left = ParseFactor();
                while (IsOp("*") || IsOp("/") || IsOp("//") || IsOp("%") || IsOp("@"))
                {
                    string op = Next().Text;
                    left = new BinOp { Op = op, Left = left, Right = ParseFactor() };
                }
                return left;
            }

            private Node ParseFactor()
            {
                if (IsOp("+") || IsOp("-") || IsOp("~"))
                {
                    string op = Next().Text;
                    return new UnaryOp { Op = op, Operand = ParseFactor() };
                }
                return ParsePower();
            }

            private Node ParsePower()
            {
                if (IsKeyword("await"))
                    throw Error("'await' outside function");
                Node left = ParsePrimary();
                if (AcceptOp("**"))
                    return new BinOp { Op = "**", Left = left, Right = ParseFactor() };
                return left;
            }

            private Node ParsePrimary()
            {
                Node node = ParseAtom();
                while (true)
                {
                    if (AcceptOp("."))
                    {
                        if (Peek.Kind != TokenKind.Name)
                            throw Error();
                        node = new Attribute { Value = node, Name = Next().Text };
                    }
                    else if (AcceptOp("("))
                    {
                        node = new Call { Func = node, Args = ParseArguments() };
                    }
                    else if (AcceptOp("["))
                    {
                        node = new Subscript { Value = node, Key = ParseSubscripts() };
                        ExpectOp("]");
                    }
                    else
                    {
                        return node;
                    }
                }
            }

            private List<Argument> ParseArguments()
            {
                var args = new List<Argument>();
                while (!IsOp(")"))
                {
                    if (AcceptOp("**"))
                        args.Add(new Argument { DoubleStar = true, Value = ParseExpression() });
                    else if (AcceptOp("*"))
                        args.Add(new Argument { Star = true, Value = ParseExpression() });
                    else if (Peek.Kind == TokenKind.Name && PeekAt(1).Kind == TokenKind.Op && PeekAt(1).Text == "=")
                    {
                        string kw = Next().Text;
                        _pos++;
                        args.Add(new Argument { Keyword = kw, Value = ParseExpression() });
                    }
                    else
                    {
                        Node value = ParseNamedExpression();
                        if (IsKeyword("for") || IsKeyword("async"))
                            throw ComprehensionError();
                        args.Add(new Argument { Value = value });
                    }
                    if (!AcceptOp(","))
                        break;
                }
                ExpectOp(")");
                return args;
            }

            private Node ParseSubscripts()
            {
                Node first = ParseSubscript();
                if (!IsOp(","))
                    return first;
                var items = new List<Node> { first };
                while (AcceptOp(","))
                {
                    if (IsOp("]"))
                        break;
                    items.Add(ParseSubscript());
                }
                return new Display { Kind = "tuple", Items = items, Starred = items.Select(x => false).ToList() };
            }

            private Node ParseSubscript()
            {
                Node start = null, stop = null, step = null;
                if (!IsOp(":"))
                {
                    start = ParseNamedExpression();
                    if (!IsOp(":"))
                        return start;
                }
                ExpectOp(":");
                if (!IsOp(":") && !IsOp("]") && !IsOp(","))
                    stop = ParseExpression();
                if (AcceptOp(":"))
                {
                    if (!IsOp("]") && !IsOp(","))
                        step = ParseExpression();
                }
                return new SliceNode { Start = start, Stop = stop, Step = step };
            }

            private Exception ComprehensionError()
            {
                // The loop variable of a comprehension is a stored name, which the filter does not allow.
                _pos++;
                string target = Peek.Kind == TokenKind.Name ? Peek.Text : "?";
                return new InvalidArgumentException(string.Format("Invalid filter: Modifying variables ({0}) not allowed.", target));
            }

            private Node ParseAtom()
            {
                Token t = Peek;
                switch (t.Kind)
                {
                    case TokenKind.Name:
                        _pos++;
                        switch (t.Text)
                        {
                            case "True": return new Constant { Value = true };
                            case "False": return new Constant { Value = false };
                            case "None": return new Constant { Value = null };
                        }
                        if (Keywords.Contains(t.Text))
                        {
                            _pos--;
                            throw Error();
                        }
                        if (t.Text == "datetime")
                            return new DateTimeName();
                        _names.Add(t.Text);
                        return new ItemAttribute { Name = t.Text };
                    case TokenKind.Number:
                        _pos++;
                        return new Constant { Value = t.Value };
                    case TokenKind.String:
                        return ParseStrings();
                    case TokenKind.Op:
                        if (t.Text == "(")
                        {
                            _pos++;
                            if (AcceptOp(")"))
                                return new Display { Kind = "tuple", Items = new List<Node>(), Starred = new List<bool>() };
                            if (IsKeyword("yield"))
                                throw Error("'yield' outside function");
                            Node first = ParseStarOrExpression();
                            bool firstStarred = _lastStarred;
                            if (IsKeyword("for") || IsKeyword("async"))
                                throw ComprehensionError();
                            if (AcceptOp(")"))
                            {
                                if (firstStarred)
                                    throw Error("cannot use starred expression here");
                                return first;
                            }
                            var items = new List<Node> { first };
                            var starred = new List<bool> { firstStarred };
                            while (AcceptOp(","))
                            {
                                if (IsOp(")"))
                                    break;
                                items.Add(ParseStarOrExpression());
                                starred.Add(_lastStarred);
                            }
                            ExpectOp(")");
                            return new Display { Kind = "tuple", Items = items, Starred = starred };
                        }
                        if (t.Text == "[")
                        {
                            _pos++;
                            var items = new List<Node>();
                            var starred = new List<bool>();
                            while (!IsOp("]"))
                            {
                                items.Add(ParseStarOrExpression());
                                starred.Add(_lastStarred);
                                if (items.Count == 1 && (IsKeyword("for") || IsKeyword("async")))
                                    throw ComprehensionError();
                                if (!AcceptOp(","))
                                    break;
                            }
                            ExpectOp("]");
                            return new Display { Kind = "list", Items = items, Starred = starred };
                        }
                        if (t.Text == "{")
                        {
                            _pos++;
                            if (AcceptOp("}"))
                                return new DictDisplay { Keys = new List<Node>(), Values = new List<Node>() };
                            if (AcceptOp("**"))
                                return ParseDictRest(null, ParseBitOr());
                            Node first = ParseStarOrExpression();
                            bool firstStarred = _lastStarred;
                            if (AcceptOp(":"))
                            {
                                Node value = ParseExpression();
                                if (IsKeyword("for") || IsKeyword("async"))
                                    throw ComprehensionError();
                                return ParseDictRest(first, value);
                            }
                            if (IsKeyword("for") || IsKeyword("async"))
                                throw ComprehensionError();
                            var items = new List<Node> { first };
                            var starred = new List<bool> { firstStarred };
                            while (AcceptOp(","))
                            {
                                if (IsOp("}"))
                                    break;
                                items.Add(ParseStarOrExpression());
                                starred.Add(_lastStarred);
                            }
                            ExpectOp("}");
                            return new Display { Kind = "set", Items = items, Starred = starred };
                        }
                        if (t.Text == "...")
                        {
                            _pos++;
                            return new Constant { Value = null };
                        }
                        break;
                }
                throw Error();
            }

            private Node ParseDictRest(Node firstKey, Node firstValue)
            {
                var keys = new List<Node> { firstKey };
                var values = new List<Node> { firstValue };
                while (AcceptOp(","))
                {
                    if (IsOp("}"))
                        break;
                    if (AcceptOp("**"))
                    {
                        keys.Add(null);
                        values.Add(ParseBitOr());
                        continue;
                    }
                    keys.Add(ParseExpression());
                    ExpectOp(":");
                    values.Add(ParseExpression());
                }
                ExpectOp("}");
                return new DictDisplay { Keys = keys, Values = values };
            }

            private Node ParseStrings()
            {
                var parts = new List<FormattedValue>();
                bool anyF = false;
                while (Peek.Kind == TokenKind.String)
                {
                    StringPart sp = Next().Str;
                    if (sp.IsFString)
                    {
                        anyF = true;
                        parts.AddRange(ParseFString(sp.Text, sp.IsRaw));
                    }
                    else
                    {
                        parts.Add(new FormattedValue { Literal = sp.IsRaw ? sp.Text : PyStr.DecodeEscapes(sp.Text) });
                    }
                }
                if (!anyF)
                    return new Constant { Value = string.Concat(parts.Select(p => p.Literal)) };
                return new JoinedStr { Parts = parts };
            }

            private List<FormattedValue> ParseFString(string body, bool raw)
            {
                var parts = new List<FormattedValue>();
                var literal = new StringBuilder();
                int i = 0;
                while (i < body.Length)
                {
                    char c = body[i];
                    if (c == '{')
                    {
                        if (i + 1 < body.Length && body[i + 1] == '{')
                        {
                            literal.Append('{');
                            i += 2;
                            continue;
                        }
                        if (literal.Length > 0)
                        {
                            parts.Add(new FormattedValue { Literal = raw ? literal.ToString() : PyStr.DecodeEscapes(literal.ToString()) });
                            literal.Clear();
                        }
                        parts.Add(ParseFStringField(body, ref i));
                        continue;
                    }
                    if (c == '}')
                    {
                        if (i + 1 < body.Length && body[i + 1] == '}')
                        {
                            literal.Append('}');
                            i += 2;
                            continue;
                        }
                        throw new PySyntaxError("f-string: single '}' is not allowed");
                    }
                    literal.Append(c);
                    i++;
                }
                if (literal.Length > 0)
                    parts.Add(new FormattedValue { Literal = raw ? literal.ToString() : PyStr.DecodeEscapes(literal.ToString()) });
                return parts;
            }

            private FormattedValue ParseFStringField(string body, ref int i)
            {
                // body[i] == '{'
                i++;
                int start = i;
                int depth = 0;
                char quote = '\0';
                while (i < body.Length)
                {
                    char c = body[i];
                    if (quote != '\0')
                    {
                        if (c == quote)
                            quote = '\0';
                        i++;
                        continue;
                    }
                    if (c == '\'' || c == '"')
                    {
                        quote = c;
                        i++;
                        continue;
                    }
                    if (c == '(' || c == '[' || c == '{')
                        depth++;
                    else if (c == ')' || c == ']' || (c == '}' && depth > 0))
                        depth--;
                    else if (depth == 0 && (c == '}' || c == ':' || (c == '!' && i + 1 < body.Length && body[i + 1] != '=')))
                        break;
                    i++;
                }
                if (i >= body.Length)
                    throw new PySyntaxError("f-string: expecting '}'");
                string exprText = body.Substring(start, i - start);
                var field = new FormattedValue();
                string trimmed = exprText.TrimEnd();
                if (trimmed.EndsWith("=") && !trimmed.EndsWith("==") && !trimmed.EndsWith("!=") && !trimmed.EndsWith("<=") && !trimmed.EndsWith(">="))
                {
                    field.DebugText = exprText;
                    exprText = trimmed.Substring(0, trimmed.Length - 1);
                }
                if (exprText.Trim().Length == 0)
                    throw new PySyntaxError("f-string: valid expression required before '}'");
                var sub = new Parser(exprText, _names);
                field.Expr = sub.ParseTopLevel();
                if (body[i] == '!')
                {
                    i++;
                    if (i >= body.Length)
                        throw new PySyntaxError("f-string: missing conversion character");
                    field.Conversion = body[i++];
                    if ("sra".IndexOf(field.Conversion.Value) < 0)
                        throw new PySyntaxError("f-string: invalid conversion character");
                }
                if (i < body.Length && body[i] == ':')
                {
                    i++;
                    int specStart = i;
                    int d = 0;
                    while (i < body.Length)
                    {
                        if (body[i] == '{')
                            d++;
                        else if (body[i] == '}')
                        {
                            if (d == 0)
                                break;
                            d--;
                        }
                        i++;
                    }
                    field.Spec = ParseFString(body.Substring(specStart, i - specStart), true);
                }
                if (i >= body.Length || body[i] != '}')
                    throw new PySyntaxError("f-string: expecting '}'");
                i++;
                return field;
            }
        }

        #endregion
    }
}

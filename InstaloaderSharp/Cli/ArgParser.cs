using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using InstaloaderSharp.Python;

namespace InstaloaderSharp.Cli
{
    /// <summary>Thrown to terminate after an argument parsing error, help or version output.</summary>
    public sealed class ArgParserExit : Exception
    {
        public ArgParserExit(int code) : base("exit " + code)
        {
            Code = code;
        }

        public int Code { get; private set; }
    }

    /// <summary>Raised by type converters for invalid values (argparse.ArgumentTypeError).</summary>
    public sealed class ArgumentTypeError : Exception
    {
        public ArgumentTypeError(string message) : base(message) { }
    }

    public enum ArgKind
    {
        StoreTrue,
        Store,
        Append,
        OptionalValue,
        Help,
        Version,
        Positional
    }

    /// <summary>An argument definition, like an argparse Action.</summary>
    public sealed class ArgAction
    {
        public string[] OptionStrings = new string[0];
        public string Dest;
        public ArgKind Kind;
        public string Metavar;
        public string Help;
        public bool Suppress;
        public object Const;
        public object Default;
        public Func<string, object> Type;
        public string TypeName;

        public bool IsPositional { get { return OptionStrings.Length == 0; } }

        public string DisplayName
        {
            get { return IsPositional ? (Metavar ?? Dest) : string.Join("/", OptionStrings); }
        }
    }

    public sealed class ArgGroup
    {
        public string Title;
        public string Description;
        public readonly List<ArgAction> Actions = new List<ArgAction>();
    }

    /// <summary>
    /// A command line parser with the behavior of Python's argparse.ArgumentParser (as configured by Instaloader):
    /// unique prefixes of long options, '--option=value', '-ovalue', combined short flags, '--' to end options,
    /// '+file' to read arguments from a file, and argparse's help layout and error messages.
    /// </summary>
    public sealed class ArgParser
    {
        public string Prog;
        public string Usage;
        public string Description;
        public string Epilog;
        public string VersionString;
        public char FromFilePrefix = '+';
        public readonly List<ArgGroup> Groups = new List<ArgGroup>();
        private readonly List<ArgAction> _actions = new List<ArgAction>();
        private readonly List<KeyValuePair<string, ArgAction>> _optionStrings = new List<KeyValuePair<string, ArgAction>>();

        public ArgGroup AddGroup(string title, string description = null)
        {
            var g = new ArgGroup { Title = title, Description = description };
            Groups.Add(g);
            return g;
        }

        public ArgAction Add(ArgGroup group, ArgAction action)
        {
            if (action.Dest == null)
            {
                string longOpt = action.OptionStrings.FirstOrDefault(o => o.StartsWith("--")) ?? action.OptionStrings[0];
                action.Dest = longOpt.TrimStart('-').Replace('-', '_');
            }
            group.Actions.Add(action);
            _actions.Add(action);
            foreach (string o in action.OptionStrings)
                _optionStrings.Add(new KeyValuePair<string, ArgAction>(o, action));
            return action;
        }

        #region help

        private static int TerminalWidth()
        {
            string columns = Environment.GetEnvironmentVariable("COLUMNS");
            int c;
            if (columns != null && int.TryParse(columns, out c) && c > 0)
                return c - 2;
            try
            {
                if (!Console.IsOutputRedirected)
                    return Console.WindowWidth - 2;
            }
            catch (IOException)
            {
            }
            return 80 - 2;
        }

        private static string FormatArgs(ArgAction a)
        {
            string metavar = a.Metavar ?? (a.IsPositional ? a.Dest : a.Dest.ToUpperInvariant());
            switch (a.Kind)
            {
                case ArgKind.Store:
                case ArgKind.Append:
                    return metavar;
                case ArgKind.OptionalValue:
                    return "[" + metavar + "]";
                case ArgKind.Positional:
                    return metavar;
            }
            return "";
        }

        private static string Invocation(ArgAction a)
        {
            if (a.IsPositional)
                return a.Metavar ?? a.Dest;
            string opts = string.Join(", ", a.OptionStrings);
            string args = FormatArgs(a);
            return args.Length == 0 ? opts : opts + " " + args;
        }

        public string FormatUsage()
        {
            return "usage: " + Usage.Replace("%%", "%") + "\n";
        }

        public string FormatHelp()
        {
            int width = TerminalWidth();
            int maxHelpPosition = Math.Min(24, Math.Max(width - 20, 4));
            int actionMaxLength = 0;
            foreach (ArgGroup g in Groups)
                foreach (ArgAction a in g.Actions)
                    if (!a.Suppress)
                        actionMaxLength = Math.Max(actionMaxLength, Invocation(a).Length + 2);
            int helpPosition = Math.Min(actionMaxLength + 2, maxHelpPosition);
            int helpWidth = Math.Max(width - helpPosition, 11);
            int actionWidth = helpPosition - 2 - 2;

            var sb = new StringBuilder();
            sb.Append(FormatUsage()).Append('\n');
            if (!string.IsNullOrEmpty(Description))
            {
                foreach (string line in TextWrap.Wrap(Normalize(Description), Math.Max(width, 11)))
                    sb.Append(line).Append('\n');
                sb.Append('\n');
            }
            foreach (ArgGroup g in Groups)
            {
                List<ArgAction> visible = g.Actions.Where(a => !a.Suppress).ToList();
                if (visible.Count == 0 && string.IsNullOrEmpty(g.Description))
                    continue;
                sb.Append(g.Title).Append(":\n");
                if (!string.IsNullOrEmpty(g.Description))
                {
                    foreach (string line in TextWrap.Wrap(Normalize(g.Description), Math.Max(width - 2, 11)))
                        sb.Append("  ").Append(line).Append('\n');
                    sb.Append('\n');
                }
                foreach (ArgAction a in visible)
                {
                    string invocation = Invocation(a);
                    if (string.IsNullOrEmpty(a.Help))
                    {
                        sb.Append("  ").Append(invocation).Append('\n');
                        continue;
                    }
                    List<string> helpLines = TextWrap.Wrap(Normalize(a.Help.Replace("%%", "%")), helpWidth);
                    if (invocation.Length <= actionWidth)
                    {
                        sb.Append("  ").Append(invocation.PadRight(actionWidth)).Append("  ");
                    }
                    else
                    {
                        sb.Append("  ").Append(invocation).Append('\n');
                        sb.Append(' ', helpPosition);
                    }
                    for (int i = 0; i < helpLines.Count; i++)
                    {
                        if (i > 0)
                            sb.Append(' ', helpPosition);
                        sb.Append(helpLines[i]).Append('\n');
                    }
                }
                sb.Append('\n');
            }
            if (!string.IsNullOrEmpty(Epilog))
            {
                foreach (string line in TextWrap.Wrap(Normalize(Epilog), Math.Max(width, 11)))
                    sb.Append(line).Append('\n');
            }
            return sb.ToString();
        }

        private static string Normalize(string text)
        {
            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        #endregion

        #region parsing

        public void Error(string message)
        {
            Console.Error.Write(FormatUsage());
            Console.Error.WriteLine("{0}: error: {1}", Prog, message);
            throw new ArgParserExit(2);
        }

        private List<string> ReadArgsFromFiles(IEnumerable<string> args)
        {
            var result = new List<string>();
            foreach (string arg in args)
            {
                if (arg.Length == 0 || arg[0] != FromFilePrefix)
                {
                    result.Add(arg);
                    continue;
                }
                string path = arg.Substring(1);
                string text;
                try
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    try
                    {
                        text = new UTF8Encoding(false, true).GetString(bytes);
                    }
                    catch (DecoderFallbackException)
                    {
                        text = PyOs.LocaleEncoding.GetString(bytes);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException ||
                                           ex is NotSupportedException)
                {
                    Error(string.Format("[Errno 2] No such file or directory: {0}", PyStr.Repr(path)));
                    throw;
                }
                if (text.Length > 0 && text[0] == (char)0xFEFF)
                    text = text.Substring(1);
                result.AddRange(ReadArgsFromFiles(PyStr.SplitLines(text)));
            }
            return result;
        }

        private static readonly Regex NegativeNumber = new Regex(@"^-\d+$|^-\d*\.\d+$");

        private sealed class OptionMatch
        {
            public ArgAction Action;
            public string OptionString;
            public string ExplicitArg;
        }

        /// <summary>argparse's _parse_optional: null means the argument is positional.</summary>
        private OptionMatch ParseOptional(string arg)
        {
            if (arg.Length == 0 || arg[0] != '-')
                return null;
            foreach (var kv in _optionStrings)
                if (kv.Key == arg)
                    return new OptionMatch { Action = kv.Value, OptionString = arg };
            if (arg.Length == 1)
                return null;
            int eq = arg.IndexOf('=');
            if (eq >= 0)
            {
                string optionString = arg.Substring(0, eq);
                foreach (var kv in _optionStrings)
                    if (kv.Key == optionString)
                        return new OptionMatch { Action = kv.Value, OptionString = optionString, ExplicitArg = arg.Substring(eq + 1) };
            }
            List<OptionMatch> tuples = GetOptionTuples(arg);
            if (tuples.Count > 1)
                Error(string.Format("ambiguous option: {0} could match {1}", eq >= 0 ? arg.Substring(0, eq) : arg,
                                    string.Join(", ", tuples.Select(t => t.OptionString))));
            if (tuples.Count == 1)
                return tuples[0];
            if (NegativeNumber.IsMatch(arg))
                return null;
            if (arg.Contains(" "))
                return null;
            return new OptionMatch { Action = null, OptionString = arg };
        }

        private List<OptionMatch> GetOptionTuples(string optionString)
        {
            var result = new List<OptionMatch>();
            if (optionString.Length >= 2 && optionString[0] == '-' && optionString[1] == '-')
            {
                int eq = optionString.IndexOf('=');
                string prefix = eq >= 0 ? optionString.Substring(0, eq) : optionString;
                string explicitArg = eq >= 0 ? optionString.Substring(eq + 1) : null;
                foreach (var kv in _optionStrings)
                    if (kv.Key.StartsWith(prefix, StringComparison.Ordinal))
                        result.Add(new OptionMatch { Action = kv.Value, OptionString = kv.Key, ExplicitArg = explicitArg });
            }
            else if (optionString.Length >= 2 && optionString[0] == '-')
            {
                int eq = optionString.IndexOf('=');
                string prefix = eq >= 0 ? optionString.Substring(0, eq) : optionString;
                string explicitArg = eq >= 0 ? optionString.Substring(eq + 1) : null;
                string shortPrefix = optionString.Substring(0, 2);
                string shortExplicit = optionString.Substring(2);
                foreach (var kv in _optionStrings)
                {
                    if (kv.Key == shortPrefix)
                        result.Add(new OptionMatch { Action = kv.Value, OptionString = kv.Key, ExplicitArg = shortExplicit });
                    else if (kv.Key.StartsWith(prefix, StringComparison.Ordinal))
                        result.Add(new OptionMatch { Action = kv.Value, OptionString = kv.Key, ExplicitArg = explicitArg });
                }
            }
            return result;
        }

        private object Convert(ArgAction action, string value)
        {
            if (action.Type == null)
                return value;
            try
            {
                return action.Type(value);
            }
            catch (ArgumentTypeError err)
            {
                Error(string.Format("argument {0}: {1}", action.DisplayName, err.Message));
            }
            catch (Exception ex) when (ex is FormatException || ex is OverflowException || ex is PyValueError)
            {
                Error(string.Format("argument {0}: invalid {1} value: {2}", action.DisplayName, action.TypeName, PyStr.Repr(value)));
            }
            return null;
        }

        /// <summary>Parses the arguments; returns the values by destination name.</summary>
        public Dictionary<string, object> Parse(IEnumerable<string> argv)
        {
            List<string> args = ReadArgsFromFiles(argv);
            var values = new Dictionary<string, object>();
            foreach (ArgAction a in _actions)
            {
                if (a.Kind == ArgKind.StoreTrue)
                    values[a.Dest] = false;
                else if (a.Kind == ArgKind.Positional)
                    values[a.Dest] = new List<string>();
                else if (a.Kind != ArgKind.Help && a.Kind != ArgKind.Version)
                    values[a.Dest] = a.Default;
            }
            ArgAction positional = _actions.FirstOrDefault(a => a.Kind == ArgKind.Positional);
            var positionals = new List<string>();
            var extras = new List<string>();
            bool afterDoubleDash = false;
            Func<string, bool> isOptionLike = s => s != "--" && ParseOptional(s) != null;
            for (int i = 0; i < args.Count; i++)
            {
                string arg = args[i];
                if (afterDoubleDash)
                {
                    positionals.Add(arg);
                    continue;
                }
                if (arg == "--")
                {
                    afterDoubleDash = true;
                    continue;
                }
                OptionMatch match = ParseOptional(arg);
                if (match == null)
                {
                    positionals.Add(arg);
                    continue;
                }
                if (match.Action == null)
                {
                    extras.Add(arg);
                    continue;
                }
                while (true)
                {
                    ArgAction action = match.Action;
                    string explicitArg = match.ExplicitArg;
                    switch (action.Kind)
                    {
                        case ArgKind.StoreTrue:
                        case ArgKind.Help:
                        case ArgKind.Version:
                            if (!string.IsNullOrEmpty(explicitArg))
                            {
                                bool singleDash = match.OptionString.Length == 2 && match.OptionString[0] == '-' && match.OptionString[1] != '-';
                                if (singleDash)
                                {
                                    Apply(action, null, values);
                                    string next = "-" + explicitArg[0];
                                    string rest = explicitArg.Length > 1 ? explicitArg.Substring(1) : null;
                                    ArgAction nextAction = _optionStrings.Where(kv => kv.Key == next).Select(kv => kv.Value).FirstOrDefault();
                                    if (nextAction == null)
                                        Error(string.Format("argument {0}: ignored explicit argument {1}", action.DisplayName, PyStr.Repr(explicitArg)));
                                    match = new OptionMatch { Action = nextAction, OptionString = next, ExplicitArg = rest };
                                    continue;
                                }
                                Error(string.Format("argument {0}: ignored explicit argument {1}", action.DisplayName, PyStr.Repr(explicitArg)));
                            }
                            else if (explicitArg != null && match.OptionString.StartsWith("--"))
                            {
                                Error(string.Format("argument {0}: ignored explicit argument {1}", action.DisplayName, PyStr.Repr(explicitArg)));
                            }
                            Apply(action, null, values);
                            break;
                        case ArgKind.Store:
                        case ArgKind.Append:
                            {
                                string value;
                                if (explicitArg != null && (explicitArg.Length > 0 || match.OptionString.StartsWith("--") || arg.Contains("=")))
                                {
                                    value = explicitArg;
                                }
                                else if (i + 1 < args.Count && !isOptionLike(args[i + 1]) && args[i + 1] != "--")
                                {
                                    value = args[++i];
                                }
                                else
                                {
                                    Error(string.Format("argument {0}: expected one argument", action.DisplayName));
                                    value = null;
                                }
                                Apply(action, value, values);
                                break;
                            }
                        case ArgKind.OptionalValue:
                            {
                                string value = null;
                                if (explicitArg != null && (explicitArg.Length > 0 || arg.Contains("=")))
                                    value = explicitArg;
                                else if (i + 1 < args.Count && !isOptionLike(args[i + 1]) && args[i + 1] != "--")
                                    value = args[++i];
                                if (value == null)
                                    values[action.Dest] = action.Const;
                                else
                                    Apply(action, value, values);
                                break;
                            }
                    }
                    break;
                }
            }
            if (extras.Count > 0)
                Error("unrecognized arguments: " + string.Join(" ", extras));
            if (positional != null)
                values[positional.Dest] = positionals;
            else if (positionals.Count > 0)
                Error("unrecognized arguments: " + string.Join(" ", positionals));
            return values;
        }

        private void Apply(ArgAction action, string value, Dictionary<string, object> values)
        {
            switch (action.Kind)
            {
                case ArgKind.StoreTrue:
                    values[action.Dest] = true;
                    break;
                case ArgKind.Help:
                    Console.Out.Write(FormatHelp());
                    throw new ArgParserExit(0);
                case ArgKind.Version:
                    Console.Out.WriteLine(VersionString);
                    throw new ArgParserExit(0);
                case ArgKind.Store:
                case ArgKind.OptionalValue:
                    values[action.Dest] = Convert(action, value);
                    break;
                case ArgKind.Append:
                    {
                        var list = values[action.Dest] as List<object>;
                        list = list == null ? new List<object>() : new List<object>(list);
                        list.Add(Convert(action, value));
                        values[action.Dest] = list;
                        break;
                    }
            }
        }

        #endregion
    }
}

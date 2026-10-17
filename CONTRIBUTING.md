# Contributing to InstaloaderSharp

Thank you for your interest in InstaloaderSharp! Everything about the project, including bug reports, feature
requests, questions and code contributions, is handled through the GitHub repository:

**https://github.com/InstaloaderSharp/InstaloaderSharp**

* [Reporting a bug](#reporting-a-bug)
* [Reporting a security issue](#reporting-a-security-issue)
* [Suggesting a feature](#suggesting-a-feature)
* [Asking a question](#asking-a-question)
* [Contributing code](#contributing-code)
* [Project guidelines](#project-guidelines)
* [Testing your changes](#testing-your-changes)
* [Submitting a pull request](#submitting-a-pull-request)
* [License](#license)

## Reporting a bug

Report bugs in the [issue tracker](https://github.com/InstaloaderSharp/InstaloaderSharp/issues).

1. **Search first.** Check the [open and closed issues](https://github.com/InstaloaderSharp/InstaloaderSharp/issues?q=is%3Aissue)
   for the same problem. If you find one, add any new details as a comment instead of opening a duplicate.
2. **Make sure you use the latest version.** Many problems are caused by changes on Instagram's side and may
   already be fixed in a newer release.
3. **Open a new issue** with a short, descriptive title, e.g. "`--stories` fails with KeyError 'reels'"
   rather than "It doesn't work".

Please include:

* The InstaloaderSharp version (`InstaloaderSharp.exe --version`) and your Windows version.
* The full command line you ran.
* The complete output, including all error messages and the "Errors or warnings occurred" summary at the end,
  and the exit code.
* What you expected to happen and what happened instead.
* Whether you were logged in, and how (`--login` or `--load-cookies` with which browser).
* Whether the problem is reproducible, and if it happens with every profile/post or only with specific ones.
* If you can, whether [Instaloader](https://github.com/instaloader/instaloader) shows the same problem.
  That helps tell bugs in InstaloaderSharp apart from changes on Instagram's side. Both are worth reporting:
  InstaloaderSharp aims to work correctly even where Instaloader does not.

> **Never post passwords, session files, cookie values or two-factor codes.** Session files
> (`%LOCALAPPDATA%\Instaloader\session-*`) and browser cookies give full access to your Instagram account.
> Before posting output, remove or replace anything private, such as the usernames of private accounts or
> the contents of `.json`/`.json.xz` metadata files of private posts. A public profile or post that shows the
> problem is the most helpful example.

### Rate limits and blocked accounts

Messages like *"Please wait a few minutes before you try again"*, HTTP 429 or 401, *"Checkpoint required"*
or redirects to `/terms/unblock/` come from Instagram restricting the account or IP address, not from a bug.
Before reporting them, see "Tips and troubleshooting" in the [README](README.md#tips-and-troubleshooting).
Waiting, using the account normally in a browser for a while, or re-importing fresh cookies with
`--load-cookies` usually helps.

## Reporting a security issue

If you find a security problem, for example in how session files, cookies or credentials are handled, **do not
open a public issue.** Report it privately through the repository's
[Security tab](https://github.com/InstaloaderSharp/InstaloaderSharp/security) ("Report a vulnerability"),
so that it can be fixed before it is disclosed.

## Suggesting a feature

Feature requests are welcome as [issues](https://github.com/InstaloaderSharp/InstaloaderSharp/issues/new).
Describe what you would like to do and why (the use case), not only the proposed solution, and mention if
Instaloader already has the feature. Please keep in mind that InstaloaderSharp only downloads data;
features that act on Instagram (liking, following, posting, ...) are out of scope.

## Asking a question

For questions about using InstaloaderSharp, first check the [README](README.md), which covers all options. If
it does not answer your question, open an [issue](https://github.com/InstaloaderSharp/InstaloaderSharp/issues)
and describe what you are trying to do.

## Contributing code

Bug fixes, adaptations to Instagram changes, documentation improvements and new features are all welcome.
For larger changes, please open an issue first to discuss the approach, so that no work is wasted.

### Setting up

1. Fork the repository on GitHub and clone your fork:

   ```
   git clone https://github.com/YOUR-USERNAME/InstaloaderSharp.git
   ```

2. Create a branch for your change from `main`, with a descriptive name:

   ```
   git checkout -b fix-stories-keyerror
   ```

3. Build the solution, either in Visual Studio (2015 or later) or with:

   ```
   "C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe" InstaloaderSharp.sln /p:Configuration=Release
   ```

   You need the .NET Framework 4.8.1 developer pack (reference assemblies). The build must finish without
   errors and warnings.

### Repository layout

| Path | Contents |
| --- | --- |
| `InstaloaderSharp/Instaloader.cs`, `InstaloaderContext.cs`, `NodeIterator.cs`, `LatestStamps.cs` | The core: downloading, Instagram requests, resumable iteration, `--latest-stamps` |
| `InstaloaderSharp/Structures/` | `Post`, `Profile`, `StoryItem`, `Story`, `Highlight`, `Hashtag` and saving/loading them as JSON |
| `InstaloaderSharp/Program.cs`, `Cli/` | Command line (argparse-compatible parser), `--load-cookies` (browser cookie databases) |
| `InstaloaderSharp/Http/` | HTTP/2 client on top of WinHTTP, cookie jar, `requests`-like session |
| `InstaloaderSharp/Json/`, `Compression/`, `Python/` | Python-compatible JSON, xz/LZMA, pickle, `str.format`, `strftime` and the expression evaluator for `--post-filter` |

## Project guidelines

* **C# 6 and .NET Framework 4.8.1.** The code must compile with the C# 6 compiler of Visual Studio 2015 /
  MSBuild 14. Do not use newer language features such as tuples, `out var`, local functions, pattern
  matching (`is T x`), `switch` expressions, `??=` or `default` literals.
* **No third-party dependencies.** InstaloaderSharp is a single executable without NuGet packages. Use the .NET
  Framework and Windows APIs only.
* **Add new source files to `InstaloaderSharp/InstaloaderSharp.csproj`.** The project lists its files explicitly.
  Visual Studio does this automatically; if you use another editor, add a `<Compile Include="..." />` entry.
* **Correct behavior first.** InstaloaderSharp started as a port of Instaloader 4.15.4, but it should work
  correctly rather than reproduce Instaloader's bugs or failures caused by Instagram changes. Fix them, and
  document every deliberate difference from Instaloader in the "Differences from Instaloader" section of
  the [README](README.md).
* **Keep files compatible with Python Instaloader.** Users may use both programs on the same downloads.
  Session files (pickle), metadata and resume files (`.json`/`.json.xz`), `latest-stamps.ini`, file names and
  modification times should stay readable and consistent with Instaloader.
* **Keep the command line compatible.** Do not rename or remove existing options or change their meaning.
  New options should follow the existing naming style. Update the help text and the README when you change
  options or behavior.
* **Follow the existing code style.** Match the surrounding code: four-space indentation, braces on their own
  lines, PascalCase names that mirror the Python names (`download_post` → `DownloadPost`), and XML
  documentation comments on public members. Explain *why* in comments where the code works around Instagram
  behavior.
* **Do not commit downloads or personal data.** Never commit session files, cookies, downloaded media,
  metadata of private accounts or credentials, also not in tests or examples.

## Submitting a pull request

1. Keep the pull request focused on one change; split unrelated changes into separate pull requests.
2. Write clear commit messages: a short summary line in the imperative ("Fix KeyError in story download"),
   followed by a blank line and an explanation of what and why, if needed.
3. Make sure your branch is up to date with `main` and builds without errors and warnings.
4. Push the branch to your fork and open a pull request against `main` of
   [InstaloaderSharp/InstaloaderSharp](https://github.com/InstaloaderSharp/InstaloaderSharp/pulls).
5. In the description, explain what the change does, link the related issue (e.g. "Fixes #12"), and describe
   how you tested it.
6. Be ready to answer questions and to update your pull request after review.

## License

InstaloaderSharp is licensed under the [MIT License](LICENSE). By contributing, you agree that your
contributions are licensed under the same license.

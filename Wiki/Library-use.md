`InstaloaderSharp.exe` is also a .NET library. Your own C# (or other .NET) program can reference it and use the same classes the command line uses, to download items, read their metadata or build your own tools. The API follows Instaloader's Python API, with .NET naming.

## Contents

- [Setting up a project](#setting-up-a-project)
- [Naming](#naming)
- [The main classes](#the-main-classes)
- [Downloading a profile](#downloading-a-profile)
- [Options](#options)
- [Logging in](#logging-in)
- [Reading metadata](#reading-metadata)
- [Single posts, hashtags, stories and highlights](#single-posts-hashtags-stories-and-highlights)
- [Saving and loading metadata files](#saving-and-loading-metadata-files)
- [Resumable downloads](#resumable-downloads)
- [Filters](#filters)
- [Errors and Ctrl+C](#errors-and-ctrlc)
- [Things to keep in mind](#things-to-keep-in-mind)

## Setting up a project

1. Create a project for .NET Framework 4.8.1 (for example a console application in Visual Studio).
2. Add a reference to `InstaloaderSharp.exe` (**Project → Add Reference → Browse**), and to `System.Numerics`.
3. Copy `InstaloaderSharp.exe` next to your program, or let Visual Studio do it (**Copy Local**).

All classes are in the namespace `InstaloaderSharp`; `Interrupt` is in `InstaloaderSharp.Util`. The library has the same requirements as the program: Windows 10 1607 or later and .NET Framework 4.8.1.

## Naming

Python names map to .NET names in PascalCase:

| Python (Instaloader) | C# (InstaloaderSharp) |
|---|---|
| `instaloader.Instaloader()` | `new Instaloader()` |
| `L.download_post(post, target)` | `L.DownloadPost(post, target)` |
| `Profile.from_username(L.context, name)` | `Profile.FromUsername(L.Context, name)` |
| `post.date_utc`, `post.caption_hashtags` | `post.DateUtc`, `post.CaptionHashtags` |
| keyword arguments | named arguments: `new Instaloader(downloadVideos: false)` |
| `KeyboardInterrupt` | `KeyboardInterruptException` |
| `with L.context.error_catcher(...):` | `L.Context.ErrorCatcher(..., () => { ... })` |
| `with resumable_iteration(...):` | `NodeIteration.ResumableIteration(..., body: ...)` |

Numbers that Instagram may leave out are nullable (`long? Likes`, `long? Followers`). Raw JSON is represented by the `JObject` and `JToken` classes in `InstaloaderSharp.Json`.

## The main classes

| Class | Purpose |
|---|---|
| `Instaloader` | Downloading: posts, stories, highlights, profiles, feed, saved posts, hashtags, locations; logging in; sessions. Its constructor takes the [command-line options](#options) |
| `InstaloaderContext` | The connection to Instagram (`L.Context`): requests, rate limiting, login state, error log |
| `Profile` | A profile: properties, and its posts, tagged posts, reels, followers and followees |
| `Post` | A post: properties, comments, likes, sidecar items |
| `StoryItem`, `Story`, `Highlight` | Story items, a profile's current story, highlights |
| `Hashtag` | A hashtag and its posts |
| `PostLocation`, `PostComment`, `PostSidecarNode` | Parts of a post |
| `NodeIterator<T>` | A list of items fetched from Instagram page by page, which can be frozen and resumed |
| `Structures` | Saving and loading metadata files (`SaveStructureToFile`, `LoadStructureFromFile`) |
| `LatestStamps` | The `--latest-stamps` database |

The properties of these classes are listed in [Properties reference](Properties-reference).

## Downloading a profile

```csharp
using System.Linq;
using InstaloaderSharp;

class Example
{
    static void Main()
    {
        using (var L = new Instaloader())
        {
            Profile profile = Profile.FromUsername(L.Context, "instagram");
            foreach (Post post in profile.GetPosts().Take(10))
                L.DownloadPost(post, profile.Username);
        }
    }
}
```

`DownloadPost` saves the post with the current options into the folder given by `--dirname-pattern` (default `{target}`, here `instagram`). It returns `true` if something was downloaded and `false` if all files existed.

The complete download of the command line, including profile picture, metadata and the options for stories and highlights, is `L.DownloadProfiles(...)`.

## Options

The constructor of `Instaloader` takes the options of the command line as named arguments:

```csharp
var L = new Instaloader(downloadVideos: false,
                        compressJson: false,
                        dirnamePattern: @"D:\Instagram\{profile}",
                        filenamePattern: "{date_utc:%Y-%m-%d}_{shortcode}");
```

| Argument | Option |
|---|---|
| `sleep` | Pauses between requests (`true`; never turn off for real downloads) |
| `quiet` | `--quiet` |
| `userAgent` | `--user-agent` |
| `dirnamePattern`, `filenamePattern`, `titlePattern` | `--dirname-pattern`, `--filename-pattern`, `--title-pattern` |
| `downloadPictures`, `downloadVideos`, `downloadVideoThumbnails` | `--no-pictures`, `--no-videos`, `--no-video-thumbnails` |
| `downloadGeotags`, `downloadComments` | `--geotags`, `--comments` |
| `saveMetadata`, `compressJson` | `--no-metadata-json`, `--no-compress-json` |
| `postMetadataTxtPattern`, `storyitemMetadataTxtPattern` | `--post-metadata-txt`, `--storyitem-metadata-txt` |
| `maxConnectionAttempts`, `requestTimeout` | `--max-connection-attempts`, `--request-timeout` |
| `resumePrefix` | `--resume-prefix`; `null` is `--no-resume` |
| `slide` | `--slide` |
| `fatalStatusCodes` | `--abort-on` |
| `iphoneSupport` | `--no-iphone` |
| `sanitizePaths` | `--sanitize-paths` |
| `rateController` | A function creating a custom `RateController`, to change the request limits |

## Logging in

```csharp
using (var L = new Instaloader())
{
    // Use a saved session (as with --login), ...
    L.LoadSessionFromFile("YOUR-USERNAME");

    // ... or log in with a password.
    try
    {
        L.Login("YOUR-USERNAME", password);
    }
    catch (TwoFactorAuthRequiredException)
    {
        L.TwoFactorLogin(code);
    }
    L.SaveSessionToFile();

    Console.WriteLine(L.TestLogin());   // the logged-in username, or null
}
```

`L.InteractiveLogin(username)` asks for the password (and two-factor code) on the console, like the command line. `L.Context.IsLoggedIn` and `L.Context.Username` tell the login state.

## Reading metadata

Properties are fetched from Instagram when they are first used, so reading metadata needs no downloads:

```csharp
Profile profile = Profile.FromUsername(L.Context, "instagram");
Console.WriteLine("{0} ({1}): {2} posts, {3} followers",
                  profile.Username, profile.FullName, profile.Mediacount, profile.Followers);
foreach (Post post in profile.GetPosts().Take(5))
    Console.WriteLine("{0:yyyy-MM-dd} {1} likes {2}",
                      post.DateUtc, post.Likes, string.Join(" ", post.CaptionHashtags));
```

Other lists of a profile: `GetTaggedPosts()`, `GetReels()`, `GetSavedPosts()` (your own profile, logged in), `GetFollowers()` and `GetFollowees()` (logged in). Of a post: `GetComments()`, `GetLikes()`, `GetSidecarNodes()`.

## Single posts, hashtags, stories and highlights

```csharp
Post post = Post.FromShortcode(L.Context, "DdwalmrkTSK");
L.DownloadPost(post, "single");

foreach (Post p in Hashtag.FromName(L.Context, "sunset").GetPostsResumable().Take(20))
    L.DownloadPost(p, "#sunset");

// Logged in:
foreach (Story story in L.GetStories(new[] { profile.Userid }))
    foreach (StoryItem item in story.GetItems())
        L.DownloadStoryitem(item, profile.Username);

L.DownloadHighlights(profile);
```

Further downloads: `L.DownloadFeedPosts(maxCount: 50)`, `L.DownloadSavedPosts()`, `L.DownloadLocation("123456789")`, `L.DownloadTagged(profile)`, `L.DownloadReels(profile)`, `L.DownloadProfilepic(profile)`.

## Saving and loading metadata files

```csharp
Structures.SaveStructureToFile(post, "post.json.xz");
var loaded = (Post)Structures.LoadStructureFromFile(L.Context, "post.json.xz");
```

The files are the same as those of the command line and of Instaloader; see [File formats](File-formats#metadata-files). `LoadStructureFromFile` returns a `Post`, `StoryItem`, `Profile`, `Hashtag` or `FrozenNodeIterator`.

## Resumable downloads

A `NodeIterator<T>` can save its position and continue later. `NodeIteration.ResumableIteration` does this the way the command line does: it resumes from a file if one exists, saves the position if the body throws, and deletes the file when the body completes.

```csharp
NodeIterator<Post> posts = profile.GetPosts();
NodeIteration.ResumableIteration(
    context: L.Context,
    iterator: posts,
    load: (ctx, path) => Structures.LoadStructureFromFile(ctx, path),
    save: (frozen, path) => Structures.SaveStructureToFile(frozen, path),
    formatPath: magic => "resume_" + magic + ".json.xz",
    checkBbd: true,
    enabled: true,
    body: (isResuming, startIndex) =>
    {
        foreach (Post post in posts)
            L.DownloadPost(post, profile.Username);
    });
```

`posts.Freeze()` and `posts.Thaw(frozen)` save and restore the position directly.

## Filters

The command line's filter expressions can be used in your own code:

```csharp
Func<Post, bool> filter = Program.FilterstrToFilterfunc<Post>("likes > 1000 and not is_video");
```

The function throws `InvalidArgumentException` for an invalid expression. See [Filtering posts and stories](Filtering-posts-and-stories).

## Errors and Ctrl+C

- Errors are reported as exceptions derived from `InstaloaderException`: `ProfileNotExistsException`, `LoginRequiredException`, `PrivateProfileNotFollowedException`, `ConnectionException` (with `QueryReturnedNotFoundException` and `TooManyRequestsException`), `LoginException` (with `BadCredentialsException` and `TwoFactorAuthRequiredException`), `InvalidArgumentException` and others. `AbortDownloadException` (not an `InstaloaderException`) means that Instagram demanded a verification and the download must stop.
- `L.Context.ErrorCatcher("description", () => { ... })` runs an action, catches `InstaloaderException`s, prints them and keeps them for the summary at the end, like the command line does for each target. `L.HasStoredErrors` tells whether there were any.
- `Interrupt.Install()` makes Ctrl+C throw a `KeyboardInterruptException` at the next request or pause instead of ending the process, so that resume information can be saved. Without it, Ctrl+C ends your program immediately.

```csharp
Interrupt.Install();
try
{
    foreach (Post post in profile.GetPosts())
        L.Context.ErrorCatcher("Download " + post.Shortcode, () => L.DownloadPost(post, profile.Username));
}
catch (KeyboardInterruptException)
{
    Console.WriteLine("Interrupted.");
}
```

## Things to keep in mind

- Keep `sleep: true` (the default) and the default rate controller for real downloads; without the pauses and limits Instagram quickly blocks the account.
- `Instaloader` and its context are not thread-safe. Use one instance per thread, and do not run several instances with the same account at the same time.
- Dispose the `Instaloader` (`using`) to close its connections.
- Lists are fetched lazily: iterating a `NodeIterator` makes requests as it goes, so use `Take(...)` to limit them.
- The library writes progress messages to the console like the command line, unless `quiet: true` is given.

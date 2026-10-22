InstaloaderSharp follows Instaloader closely, but it puts working correctly before reproducing Instaloader's behavior. Where Instaloader fails because Instagram changed, or because of a bug, InstaloaderSharp is fixed and differs deliberately. This page lists every such difference.

## Contents

- [Features that work again](#features-that-work-again)
- [How requests are sent](#how-requests-are-sent)
- [Fixes of the Instaloader 4.16 pre-release](#fixes-of-the-instaloader-416-pre-release)
- [File names](#file-names)
- [Logging in](#logging-in)
- [Error handling](#error-handling)
- [Platform](#platform)

## Features that work again

| Feature | Instaloader | InstaloaderSharp |
|---|---|---|
| `:feed` | Fails with `TypeError: 'NoneType' object is not subscriptable`: it uses a query that Instagram no longer answers with the feed | Loads the feed the way Instagram's website does: the first page from the home page, further pages with the query the home page declares. Instagram's suggested posts from accounts you do not follow are skipped |
| `:stories` | Uses a query for the list of accounts with stories that Instagram no longer answers | Gets the list from the app's story tray (`api/v1/feed/reels_tray/`) |
| Followers and followees (`@profile`, `Profile.GetFollowers()`, `GetFollowees()`) | Its query returns no profiles | Uses the friendships interface of Instagram's web app |
| Geotags (`--geotags`) | Fails with `KeyError: 'id'`: Instagram now names the location ID `pk` and no longer provides `slug` and `has_public_page`; the fallback lookup returns no data either | Works, also without logging in. In saved metadata, a post's location has no `slug` |
| `--igtv` | Fails with `KeyError: 'edges'` | Reports that Instagram has retired IGTV; former IGTV videos are downloaded as posts and reels. Lists that Instagram returns without items are treated as empty instead of failing |
| `Profile.GetFollowedHashtags()` | Fails with a `TypeError` | Returns an empty list, since Instagram removed following hashtags in December 2024 |
| Restricted profiles (for example age-restricted ones) | Reports `Profile ... does not exist.`: it reads profiles from their page as a logged-out visitor sees it, and Instagram does not show restricted profiles to logged-out visitors | Found when logged in: the profile's ID is read from the profile page as the session sees it, and the profile is loaded by its ID |
| Looking up a profile by its ID (renamed profiles) | Uses `api/v1/users/ID/info/`, which Instagram answers with 429 for logged-in sessions | Logged in, uses the GraphQL profile query, which takes the ID |
| Tagged posts (`--tagged`) | Downloads only the first few tagged posts: its query returns a first page of a few posts, followed by empty pages | Downloads all tagged posts, with the query of the website's tagged tab (`PolarisProfileTaggedTabContentQuery_connection`) |
| Searching Instagram (`TopSearchResults` in library use) | Uses the website's top search, which Instagram has retired (404) | Uses the search of Instagram's app (`api/v1/fbsearch/topsearch_flat/`, and `api/v1/fbsearch/places/` for places). Requires login; throws `LoginRequiredException` otherwise |
| Suggestions for misspelled usernames | Tries Instagram's retired top search (404) several times | Not made, since the search is retired; the error is `Profile ... does not exist.` |
| Logged-in post downloads | For some accounts, Instagram answers every query with a redirect to its home page (`HTTP redirect from https://www.instagram.com/graphql/query to https://www.instagram.com/`), and no posts are downloaded, without an error | Works; see below |

## How requests are sent

- **Logged-in queries are sent like the website's.** Since October 2026, Instagram redirects logged-in GraphQL queries to its home page, for some accounts, unless they carry the session's request token (`fb_dtsg`, from Instagram's home page), the query's name and the browser's `Sec-Fetch-*` headers. InstaloaderSharp sends them for queries of profile posts, reels, post details and further pages of `:feed`. Reels are loaded with the query of the website's reels tab (`PolarisProfileReelsTabContentQuery_connection`), since Instagram answers Instaloader's older reels query without reels when it is sent this way.
- **Access token for the app interface.** Requests to Instagram's app interface (`i.instagram.com`) obtain the app's access token first, if the session does not have one yet, for example a session imported from a browser. Without it, Instagram refuses the story tray and profile information.
- **Retries of POST queries are sent as POST.** Instaloader retries failed `doc_id` (POST) queries as GET requests.

## Fixes of the Instaloader 4.16 pre-release

These changes of the Instaloader 4.16 pre-release are included already, so InstaloaderSharp behaves in these points like Instaloader 4.16 rather than 4.15.4:

- **Profile posts** are loaded, when logged in, with the website's newer posts query (`29104886249145907`), sent to its `api/graphql` endpoint with the page's tokens (`fb_dtsg`, `lsd`, `av`). Instagram answers the previous query with 400 `feedback_required`.
- **Highlights are counted** while they are downloaded: `[3/9] Retrieving highlights "..." from profile ...`.
- **A page request that is redirected to the login page** is reported as such (`Redirected to login page. Use --login or --load-cookies.`) instead of as `302 Found`.
- **Redirects of POST requests** no longer repeat the request's data as URL arguments.
- **`Profile.GetPosts()`** raises `PrivateProfileNotFollowedException` for a private profile that you do not follow, instead of returning no posts (Instagram answers with an empty list).

Instaloader 4.16 also loads `:feed` from the app's timeline (`api/v1/feed/timeline/`). InstaloaderSharp keeps loading the feed the way the website does, which returns the same posts with fewer requests: in a test, the timeline returned about two posts per request among suggestions.

## File names

- **Formatted values are cleaned.** Values with a format specification in `--dirname-pattern` and `--filename-pattern`, such as `{date_utc:%c}` or `{date_utc:%H:%M}`, get the same replacements of characters that are invalid in Windows file names as other values (`:` becomes `：`). Instaloader leaves them unchanged and fails with `OSError: [Errno 22] Invalid argument`. Path separators from formats are kept, so `{date_utc:%Y/%m}` still creates subfolders.
- **`%c`, `%#c` and `%#x`** give the same text as Python 3.14 on Windows.
- **Long paths** of more than 260 characters work when they are enabled in Windows, as in Python.

## Logging in

- **`--load-cookies` is built in.** Instaloader needs the optional `browser_cookie3` package. InstaloaderSharp supports Firefox, LibreWolf and the Chromium-based browsers on Windows itself. Cookies with Chrome's app-bound encryption (`v20`) cannot be decrypted, as with `browser_cookie3`, and Chrome may need to be closed so that its cookie database can be read. Safari only exists on macOS.

## Error handling

- **Syntax errors in filters** are reported as an error message, with exit code 2, instead of a Python traceback.
- **Several profiles** are downloaded in the order they were given, instead of in the order of Python's set.

## Platform

InstaloaderSharp is a Windows program for .NET Framework. It has no Python API; its [library](Library-use) is a .NET API with the same structure as Instaloader's Python API.

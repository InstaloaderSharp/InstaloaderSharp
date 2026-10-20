Terms used in InstaloaderSharp and this manual.

| Term | Meaning |
|---|---|
| **Abort** | Stopping the whole download because Instagram demands a verification, or because of `--abort-on`. Exit code 4 |
| **App interface** | The interface of Instagram's mobile apps (`i.instagram.com`). Used when logged in for the largest versions of pictures and videos, stories and the story tray; turned off with `--no-iphone` |
| **Caption** | The text of a post. Saved in the post's `.txt` file |
| **Carousel** | Instagram's name for a post with several pictures or videos; see *sidecar* |
| **Checkpoint** | A verification that Instagram demands before it accepts a login or further requests, done in a browser or the app |
| **Cookies** | Small pieces of data with which a website recognizes a logged-in browser. `--load-cookies` imports Instagram's cookies from a browser |
| **Cover** | The picture of a highlight, saved as `..._cover.jpg` |
| **`date_utc`, `date_local`** | A post's publication time in UTC, and in your computer's time zone |
| **doc_id** | The ID of a query of Instagram's website. Instagram replaces them from time to time, which is one reason why downloaders need updates |
| **`fb_dtsg`** | A request token that Instagram's website embeds in its pages and sends with its queries; InstaloaderSharp sends it with logged-in queries |
| **Fast update** | `--fast-update`: stopping at the first post that was downloaded before |
| **Feed** | The posts of the accounts you follow, as on Instagram's home page. Target `:feed` |
| **Followees** | The accounts a profile follows. Target `@profilename` |
| **Followers** | The accounts that follow a profile |
| **Geotag** | The location a post is tagged with. Saved with `--geotags` |
| **GraphImage, GraphVideo, GraphSidecar** | The `typename` of a post: one picture, one video, or several pictures and videos |
| **GraphQL** | The query language of Instagram's website; most requests of the program are GraphQL queries |
| **Hashtag** | A word marked with `#` in a caption. Target `"#hashtag"` |
| **Highlight** | A collection of story items that a profile keeps on its profile page after the 24 hours. Downloaded with `--highlights` |
| **IGTV** | Instagram's former long-video service, retired in 2022. Its videos are now posts and reels |
| **Instaloader** | The Python program that InstaloaderSharp is a C# / .NET Framework version of: [github.com/instaloader/instaloader](https://github.com/instaloader/instaloader) |
| **Latest stamps** | `--latest-stamps`: a file that remembers the time of the newest downloaded items of each profile |
| **Location ID** | The numeric ID of a location, from its URL `instagram.com/explore/locations/ID/`. Target `%ID` |
| **Magic** | The eight characters in a resume file's name, computed from the list it belongs to |
| **Mediaid** | The numeric ID of a post or story item |
| **Metadata** | Everything Instagram reports about an item besides its pictures and videos: date, caption, likes, comments, tagged users, location and more. Saved in `.json.xz` files |
| **`pcaption`** | A shortened, one-line version of the caption for file names |
| **Pinned post** | A post that a profile has fixed at the top of its profile, out of date order. Up to three |
| **Profile** | An Instagram account, as a target given by its username |
| **Rate limit** | The number of requests Instagram allows in a given time. The program paces itself to stay within it |
| **Reel** | A short video. Downloaded with the posts, and with `--reels` |
| **Resume file** | `iterator_MAGIC.json.xz`: the position of an interrupted download, for continuing it |
| **Saved posts** | Posts you bookmarked on Instagram. Target `:saved` |
| **Session** | A login, kept in a session file so that the password is not needed every time |
| **Session file** | `%LOCALAPPDATA%\Instaloader\session-USERNAME`: the saved login cookies of an account |
| **Shortcode** | The ID of a post in its URL, `instagram.com/p/SHORTCODE/`. Target `-- -SHORTCODE` |
| **Sidecar** | A post with several pictures or videos (typename `GraphSidecar`), saved as numbered files `_1`, `_2`, ... |
| **Slide** | One picture or video of a sidecar. `--slide` selects which are downloaded |
| **Story, story item** | Pictures and videos that are visible for 24 hours. A profile's story consists of story items |
| **Story tray** | The row of stories at the top of Instagram's app; the source of `:stories` |
| **Tagged posts** | Posts of other accounts in which a profile is tagged. Downloaded with `--tagged` |
| **Target** | What to download, given on the command line: a profile, hashtag, location, shortcode, `:feed`, `:stories`, `:saved` or a metadata file |
| **Template** | A pattern with placeholders in curly braces, such as `{date_utc}_UTC`, for file names, folder names and caption files |
| **Thumbnail** | The still picture shown for a video, downloaded unless `--no-video-thumbnails` is given |
| **Two-factor authentication (2FA)** | A login that needs a code from an app or text message in addition to the password |
| **User agent** | The identification of the browser that a request claims to come from; set with `--user-agent` |
| **UTC** | Coordinated Universal Time, the time zone of file names (`..._UTC`) |
| **WinHTTP** | The HTTP component of Windows that InstaloaderSharp uses for its connections |
| **xz** | A compression format. Metadata files are xz-compressed JSON (`.json.xz`) |

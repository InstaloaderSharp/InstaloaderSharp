This page describes the formats of the files InstaloaderSharp reads and writes, for those who want to process them with other programs. All formats are the same as Instaloader's; see [Interoperability with Instaloader](Interoperability-with-Instaloader).

## Contents

- [Overview](#overview)
- [Pictures and videos](#pictures-and-videos)
- [Caption files](#caption-files)
- [Metadata files](#metadata-files)
- [Comments](#comments)
- [Geotags](#geotags)
- [Profile ID files](#profile-id-files)
- [Resume files](#resume-files)
- [Latest-stamps file](#latest-stamps-file)
- [Session files](#session-files)
- [Option files](#option-files)
- [Browser cookie databases](#browser-cookie-databases)

## Overview

| File | Format | Written by |
|---|---|---|
| `*.jpg`, `*.mp4`, `*.webp`, `*.heic` | As served by Instagram, unchanged | Every download |
| `*.txt` | UTF-8 text | Captions, `--post-metadata-txt`, `--storyitem-metadata-txt` |
| `*.json.xz`, `*.json` | JSON, xz-compressed or pretty-printed | Metadata of posts, story items, profiles and hashtags |
| `*_comments.json` | JSON | `--comments` |
| `*_location.txt` | UTF-8 text | `--geotags` |
| `id` | Text: one number | Profile downloads without `--latest-stamps` |
| `iterator_*.json.xz` | JSON, xz-compressed | Interrupted downloads |
| `latest-stamps.ini` | INI | `--latest-stamps` |
| `session-USERNAME` | Python pickle | Logins |

Text files that the program writes use Windows line endings (CR LF), as Instaloader's do on Windows. Geotag files are the exception: like Instaloader, the program writes them with LF line endings.

## Pictures and videos

Pictures and videos are saved byte for byte as Instagram's content servers deliver them; nothing is converted or re-encoded. The extension comes from the server's content type (`image/jpeg` gives `.jpg`). The file's modification time is set to the post's publication time.

## Caption files

UTF-8 text without byte order mark, ending with a line break. By default the post's caption; with `--post-metadata-txt`, the formatted template lines. When a caption changes, the previous file is kept as `_old_01.txt` (see [Downloaded files](Downloaded-files#captions)). An unchanged caption is recognized regardless of its line endings.

## Metadata files

A metadata file contains a JSON object with two members:

```json
{
    "instaloader": {
        "node_type": "Post",
        "version": "4.16"
    },
    "node": {
        "__typename": "GraphSidecar",
        "shortcode": "DdwalmrkTSK",
        "id": "3994809802840683658",
        "date": 1790438460,
        "...": "..."
    }
}
```

- `instaloader.node_type` is `Post`, `StoryItem`, `Profile`, `Hashtag` or `FrozenNodeIterator` (for resume files).
- `instaloader.version` is the Instaloader version the file is compatible with: InstaloaderSharp writes `4.16`, not its own version, so that its files are identical to Instaloader's.
- `node` is the item as Instagram reported it, in the form Instaloader uses (Instagram's GraphQL field names such as `edge_media_preview_like`, `display_url` and `taken_at_timestamp`). Posts that were fetched from Instagram's app interface also contain the app's data under `iphone_struct`. Profiles contain their metadata; posts may contain their owner's profile under `owner`.

**`.json.xz`** files contain the JSON in compact form (no spaces, keys not sorted), compressed with xz (LZMA2, without integrity check). Any xz tool can decompress them, for example 7-Zip or `xz -d`.

**`.json`** files (with `--no-compress-json`) contain the JSON pretty-printed with an indentation of four spaces and sorted keys.

In both, non-ASCII characters are written as `\uXXXX` escapes, as Python's `json` module does, and numbers keep Python's notation. The files can be read by any JSON parser.

The program can read every file it writes, and uses them as [targets](Targets#metadata-files).

## Comments

`_comments.json` contains a pretty-printed JSON list of comments, newest first:

```json
[
    {
        "id": 18012345678901234,
        "created_at": 1790440000,
        "text": "Congratulations!",
        "owner": { "id": "1234567", "username": "someone", "...": "..." },
        "likes_count": 3,
        "answers": [
            {
                "id": 18023456789012345,
                "created_at": 1790441000,
                "text": "Thank you!",
                "owner": { "...": "..." },
                "likes_count": 1
            }
        ]
    }
]
```

`created_at` is a Unix timestamp (seconds since 1970, UTC). `owner` is the author's profile, in the same form as in profile metadata. `answers` are the replies, newest first. When the comments are downloaded again, the new list is merged with the existing file by comment ID, so comments deleted on Instagram are kept.

## Geotags

`_location.txt` contains the location's name on the first line and, when Instagram provides coordinates, a Google Maps link on the second line:

```
Uppsala, Sweden
https://maps.google.com/maps?q=59.8586,17.6389&ll=59.8586,17.6389
```

## Profile ID files

The file `id` in a profile's folder contains the profile's numeric ID as decimal digits followed by a line break. If `--dirname-pattern` contains neither `{target}` nor `{profile}`, all profiles share a folder and the file is named `USERNAME_id` instead.

## Resume files

`iterator_MAGIC.json.xz` is a metadata file with `node_type` `FrozenNodeIterator`. Its `node` holds the state of the interrupted list:

| Member | Meaning |
|---|---|
| `query_hash`, `doc_id` | The Instagram query that produces the list |
| `query_variables` | The query's variables, such as the profile's ID or username |
| `query_referer` | The page the query is sent from |
| `context_username` | The account that was logged in, or `null` |
| `total_index` | How many items were processed |
| `best_before` | Unix timestamp after which the file is no longer used (29 days after it was written) |
| `remaining_data` | The rest of the current page of results, including Instagram's position marker for the next page |
| `first_node` | The first (newest) item of the list, for `--latest-stamps` |

`MAGIC` is the first 6 bytes of a BLAKE2b hash, in URL-safe Base64 (8 characters), of the JSON text `[query_hash, query_variables, query_referer, context_username]`. A command therefore finds its resume file again by computing the same hash. The prefix `iterator` is changed with `--resume-prefix`.

## Latest-stamps file

An INI file in the dialect of Python's `configparser`, with one section per profile:

```ini
[instagram]
profile-id = 25025320
profile-pic = 552315460_18667771684001234_n.jpg
post-timestamp = 2026-10-19T18:01:46.000000+0200
story-timestamp = 2026-10-19T09:12:00.000000+0200
```

| Key | Value |
|---|---|
| `profile-id` | The profile's numeric ID |
| `profile-pic` | The file name of the last downloaded profile picture, as on Instagram's server |
| `post-timestamp`, `tagged-timestamp`, `reels-timestamp`, `story-timestamp`, `igtv-timestamp` | Times of the newest downloaded items |

Times are written in the format `%Y-%m-%dT%H:%M:%S.%f%z`: date, `T`, time with exactly six decimal places, and the time zone offset without colon (`+0200`). A time in any other format cannot be read and counts as 1 January 1970. When a profile is renamed, its section is renamed. See [Keeping an archive up to date](Keeping-an-archive-up-to-date#latest-stamps).

## Session files

`session-USERNAME` contains the session's cookies as a Python dictionary from cookie name to value (`sessionid`, `csrftoken`, `ds_user_id`, `mid` and others), serialized with Python's `pickle`. InstaloaderSharp reads all pickle protocols that Python can write and writes protocol 4, like Python 3.8 and later do by default. The file contains no password, but the `sessionid` cookie gives access to the account. See [Logging in](Logging-in#session-files).

## Option files

Files passed with `+FILE` contain one command-line argument per line, in UTF-8 (or the Windows code page if the file is not valid UTF-8). See [Command-line options](Command-line-options#reading-options-from-a-file).

## Browser cookie databases

`--load-cookies` reads, without changing them:

- **Firefox and LibreWolf:** the SQLite database `cookies.sqlite` of the profile, including changes that are still in its write-ahead log (`cookies.sqlite-wal`), so the browser can stay open.
- **Chromium-based browsers:** the SQLite database `Network\Cookies` (or `Cookies` in older versions) of the profile, decrypted with the key from the browser's `Local State` file, which Windows protects for your user account (DPAPI). Cookies with Chrome's app-bound encryption (`v20`) cannot be decrypted.

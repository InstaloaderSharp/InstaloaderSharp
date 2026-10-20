This page describes where InstaloaderSharp saves what it downloads, and what each file contains. The folder and file names can be changed with the options in [File and folder names](File-and-folder-names); this page describes the defaults.

## Contents

- [Where files go](#where-files-go)
- [A profile's folder](#a-profiles-folder)
- [Posts](#posts)
- [Posts with several pictures or videos](#posts-with-several-pictures-or-videos)
- [Stories and highlights](#stories-and-highlights)
- [Tagged posts and reels](#tagged-posts-and-reels)
- [Profile pictures](#profile-pictures)
- [Captions](#captions)
- [Metadata files](#metadata-files)
- [Comments](#comments)
- [Geotags](#geotags)
- [File dates](#file-dates)
- [Downloading again](#downloading-again)
- [Renamed profiles](#renamed-profiles)
- [Other files](#other-files)

## Where files go

Every target gets its own folder in the current directory (the folder the command prompt is in), named after the target:

| Target | Folder |
|---|---|
| `instagram` | `instagram` |
| `-- -DdwalmrkTSK` | `-DdwalmrkTSK` |
| `"#sunset"` | `#sunset` |
| `%123456789` | `%123456789` |
| `:feed`, `:stories`, `:saved` | `：feed`, `：stories`, `：saved` |

Characters that are not allowed in Windows file names are replaced with similar-looking Unicode characters, such as the full-width colon `：` (U+FF1A) in `：feed`. See [File and folder names](File-and-folder-names#invalid-characters) for the full list.

To download into another folder, change to that folder first (`cd /d D:\Instagram`), or use `--dirname-pattern`, for example `--dirname-pattern "D:\Instagram\{target}"`.

## A profile's folder

```
instagram\
    id                                  the profile's numeric ID
    instagram_25025320.json.xz          profile metadata
    2025-09-20_00-00-36_UTC_profile_pic.jpg
    2026-10-08_17-10-31_UTC.jpg         picture, or thumbnail of a video
    2026-10-08_17-10-31_UTC.mp4         video
    2026-10-08_17-10-31_UTC.txt         caption
    2026-10-08_17-10-31_UTC.json.xz     post metadata
    2026-09-26_16-01-00_UTC_1.jpg       a post with three pictures
    2026-09-26_16-01-00_UTC_2.jpg
    2026-09-26_16-01-00_UTC_3.jpg
    2026-09-26_16-01-00_UTC.txt
    2026-09-26_16-01-00_UTC.json.xz
    Summer 2026\                        a highlight (with --highlights)
    ：tagged\                            posts the profile is tagged in (with --tagged)
```

## Posts

Every post is saved under a name made of its date and time in UTC, `YYYY-MM-DD_HH-MM-SS_UTC`, followed by an extension:

| File | Contents | Leave out with |
|---|---|---|
| `.jpg` (or `.webp`, `.heic`, ...) | The picture. For a video, its thumbnail picture | `--no-pictures`, `--no-video-thumbnails` |
| `.mp4` | The video | `--no-videos` |
| `.txt` | The caption | `--no-captions` |
| `.json.xz` | The post's metadata | `--no-metadata-json` |
| `_comments.json` | The comments, only with `--comments` | |
| `_location.txt` | The location, only with `--geotags` | |

The extension of pictures and videos comes from the type that Instagram's server reports, so a picture is usually `.jpg`, but can also be `.webp` or `.heic`.

Two posts published in the same second by the same profile would get the same name. This practically never happens with the default pattern; if you change `--filename-pattern`, make sure it stays unique, for example by including `{shortcode}`.

## Posts with several pictures or videos

A post with several pictures or videos (Instagram calls it a carousel, Instaloader a *sidecar*, type `GraphSidecar`) gets one numbered file per picture or video: `_1.jpg`, `_2.mp4`, `_3.jpg` and so on, and one caption and one metadata file for the whole post. Videos in a sidecar also get a numbered thumbnail picture.

`--slide` downloads only some of them, for example `--slide 1` for the first one; see [Choosing what to download](Choosing-what-to-download#parts-of-posts-with-several-pictures).

## Stories and highlights

Story items are saved like posts, with the same naming (their date and time in UTC):

| Download | Folder |
|---|---|
| `--stories` with profile targets | The profile's folder |
| `:stories` | `：stories` |
| `--highlights` | A subfolder of the profile's folder for each highlight, named after its title, e.g. `instagram\Summer 2026` |

A story item gets a `.jpg` (or the thumbnail of a video), an `.mp4` for videos and a `.json.xz` metadata file. No caption file is written for story items unless you give `--storyitem-metadata-txt`. Each highlight folder also gets the highlight's cover picture, `..._UTC_cover.jpg`.

## Tagged posts and reels

- `--tagged` saves the posts the profile is tagged in to the subfolder `：tagged` of the profile's folder.
- `--reels` saves the profile's reels in the profile's folder, like posts. A reel that is also a post is saved only once.

## Profile pictures

The profile picture is saved as `YYYY-MM-DD_HH-MM-SS_UTC_profile_pic.jpg` in the profile's folder. The date is when the picture was last changed, as reported by Instagram's server, so a new file appears when the profile picture changes, and old profile pictures are kept.

When you are logged in, the largest available version is downloaded; if a smaller version was downloaded before, it is replaced. With `--latest-stamps`, the program remembers the file name of the last downloaded picture and does not download it again; see [Keeping an archive up to date](Keeping-an-archive-up-to-date#latest-stamps).

Hashtag targets get the hashtag's picture in the same way, `..._UTC_profile_pic.jpg` in the hashtag's folder.

The name of profile pictures and highlight covers is set with `--title-pattern`; see [File and folder names](File-and-folder-names).

## Captions

The `.txt` file contains the post's caption, as UTF-8 text with a final line break. With `--post-metadata-txt`, it contains whatever you choose instead, for example the number of likes; see [File and folder names](File-and-folder-names#caption-file-templates).

If a caption changes on Instagram (the owner edited it), the next download keeps the old version: the previous file is renamed to `..._old_01.txt` and the new caption is written to `.txt`. Older versions move on to `_old_02.txt`, `_old_03.txt` and so on, so `_old_01` is always the most recent earlier version. The progress line shows `updated` in that case, and `unchanged` if the caption is the same.

## Metadata files

The `.json.xz` file contains everything Instagram reported about the post or story item: its ID and shortcode, owner, date, caption, likes, number of comments, tagged users, location, the URLs of its pictures and videos, and more. It is JSON, compressed with xz.

- With `--no-compress-json`, plain, pretty-printed `.json` files are written instead. They are larger, but can be read in any text editor.
- To read a `.json.xz` file, decompress it with [7-Zip](https://www.7-zip.org/), or use it as a [target](Targets#metadata-files) to download the post again.
- The profile's metadata is saved as `username_ID.json.xz`, for example `instagram_25025320.json.xz`.
- Metadata files are rewritten on every download of the post, so they always have the latest numbers.

The format is described in [File formats](File-formats#metadata-files).

## Comments

With `--comments` (requires login), the comments of each post are saved in `..._comments.json`: a list of comments, each with its ID, time, text, author, number of likes and its replies (`answers`). The file is plain JSON, not compressed.

Downloading again updates the file: new comments are added and existing ones updated, and comments that were deleted on Instagram are kept. See [File formats](File-formats#comments).

## Geotags

With `--geotags`, a post that is tagged with a location gets a `..._location.txt` file with the location's name and, if Instagram provides coordinates, a Google Maps link:

```
Uppsala, Sweden
https://maps.google.com/maps?q=59.8586,17.6389&ll=59.8586,17.6389
```

## File dates

The modification time of every downloaded picture, video, caption, geotag and comments file is set to the time the post or story item was published. Sorting a folder by date in Windows Explorer therefore sorts the posts by when they were posted, not when they were downloaded. Profile pictures and highlight covers get the date Instagram's server reports for the picture. Metadata files keep the time they were written.

## Downloading again

When you download a target again, files that already exist are skipped (`... exists`), so only new posts and missing files are downloaded. The metadata file is rewritten each time, and the caption file only if the caption changed.

Without `--fast-update`, the program still goes through all posts of the profile to check them. With `--fast-update`, it stops at the first post that was already downloaded, and with `--latest-stamps` at the first post older than the previous download; see [Keeping an archive up to date](Keeping-an-archive-up-to-date).

If you delete a picture or video, it is downloaded again the next time (without `--fast-update`). If you want to keep a post out of your archive permanently, use a [filter](Filtering-posts-and-stories).

## Renamed profiles

The first download of a profile stores its numeric ID in a file named `id` in the profile's folder (with `--latest-stamps`, in the stamps file instead). The ID never changes, even when the username does.

When you download a profile again by its old username:

1. If no profile has that name anymore, the program looks the profile up by its stored ID (`Trying to find profile oldname using its unique ID 12345.`).
2. If another profile has taken the name, the stored ID does not match (`Profile oldname does not match the stored unique ID 12345.`), and the program also looks up the original profile by its ID.
3. If the profile now has a new name, it reports `Profile oldname has changed its name to newname.`, renames the folder `oldname` to `newname` (or updates the stamps file), and downloads the profile under its new name.

## Other files

| File | Meaning |
|---|---|
| `id` | The profile's numeric ID, for finding renamed profiles |
| `iterator_XXXXXXXX.json.xz` | Information for resuming an interrupted download; deleted when the download completes. See [Interrupting and resuming](Interrupting-and-resuming) |
| `*.temp` | A file that is still being downloaded. If the program is stopped, the `.temp` file is left behind and is replaced by the next download |

Files outside the download folders:

| File | Meaning |
|---|---|
| `%LOCALAPPDATA%\Instaloader\session-USERNAME` | Saved login; see [Logging in](Logging-in#session-files) |
| `%LOCALAPPDATA%\Instaloader\latest-stamps.ini` | Default database of `--latest-stamps` |

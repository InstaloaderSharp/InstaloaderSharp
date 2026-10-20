A **target** is what you want to download: a profile, a post, a hashtag and so on. Targets are given after the options on the command line, and any number of them can be given at once.

## Contents

- [Overview](#overview)
- [Profiles](#profiles)
- [Followees of a profile](#followees-of-a-profile)
- [Single posts](#single-posts)
- [Hashtags](#hashtags)
- [Locations](#locations)
- [Your feed](#your-feed)
- [Stories of the accounts you follow](#stories-of-the-accounts-you-follow)
- [Your saved posts](#your-saved-posts)
- [Metadata files](#metadata-files)
- [Several targets at once](#several-targets-at-once)
- [Invalid targets](#invalid-targets)

## Overview

| Target | Downloads | Folder | Login required |
|---|---|---|---|
| `profilename` | All posts and the profile picture of a profile | `profilename` | Only for private profiles |
| `@profilename` | All profiles that `profilename` follows | one folder per profile | Yes |
| `-- -SHORTCODE` | A single post | `-SHORTCODE` | Only for private posts |
| `"#hashtag"` | Posts with a hashtag, and the hashtag's picture | `#hashtag` | Yes |
| `%location_id` | Posts tagged with a location | `%location_id` | Yes |
| `:feed` | Posts in your feed | `：feed` | Yes |
| `:stories` | Current stories of all accounts you follow | `：stories` | Yes |
| `:saved` | Posts you saved | `：saved` | Yes |
| `file.json`, `file.json.xz` | A post or story item again, from a metadata file saved earlier | the file's folder | Depends on the item |

The folder is where the files go with the default `--dirname-pattern` of `{target}`; see [File and folder names](File-and-folder-names) to change it. Colons are not allowed in Windows folder names, so `:feed` becomes `：feed` with a full-width colon (U+FF1A), and the same for `:stories` and `:saved`.

## Profiles

```
InstaloaderSharp.exe instagram
```

A profile is given by its username, exactly as in its URL (`https://www.instagram.com/instagram/`). Usernames are not case-sensitive. A trailing `/` is ignored, so you can use the auto-completion of your command prompt on a folder that was downloaded before.

By default this downloads the profile picture, the profile's metadata and all its posts. The options in [Choosing what to download](Choosing-what-to-download) add stories, highlights, tagged posts and reels, or leave out the posts.

**Private profiles** can only be downloaded if you are [logged in](Logging-in) with an account that follows them. Otherwise only the profile picture and the profile's metadata are saved, and the error `Login required.` or `Private but not followed.` is reported.

**Renamed profiles** are found again: the first download stores the profile's numeric ID in a file named `id` in its folder. If you later give the old name, the program looks the profile up by its ID, prints that it was renamed, and renames the folder to the new name. See [Downloaded files](Downloaded-files#renamed-profiles).

**Profiles that blocked you**: when you are logged in and a profile cannot be found or has blocked your account, the program tries again without logging in. If the profile is public, it is then downloaded anonymously, with a warning.

## Followees of a profile

```
InstaloaderSharp.exe --login YOUR-USERNAME @profilename
```

Downloads every profile that `profilename` follows, as if you had given all their usernames. This requires a login, and it can be a very large download. To download the posts of the accounts *you* follow, `:feed` is usually a better choice than `@YOUR-USERNAME`.

## Single posts

```
InstaloaderSharp.exe -- -DdwalmrkTSK
```

A post (or reel) is given by its **shortcode**, the part after `/p/` or `/reel/` in its URL, written with a leading `-`. Because the target starts with `-`, it must come after `--`, which tells the program that no options follow. Put all options before the `--`:

```
InstaloaderSharp.exe --no-videos --comments -- -DdwalmrkTSK -DePb6WLuVae
```

Each post is saved in a folder named after the target, here `-DdwalmrkTSK`. Use `--dirname-pattern "{profile}"` to save it in the folder of its owner's profile instead.

## Hashtags

```
InstaloaderSharp.exe --login YOUR-USERNAME --count 50 "#sunset"
```

Downloads posts with a hashtag, and the hashtag's picture. Put the target in double quotes. Instagram lists a hashtag's posts only to logged-in users. Popular hashtags have millions of posts, so use `--count` to limit the number.

With `--no-posts` only the hashtag's picture is downloaded, and with `--no-profile-pic` only the posts.

## Locations

```
InstaloaderSharp.exe --login YOUR-USERNAME --count 100 %123456789
```

Downloads posts tagged with a location. The location is given by its numeric ID, the number in its URL `https://www.instagram.com/explore/locations/123456789/...`. You can find the ID in the [geotag file](Downloaded-files#geotags) or metadata of a post tagged with the location, or by opening the location in a web browser.

## Your feed

```
InstaloaderSharp.exe --login YOUR-USERNAME --count 100 :feed
```

Downloads the posts in your feed: the posts of the accounts you follow, newest first. Posts that Instagram suggests from accounts you do not follow are skipped. The feed is endless in practice, so use `--count` or `--fast-update`.

## Stories of the accounts you follow

```
InstaloaderSharp.exe --login YOUR-USERNAME :stories
```

Downloads the current stories (those of the last 24 hours) of every account you follow that has one, into the folder `：stories`. To download the stories of particular profiles into their own folders instead, use `--stories` with the profiles as targets; see [Choosing what to download](Choosing-what-to-download#stories-highlights-tagged-posts-and-reels).

## Your saved posts

```
InstaloaderSharp.exe --login YOUR-USERNAME :saved
```

Downloads the posts you saved (bookmarked) in the Instagram app or website, newest first. Only your own saved posts can be downloaded, so this needs a login.

## Metadata files

```
InstaloaderSharp.exe instagram\2026-09-26_16-01-00_UTC.json.xz
```

Every downloaded post and story item has a metadata file (`.json.xz` or `.json`). Giving such a file as a target downloads the item again into the file's folder, using the information in the file. This is useful to get back pictures or videos you deleted, or to add comments or geotags to an earlier download.

The download links stored in a metadata file expire after a while. If downloading from an old file fails (for example with `403 Forbidden`), download a post by its shortcode instead. Story items disappear from Instagram after 24 hours, unless they are part of a highlight.

`--post-filter` and `--storyitem-filter` apply to these targets too. A profile's metadata file (`profilename_12345.json.xz`) cannot be used as a target: give the username instead.

## Several targets at once

Targets of any kind can be mixed:

```
InstaloaderSharp.exe --login YOUR-USERNAME --stories profile1 profile2 "#cats" :saved
```

Hashtags, locations, single posts, `:feed`, `:stories`, `:saved` and metadata files are downloaded in the order given. Profiles are collected first and then downloaded one after the other, in the order given (`[1/2] Downloading profile profile1`). Stories of all profiles requested with `--stories` are downloaded together after the profiles.

An error with one target does not stop the others: it is printed, the program continues with the next target, and all errors are repeated at the end. The [exit code](Command-line-options#exit-codes) is then 1.

Long lists of targets can be kept in a text file; see [Command-line options](Command-line-options#reading-options-from-a-file).

## Invalid targets

Usernames can contain letters, digits, `.` and `_`; hashtags can contain letters, digits and `_`; location IDs only digits. Anything else is reported as an error such as `Invalid username my-profile`. A shortcode given without `--` in front is taken as an option and reported as an unknown argument.

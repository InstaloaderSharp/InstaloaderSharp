Once a profile is downloaded, you will want to fetch only what is new from time to time. This page explains the options for that, and how to run updates automatically.

## Contents

- [Running the same command again](#running-the-same-command-again)
- [Fast updates](#fast-updates)
- [Latest stamps](#latest-stamps)
- [Fast updates or latest stamps?](#fast-updates-or-latest-stamps)
- [Limiting the number of posts](#limiting-the-number-of-posts)
- [Keeping a list of profiles](#keeping-a-list-of-profiles)
- [Scheduled downloads](#scheduled-downloads)

## Running the same command again

The simplest way to update a download is to run the same command again. Files that already exist are skipped (`... exists`), so only new posts are downloaded, and the metadata of all posts is refreshed. For a large profile this still goes through all its posts, which takes long and makes many requests. The two options below stop early instead.

## Fast updates

```
InstaloaderSharp.exe --fast-update profilename
```

With `--fast-update` (`-F`), the download of each target stops at the first post for which nothing new was downloaded, because all its files already exist. Since Instagram lists posts newest first, everything after that post was downloaded before.

- A profile can pin up to three posts to the top of its profile. Pinned posts are not in date order, so the first three posts of a profile do not stop a fast update.
- For stories, the download of a profile's stories stops at the first story item that exists already.
- `--fast-update` works for every kind of target: profiles, hashtags, locations, `:feed`, `:saved`, `:stories`, and the `--tagged` and `--reels` downloads.
- It relies on the downloaded files. If you delete or move them, the next fast update downloads them again. It cannot be combined with `--no-pictures`.

## Latest stamps

```
InstaloaderSharp.exe --latest-stamps profilename
```

With `--latest-stamps`, the program remembers, for every profile, the time of the newest post, story, tagged post and reel it downloaded, in a small database file. The next time, it only downloads items that are newer, and stops at the first older one. Up to three pinned posts are taken into account, as with `--fast-update`.

The database is `%LOCALAPPDATA%\Instaloader\latest-stamps.ini` by default. Give a file name after the option to use another one:

```
InstaloaderSharp.exe --latest-stamps D:\Instagram\latest-stamps.ini profilename
```

Because the option takes an optional file name, put the targets after another option or after `--` if the next word could be mistaken for a file name: `--latest-stamps -- profilename` uses the default file.

For each profile, the file stores:

| Key | Meaning |
|---|---|
| `profile-id` | The profile's numeric ID, for finding [renamed profiles](Downloaded-files#renamed-profiles) (instead of the `id` file) |
| `profile-pic` | The file name of the last downloaded profile picture; it is downloaded again only when it changes |
| `post-timestamp` | Time of the newest downloaded post |
| `tagged-timestamp` | Time of the newest downloaded tagged post (`--tagged`) |
| `reels-timestamp` | Time of the newest downloaded reel (`--reels`) |
| `story-timestamp` | Time of the newest downloaded story item (`--stories`) |
| `igtv-timestamp` | Kept from earlier versions; IGTV is retired |

The file is a plain text INI file and can be edited, for example to download a profile again from a certain date:

```ini
[profilename]
profile-id = 25025320
profile-pic = 552315460_18667771684001234_n.jpg
post-timestamp = 2026-10-01T00:00:00.000000+0000
story-timestamp = 2026-10-19T18:01:46.000000+0200
```

The time must be written exactly in this format, including the six digits after the seconds and the time zone offset. A value the program cannot read counts as 1 January 1970, which means everything is downloaded again. The format is the same as Instaloader's, so both programs can share the file; see [File formats](File-formats#latest-stamps-file).

Instagram lists tagged posts in the order in which the profile was tagged, not by the posts' dates. An old post in which the profile was tagged recently therefore stops a `--fast-update` or `--latest-stamps` download of tagged posts early. Download tagged posts without these options from time to time to catch everything.

Latest stamps apply to profile targets and the `--stories`, `--tagged` and `--reels` downloads of profiles. Hashtags, locations, `:feed`, `:saved` and `:stories` do not use them; use `--fast-update` or `--count` for those.

## Fast updates or latest stamps?

| | `--fast-update` | `--latest-stamps` |
|---|---|---|
| Decides by | Files in the download folder | Times stored in the stamps file |
| Downloads again after you delete files | Yes | No |
| Works if you move or reorganize downloads | No | Yes |
| Works with `--no-pictures` | No | Yes |
| Works for hashtags, locations, feed, saved posts | Yes | No |
| Re-downloads the profile picture | Only if missing (or larger, when logged in) | Only if it changed |

Use `--latest-stamps` if you process or move the downloaded files afterwards; use `--fast-update` if the download folders are your archive. Both can be given together.

## Limiting the number of posts

`--count N` (`-c N`) stops after N posts of each hashtag, location, `:feed` and `:saved` target:

```
InstaloaderSharp.exe --login YOUR-USERNAME --count 50 "#sunset" :feed
```

It does not apply to profiles; to download only recent posts of a profile, use a [filter](Filtering-posts-and-stories) such as `--post-filter "date_utc >= datetime(2026, 1, 1)"`, or `--latest-stamps` with an edited time.

## Keeping a list of profiles

Put the options and targets in a text file, one per line, and pass it with a `+` in front of its name:

```
InstaloaderSharp.exe +archive.txt
```

`archive.txt`:

```
--login=YOUR-USERNAME
--fast-update
--stories
--highlights
profile1
profile2
profile3
```

Options and targets can also be mixed: `InstaloaderSharp.exe --fast-update +profiles.txt`. See [Command-line options](Command-line-options#reading-options-from-a-file).

## Scheduled downloads

Windows' Task Scheduler can run updates automatically, for example every night. Log in interactively once first, so that a [session file](Logging-in#session-files) exists. Then create a batch file, for example `D:\Instagram\update.cmd`:

```bat
@echo off
cd /d D:\Instagram
C:\Tools\InstaloaderSharp.exe --quiet --login YOUR-USERNAME --fast-update --stories +profiles.txt >> update.log 2>&1
```

- `cd /d` makes the downloads go to `D:\Instagram`; scheduled tasks otherwise start in `C:\Windows\System32`.
- `--quiet` prints only errors and never waits for input. If the session has expired, the program exits with code 3 instead of asking for a password.
- `>> update.log 2>&1` appends all output, including errors, to a log file.

Create the task in a command prompt:

```
schtasks /create /tn "Instagram archive" /tr "D:\Instagram\update.cmd" /sc daily /st 03:00
```

or with **Task Scheduler → Create Basic Task**. Check the log and the task's **Last Run Result** from time to time: it shows the program's [exit code](Command-line-options#exit-codes), where 0 means success, 1 that some items failed, and 3 that you need to log in again.

Do not schedule several downloads with the same account at the same time, and do not run them too often: once or twice a day is enough for posts. Stories need a daily run to catch everything, since they disappear after 24 hours.

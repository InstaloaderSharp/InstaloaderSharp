Downloading a large profile can take hours. You can stop a download at any time and continue it later, and a download that Instagram breaks off can be continued too.

## Contents

- [Stopping a download](#stopping-a-download)
- [Resume files](#resume-files)
- [Continuing a download](#continuing-a-download)
- [What can be resumed](#what-can-be-resumed)
- [Options](#options)
- [Skipping a single item](#skipping-a-single-item)
- [Resuming with Instaloader](#resuming-with-instaloader)

## Stopping a download

Press **Ctrl+C**. The program does not stop immediately in the middle of writing a file: it finishes or cancels the current request, saves where it was, and exits:

```
[ 412/8123] instagram\2025-03-02_17-00-12_UTC.jpg ^C
Saved resume information to instagram\iterator_s8YiB9OL.json.xz.

Interrupted by user.
```

The exit code is 5. Files that were being downloaded at that moment are not left half-written under their final name: a download is written to a `.temp` file first and renamed when it is complete.

**Ctrl+Break** ends the program immediately, without saving resume information.

## Resume files

When a download of many posts stops early, because you pressed Ctrl+C, because of a connection error or because Instagram refused further requests, the position in the list of posts is saved in a **resume file** in the target's folder:

```
instagram\iterator_s8YiB9OL.json.xz
```

The eight characters after `iterator_` identify the list. They are computed from the query that produced it (the profile and the kind of list) and the account you are logged in with, so the posts, the tagged posts and the reels of a profile each get their own resume file, and a download can only be resumed when you are logged in with the same account as before (or not logged in both times). The file contains Instagram's position marker and the number of posts already processed, but no downloaded data.

Resume files expire after 29 days, because Instagram's position markers do not stay valid forever. An expired file is ignored (`Warning: Not resuming from ...: "Best before" date exceeded.`) and the download starts from the beginning.

## Continuing a download

Run the same command again. The program finds the resume file and continues where it stopped:

```
Resuming from instagram\iterator_s8YiB9OL.json.xz.
[ 413/8123] instagram\2025-03-01_12-30-00_UTC.jpg ...
```

When the list is complete, the resume file is deleted (`Iteration complete, deleted resume information file ...`).

The other options do not need to be the same: you can, for example, add `--no-videos` or a filter when continuing. Only the target and the kind of list must match. If you continue with `--fast-update`, keep in mind that it stops at the first post that was downloaded before, which right after resuming may come quickly.

## What can be resumed

| Download | Resumable |
|---|---|
| Posts of a profile | Yes |
| `--tagged`, `--reels` | Yes |
| `:saved` | Yes |
| Hashtags | Yes |
| `:feed`, locations | No; they start again from the newest post |
| Stories, highlights | No; they are short lists |
| Single posts, metadata files | No; there is nothing to resume |

For downloads that cannot be resumed, run the command again with `--fast-update`, which skips quickly over what was already downloaded.

## Options

| Option | Effect |
|---|---|
| `--no-resume` | Neither use existing resume files nor create new ones. Every download starts from the beginning. |
| `--resume-prefix PREFIX` | Name resume files `PREFIX_XXXXXXXX.json.xz` instead of `iterator_XXXXXXXX.json.xz`. Useful to keep separate resume files for different runs on the same folder. |

The two options cannot be combined.

## Skipping a single item

When a request fails, it is retried a few times (see [Rate limits and connections](Rate-limits-and-connections)). While this happens, the message ends with `[retrying; skip with ^C]`:

```
JSON Query to graphql/query: HTTP error code 500. [retrying; skip with ^C]
```

Pressing **Ctrl+C** at this point skips just this item (`[skipped by user]`) and continues with the next one, instead of stopping the whole program. Press Ctrl+C again outside of a retry to stop.

## Resuming with Instaloader

The resume files have the same name and contents as Instaloader's. A download that was interrupted in InstaloaderSharp can be continued with Instaloader, and the other way round, as long as both use the same folder and the same `--resume-prefix`. See [Interoperability with Instaloader](Interoperability-with-Instaloader).

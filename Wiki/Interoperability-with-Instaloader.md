InstaloaderSharp is a C# / .NET Framework version of [Instaloader](https://github.com/instaloader/instaloader). It aims to be a drop-in replacement on Windows: the same commands, the same files, the same behavior, except where Instaloader no longer works with today's Instagram. You can switch between the two programs, or use both on the same downloads.

## Contents

- [Using both programs on the same downloads](#using-both-programs-on-the-same-downloads)
- [Shared files](#shared-files)
- [The command line](#the-command-line)
- [Moving from Instaloader to InstaloaderSharp](#moving-from-instaloader-to-instaloadersharp)
- [Where the programs differ](#where-the-programs-differ)
- [Checking compatibility yourself](#checking-compatibility-yourself)

## Using both programs on the same downloads

Downloads made by one program can be updated by the other, in either order:

- Files that one program downloaded are recognized by the other (`... exists`), because both give them the same names and modification times.
- A download interrupted in one program can be [resumed](Interrupting-and-resuming) in the other.
- A `--latest-stamps` file written by one program is used by the other.
- A login saved by one program is used by the other, because both keep their session files in `%LOCALAPPDATA%\Instaloader`.

Compatibility is a goal that is tested carefully, but it is difficult to guarantee in every detail. If you find a difference, please [report it](Building-and-contributing#reporting-bugs).

## Shared files

| Data | Format | Compatibility |
|---|---|---|
| Session files (`%LOCALAPPDATA%\Instaloader\session-USERNAME`) | Python pickle | Read in all protocols; written in protocol 4, as Python writes it |
| Metadata (`*.json.xz`, `*.json`) | JSON as written by Python's `json` module, xz-compressed or pretty-printed | Should be the same, with the same key order, escaping, number formatting and line endings |
| Resume files (`iterator_MAGIC.json.xz`) | JSON, xz-compressed | Should have the same content and the same BLAKE2b "magic" file name |
| `--latest-stamps` file | INI, as written by Python's `configparser` | Should be the same |
| Pictures, videos, captions, `id` files | | Should be byte-identical, with the same file names and modification times |
| Comments (`*_comments.json`), geotags (`*_location.txt`) | JSON, text | Should be the same format, and comments are merged in the same way |

Metadata and resume files record the Instaloader version they were written with. InstaloaderSharp writes `"version": "4.15.4"`, the Instaloader version it targets, not its own version, so its files should be identical to Instaloader's. The formats are described in [File formats](File-formats).

## The command line

The command line should behave the same: the same options with the same short forms and abbreviations, the same targets, the same `+args.txt` option files, the same help output (with the program's name), the same messages and error messages, and the same [exit codes](Command-line-options#exit-codes).

To make existing scripts and scheduled tasks use InstaloaderSharp, rename `InstaloaderSharp.exe` to `instaloader.exe` and put it where the scripts find it, or change the program name in them. If you used Instaloader's `--load-cookies`, you no longer need to install `browser_cookie3`; InstaloaderSharp reads browser cookies itself.

## Moving from Instaloader to InstaloaderSharp

1. Download InstaloaderSharp (see [Getting started](Getting-started)).
2. Run your usual commands with `InstaloaderSharp.exe` instead of `instaloader`, in the same folders. Your existing session files, stamps files and downloads are used.
3. Nothing needs to be converted. If you want to go back, Instaloader can continue with everything InstaloaderSharp wrote.

You do not need Python for InstaloaderSharp. Instaloader can still be installed alongside it, for example with `pip install instaloader`.

## Where the programs differ

InstaloaderSharp works correctly where Instaloader fails because Instagram changed, or because of bugs. In those cases it deliberately differs, for example:

- `:feed`, `:stories`, followees (`@profile`) and geotags work, where Instaloader fails.
- Logged-in downloads of profiles work for accounts that Instagram redirects in Instaloader.
- Date formats with colons in file names, such as `{date_utc:%c}`, work.

The complete list, with explanations, is in [Differences from Instaloader](Differences-from-Instaloader). Files written in these cases are still in Instaloader's format, so Instaloader can read them.

InstaloaderSharp only runs on Windows, and has no Python API: it is used from the command line or as a .NET library (see [Library use](Library-use)).

## Checking compatibility yourself

To compare the two programs on your own downloads, download the same post with both into separate folders and compare the results:

```
mkdir a b
cd a
InstaloaderSharp.exe -- -DdwalmrkTSK
cd ..\b
instaloader -- -DdwalmrkTSK
cd ..
fc /b a\-DdwalmrkTSK\2026-09-26_16-01-00_UTC_1.jpg b\-DdwalmrkTSK\2026-09-26_16-01-00_UTC_1.jpg
```

File names, pictures, videos, captions and modification times should be identical. The metadata files may differ in values that Instagram changes between any two requests, such as the signed download links and the number of likes or views.

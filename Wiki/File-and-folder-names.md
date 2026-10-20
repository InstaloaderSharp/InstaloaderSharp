Folder and file names are built from **templates** in which placeholders such as `{date_utc}` or `{shortcode}` are replaced by the properties of each post. Changing the templates lets you organize downloads in your own way, for example in one folder per year.

## Contents

- [The three templates](#the-three-templates)
- [Templates](#templates)
- [Placeholders](#placeholders)
- [Format specifications](#format-specifications)
- [Subfolders](#subfolders)
- [Invalid characters](#invalid-characters)
- [Profile pictures and highlight covers](#profile-pictures-and-highlight-covers)
- [Caption file templates](#caption-file-templates)
- [Examples](#examples)
- [Things to keep in mind](#things-to-keep-in-mind)

## The three templates

| Option | Default | Used for |
|---|---|---|
| `--dirname-pattern` | `{target}` | The folder of posts, story items, profile pictures and metadata |
| `--filename-pattern` | `{date_utc}_UTC` | The file names of posts and story items, without extension and without the `_1`, `_2` numbers of sidecars |
| `--title-pattern` | `{date_utc}_UTC_{typename}` (see below) | The file names of profile pictures, hashtag pictures and highlight covers |

The file name template is relative to the folder template. Both can contain an absolute path, for example `--dirname-pattern "D:\Instagram\{target}"`.

## Templates

A template is text with placeholders in curly braces. Each placeholder is replaced by a property of the item being saved:

```
--filename-pattern "{date_utc:%Y-%m-%d}_{shortcode}"
```

gives, for a post published on 26 September 2026 with shortcode `DdwalmrkTSK`, the file names `2026-09-26_DdwalmrkTSK.jpg`, `2026-09-26_DdwalmrkTSK.txt` and so on. To put a literal brace in a name, double it: `{{` and `}}`.

The template syntax is Python's `str.format`, the same as in Instaloader, so templates written for Instaloader work unchanged.

## Placeholders

Every [property](Properties-reference) of the item can be used as a placeholder: the [post properties](Properties-reference#post) for posts, tagged posts and reels, and the [story item properties](Properties-reference#storyitem) for stories and highlights. The most useful ones:

| Placeholder | Meaning | Example |
|---|---|---|
| `{target}` | The target as given on the command line | `instagram`, `#sunset`, `:feed`, `-DdwalmrkTSK` |
| `{profile}` | The username of the post's owner | `instagram` |
| `{owner_username}`, `{owner_id}` | The owner's username and numeric ID | `instagram`, `25025320` |
| `{date_utc}` | Date and time of the post in UTC | `2026-09-26_16-01-00` |
| `{date_local}` | Date and time in your computer's time zone | `2026-09-26_18-01-00` |
| `{shortcode}` | The post's shortcode, from its URL | `DdwalmrkTSK` |
| `{mediaid}` | The post's numeric ID | `3994809802840683658` |
| `{typename}` | The kind of post | `GraphSidecar` |
| `{pcaption}` | The caption, shortened to one line of at most 30 characters plus `…` | `we said yes 🎉🎉🎉 (with help fr…` |
| `{caption}` | The whole caption; use with a format specification such as `{caption:.40}` | |
| `{likes}`, `{comments}` | Number of likes and comments | `150` |
| `{filename}` | The original file name of the picture or video on Instagram's server, without extension | `552315460_18667771684001234_n` |

`{target}` is the most important difference to `{profile}`: when you download a hashtag or your feed, `{target}` is `#sunset` or `:feed`, while `{profile}` is the username of whoever posted each post. So `--dirname-pattern "{profile}"` sorts the posts of a hashtag into one folder per account. For profile targets both are the same.

A placeholder whose property is missing (`None`) is replaced by nothing. A placeholder that is not a property at all is an error.

## Format specifications

A format specification after a colon controls how a value is written.

**Dates** use `strftime` codes. Without a specification, dates are written as `%Y-%m-%d_%H-%M-%S`.

| Code | Meaning | Example |
|---|---|---|
| `%Y`, `%y` | Year, with and without century | `2026`, `26` |
| `%m`, `%B`, `%b` | Month as a number, full name, short name | `09`, `September`, `Sep` |
| `%d` | Day of the month | `26` |
| `%H`, `%I`, `%p` | Hour (24-hour), hour (12-hour), AM or PM | `16`, `04`, `PM` |
| `%M`, `%S` | Minute, second | `01`, `00` |
| `%A`, `%a` | Weekday name, short weekday name | `Saturday`, `Sat` |
| `%j` | Day of the year | `269` |
| `%U`, `%W`, `%V` | Week number (Sunday, Monday, ISO 8601 based) | `38` |
| `%c`, `%x`, `%X` | Date and time, date, time | `Sat Sep 26 16:01:00 2026`, `09/26/26`, `16:01:00` |
| `%#c`, `%#x` | Long date and time, long date | `Saturday, September 26, 2026` |
| `%%` | A `%` sign | |

Adding `#` to a number code removes leading zeros on Windows, for example `%#d` gives `6` instead of `06`. Names of months and weekdays are always in English, as in Instaloader on Windows.

**Numbers** use Python's number formats: `{likes:06d}` gives `000150`, `{likes:,}` gives `1,500`, `{video_duration:.0f}` rounds to whole seconds.

**Text** can be cut: `{caption:.40}` keeps the first 40 characters. `{caption!r}` writes the text in quotes, as Python would show it.

## Subfolders

A `\` or `/` written in a template creates subfolders:

```
--dirname-pattern "{target}\{date_utc:%Y}"
```

saves each year's posts of a profile in a subfolder: `instagram\2025`, `instagram\2026`. A `/` or `\` that comes from a date format, such as `{date_utc:%Y/%m}`, also creates subfolders. A `/` or `\` inside a text value, such as a caption, is replaced (see below), so captions never create folders.

## Invalid characters

Windows does not allow some characters in file names. In placeholder values they are replaced with similar-looking Unicode characters:

| Character | Replaced by |
|---|---|
| `/` | `∕` (U+2215, division slash) |
| `\` | `﹨` (U+FE68) |
| `:` | `：` (U+FF1A, full-width colon) |
| `<` `>` | `﹤` `﹥` (U+FE64, U+FE65) |
| `"` | `＂` (U+FF02) |
| `\|` | `｜` (U+FF5C) |
| `?` | `︖` (U+FE16) |
| `*` | `＊` (U+FF0A) |
| Line breaks | Spaces |
| `.` at the start of a value | `․` (U+2024, one-dot leader) |

Names that Windows reserves (`CON`, `PRN`, `AUX`, `NUL`, `COM1` to `COM9`, `LPT1` to `LPT9`) get a `_` appended, and a value that ends with a `.` gets a `․` instead.

These replacements apply to the values of placeholders, not to the text you write in the template itself. Formatted values such as dates are cleaned too, except that `/` and `\` are kept so that they can create subfolders; for example `{date_utc:%H:%M}` gives `16：01`. This last point differs from Instaloader, which fails with such patterns; see [Differences from Instaloader](Differences-from-Instaloader).

`--sanitize-paths` applies these rules even on operating systems other than Windows, so that names are valid everywhere. On Windows they always apply, so the option has no effect there; it exists for compatibility with Instaloader.

## Profile pictures and highlight covers

`--title-pattern` names profile pictures, hashtag pictures and highlight covers. Their placeholders are:

| Placeholder | Meaning |
|---|---|
| `{target}` | The target |
| `{profile}`, `{owner_username}`, `{owner_id}` | The profile it belongs to |
| `{typename}` | `profile_pic` or `cover` |
| `{date_utc}`, `{date_local}` | When the picture was changed, according to Instagram's server |
| `{filename}` | The picture's file name on Instagram's server |

The default is `{date_utc}_UTC_{typename}` if `--dirname-pattern` contains `{target}` or `{profile}` (as the default `{target}` does), and `{target}_{date_utc}_UTC_{typename}` otherwise, so that profile pictures of different profiles in a shared folder do not overwrite each other.

## Caption file templates

`--post-metadata-txt` and `--storyitem-metadata-txt` use the same template syntax for the *contents* of the `.txt` files (see [Choosing what to download](Choosing-what-to-download#what-is-written-to-caption-files)). There, values are written as they are: no characters are replaced.

```
--post-metadata-txt "{caption}" --post-metadata-txt "Posted {date_local:%#x} - {likes} likes"
```

## Examples

One folder per account, with the date and shortcode in the file name:

```
InstaloaderSharp.exe --dirname-pattern "D:\Instagram\{profile}" --filename-pattern "{date_utc:%Y-%m-%d}_{shortcode}" profilename
```

One subfolder per year, with time and caption in the file name:

```
InstaloaderSharp.exe --dirname-pattern "{target}\{date_utc:%Y}" --filename-pattern "{date_utc:%m-%d_%H%M}_{pcaption}" profilename
```

Posts of a hashtag sorted by account:

```
InstaloaderSharp.exe --login YOUR-USERNAME --count 100 --dirname-pattern "#sunset\{profile}" "#sunset"
```

Everything in one folder, with the account name in front:

```
InstaloaderSharp.exe --dirname-pattern "D:\Instagram\all" --filename-pattern "{profile}_{date_utc}_UTC" profile1 profile2
```

The original file names from Instagram's server:

```
InstaloaderSharp.exe --filename-pattern "{filename}" profilename
```

## Things to keep in mind

- **Keep names unique.** Two posts that get the same file name overwrite each other's caption and metadata, and the second one's pictures are skipped as `exists`. Include `{date_utc}` with the time, `{shortcode}` or `{mediaid}`.
- **Keep the pattern when updating.** `--fast-update` and the `exists` check look for files with the names the current pattern gives. If you change the pattern, existing downloads are not recognized and everything is downloaded again under the new names.
- **Long names.** Windows paths are limited to 260 characters unless long paths are enabled in Windows; InstaloaderSharp supports long paths when they are. Prefer `{pcaption}` or `{caption:.40}` over `{caption}`.
- **Stories use the same templates.** `--filename-pattern` applies to story items too, so use placeholders that story items have as well (such as `{date_utc}`, `{shortcode}`, `{owner_username}`), or the story download fails.
- Instaloader writes the same names for the same templates, so both programs can update the same folders; see [Interoperability with Instaloader](Interoperability-with-Instaloader).

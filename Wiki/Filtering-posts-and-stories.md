Filters download only the posts or story items that match a condition, for example posts from a certain year, posts with a hashtag, or only videos. A filter is a small expression in Python syntax that is checked for every item.

## Contents

- [Using a filter](#using-a-filter)
- [Examples](#examples)
- [Variables](#variables)
- [The expression language](#the-expression-language)
- [Working with dates](#working-with-dates)
- [Working with text and lists](#working-with-text-and-lists)
- [Story filters](#story-filters)
- [Quoting](#quoting)
- [When a filter fails](#when-a-filter-fails)
- [Filters and fast updates](#filters-and-fast-updates)

## Using a filter

```
InstaloaderSharp.exe --post-filter "likes > 1000" profilename
```

`--post-filter EXPRESSION` (or its other name `--only-if`) is checked for every post, tagged post and reel. Posts for which it is false are not downloaded and are reported as skipped:

```
Only download posts with property "likes > 1000".
[  1/812] <Post DdwalmrkTSK> skipped
[  2/812] instagram\2026-10-07_18-17-32_UTC.jpg [Who IS she? …] json
```

`--storyitem-filter EXPRESSION` does the same for story items (`--stories`, `--highlights`, `:stories`).

The program checks the expression before it starts: a syntax error, or a name that is not a property, stops it with exit code 2 (`Invalid filter: ...`).

## Examples

| Filter | Downloads |
|---|---|
| `"likes > 1000"` | Posts with more than 1000 likes |
| `"not is_video"` | Only pictures (posts that are not videos) |
| `"is_video"` | Only videos |
| `"typename == 'GraphSidecar'"` | Only posts with several pictures or videos |
| `"date_utc >= datetime(2024, 1, 1)"` | Posts from 2024 and later |
| `"datetime(2024, 1, 1) <= date_utc < datetime(2025, 1, 1)"` | Posts from 2024 |
| `"date_utc.year == 2023 and date_utc.month == 12"` | Posts from December 2023 |
| `"(datetime.utcnow() - date_utc).days < 30"` | Posts from the last 30 days |
| `"'travel' in caption_hashtags"` | Posts with the hashtag `#travel` |
| `"'travel' in caption_hashtags and likes > 100"` | Posts with `#travel` and more than 100 likes |
| `"caption is not None and 'giveaway' not in caption.lower()"` | Posts whose caption does not mention "giveaway" |
| `"'friend.name' in tagged_users"` | Posts in which `friend.name` is tagged |
| `"owner_username == 'profilename'"` | Posts of a particular account, for example in `:feed` or a hashtag |
| `"is_video and video_duration < 60"` | Videos shorter than a minute |
| `"viewer_has_liked"` | Posts you liked (logged in), useful with `:feed` |
| `"not is_pinned"` | Posts that are not pinned to the profile |

Story filters:

| Filter | Downloads |
|---|---|
| `"not is_video"` | Only picture stories |
| `"date_utc >= datetime(2026, 10, 1)"` | Story items from October 2026 on, for example in highlights |

## Variables

The variables of a post filter are the [post properties](Properties-reference#post), and those of a story filter the [story item properties](Properties-reference#storyitem). The most useful ones:

| Variable | Meaning |
|---|---|
| `date_utc` | When the post was published, in UTC |
| `date_local` | The same, in your computer's time zone (with a time zone, see below) |
| `likes`, `comments` | Number of likes and comments |
| `is_video` | Whether the post is a video |
| `typename` | `'GraphImage'` (picture), `'GraphVideo'` (video) or `'GraphSidecar'` (several pictures or videos) |
| `mediacount` | Number of pictures and videos in the post |
| `caption` | The caption text, or `None` |
| `caption_hashtags` | List of hashtags in the caption, lowercase, without `#` |
| `caption_mentions` | List of usernames mentioned in the caption, lowercase, without `@` |
| `tagged_users` | List of usernames tagged in the post, lowercase |
| `owner_username`, `owner_id` | The account that posted it |
| `video_duration`, `video_view_count`, `video_play_count` | For videos: length in seconds, number of views and plays |
| `viewer_has_liked` | Whether you liked the post (when logged in) |
| `is_pinned`, `is_sponsored` | Whether the post is pinned to the profile, or a paid partnership |
| `location` | The post's location, or `None`; has `.name`, `.lat`, `.lng` and `.id` |
| `shortcode`, `mediaid` | The post's identifiers |

Some properties need an extra request to Instagram for each post, notably `location` and, for some posts, `likes`, `comments` and `video_view_count`. Filtering on them is slower. Properties that Instagram did not provide are `None`.

## The expression language

The syntax is that of a Python expression:

| Element | Examples |
|---|---|
| Numbers, strings, `True`, `False`, `None` | `1000`, `2.5`, `'text'`, `"text"` |
| Comparisons, also chained | `likes > 100`, `likes != 0`, `100 <= likes < 1000` |
| Boolean logic | `and`, `or`, `not` |
| Membership | `'cat' in caption_hashtags`, `'x' not in caption` |
| Identity | `caption is None`, `location is not None` |
| Arithmetic | `+ - * / // % **`, for example `comments * 10 > likes` |
| Conditional expression | `video_duration if is_video else 0` |
| Lists, tuples, sets, dictionaries | `owner_username in ['a', 'b']` |
| Indexing and slicing | `caption[:10]`, `caption_hashtags[0]` |
| Attributes and methods | `date_utc.year`, `caption.lower()`, `location.name` |
| f-strings | `f'{likes}'` |
| Dates | `datetime(2024, 1, 1)`, `datetime.utcnow()`, see below |

The only names available are the item's properties and `datetime`. Python functions such as `len()`, `any()` or `str()`, imports, and list comprehensions are not available: `len(caption) > 0` stops with `Invalid filter: len not a Post attribute.` Use methods of the values instead, and `caption` as a truth value (`caption and ...`) to test for an empty caption.

## Working with dates

`datetime(year, month, day)` and `datetime(year, month, day, hour, minute, second)` create a date, and `datetime.utcnow()` and `datetime.now()` the current time. Dates can be compared, and subtracting two dates gives a duration with `.days` and `.total_seconds()`:

```
--post-filter "date_utc >= datetime(2024, 6, 1)"
--post-filter "(datetime.utcnow() - date_utc).days <= 7"
```

Dates have `.year`, `.month`, `.day`, `.hour`, `.minute`, `.second`, `.weekday()` (0 is Monday, 6 is Sunday), `.isoweekday()` (1 is Monday) and `.strftime(...)`:

```
--post-filter "date_utc.weekday() >= 5"          posts from weekends (in UTC)
--post-filter "date_local.hour < 6"              posts made at night, in your time zone
--post-filter "date_utc.strftime('%m-%d') == '12-24'"
```

`date_utc` has no time zone, like `datetime(...)`, so the two can be compared directly. `date_local` includes your time zone and cannot be compared with `datetime(...)`; use `date_utc` for comparisons, or convert the date with `.astimezone()`: `date_local >= datetime(2024, 1, 1).astimezone()`.

## Working with text and lists

Captions are text (or `None` if the post has none) with Python's string methods: `.lower()`, `.upper()`, `.startswith(...)`, `.endswith(...)`, `.find(...)`, `.count(...)`, `.split()`, `.strip()`, `.replace(...)`, `.isdigit()` and more. `in` tests whether a text contains another:

```
--post-filter "caption is not None and 'recipe' in caption.lower()"
--post-filter "caption and caption.startswith('New')"
```

`caption_hashtags`, `caption_mentions` and `tagged_users` are lists of lowercase names:

```
--post-filter "'cat' in caption_hashtags or 'cats' in caption_hashtags"
--post-filter "caption_mentions == []"           posts that mention nobody
```

## Story filters

`--storyitem-filter` uses the [story item properties](Properties-reference#storyitem): `date_utc`, `date_local`, `expiring_utc`, `expiring_local`, `is_video`, `typename` (`'GraphStoryImage'` or `'GraphStoryVideo'`), `caption`, `caption_hashtags`, `caption_mentions`, `owner_username`, `owner_id`, `shortcode`, `mediaid`, `url` and `video_url`.

```
InstaloaderSharp.exe --login YOUR-USERNAME --stories --storyitem-filter "is_video" profilename
```

## Quoting

In the Windows command prompt, put the whole filter in double quotes and use single quotes for texts inside it:

```
InstaloaderSharp.exe --post-filter "'travel' in caption_hashtags" profilename
```

The form `--post-filter="..."` works too. In PowerShell, the same quoting works. In an [options file](Command-line-options#reading-options-from-a-file), write the option and the filter on one line without the outer quotes:

```
--post-filter=likes > 1000 and not is_video
```

## When a filter fails

If comparing or calculating fails for a post, for example because the filter compares a missing value (`None`) with a number, that post is skipped with an error:

```
<Post DdwalmrkTSK> skipped. Filter evaluation failed: '>' not supported between instances of 'NoneType' and 'int'
```

The download continues with the next post, and the error is repeated at the end.

Calling a method or reading an attribute of a missing value, such as `caption.lower()` for a post without a caption, is not caught in this way and stops the program with an error (as in Instaloader). Always guard properties that can be missing with `is not None`:

```
--post-filter "caption is not None and 'recipe' in caption.lower()"
--post-filter "video_duration is not None and video_duration > 60"
```

## Filters and fast updates

Skipped posts do not stop a `--fast-update`: the program goes on to the next post, and a fast update stops only at a post that passes the filter and was downloaded before. With a filter that skips most posts, a fast update therefore still goes through many posts. `--latest-stamps` stops at the first post older than the last download, whether it passes the filter or not. See [Keeping an archive up to date](Keeping-an-archive-up-to-date).

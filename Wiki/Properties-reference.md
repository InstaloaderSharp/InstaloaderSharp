This page lists the properties of the items InstaloaderSharp downloads. They are the variables of [filters](Filtering-posts-and-stories) and the placeholders of [name templates](File-and-folder-names) and [caption file templates](Choosing-what-to-download#what-is-written-to-caption-files). The names are the same as in Instaloader. In [library use](Library-use) they are C# properties with PascalCase names (`date_utc` is `DateUtc`).

## Contents

- [Using properties](#using-properties)
- [Post](#post)
- [StoryItem](#storyitem)
- [Profile](#profile)
- [PostLocation](#postlocation)
- [Hashtag](#hashtag)
- [Highlight](#highlight)
- [Title pictures](#title-pictures)
- [Values and types](#values-and-types)

## Using properties

In filters and templates, a property is written by its name: `likes`, `{likes}`. Properties of properties are reached with a dot, and items of lists with square brackets:

```
--post-filter "owner_profile.followers > 10000"
--filename-pattern "{date_utc}_{owner_profile.username}_{caption_hashtags[0]}"
--post-filter "location is not None and location.name == 'Uppsala, Sweden'"
```

Some properties are not part of the list of posts that Instagram sends, and are fetched with an extra request for each post when they are first used. This is noted in the tables below. Filtering on them, or using them in names, makes downloads slower and uses up more of the [request limits](Rate-limits-and-connections).

## Post

Posts, tagged posts, reels, and the posts of hashtags, locations, the feed and saved posts.

| Property | Type | Meaning |
|---|---|---|
| `shortcode` | text | The post's ID in its URL: `https://www.instagram.com/p/SHORTCODE/` |
| `mediaid` | number | The post's numeric ID |
| `typename` | text | `'GraphImage'` (one picture), `'GraphVideo'` (one video) or `'GraphSidecar'` (several pictures or videos) |
| `mediacount` | number | The number of pictures and videos in the post; 1 unless it is a sidecar |
| `is_video` | true/false | Whether the post is a video; false for sidecars, even if they contain videos |
| `date_utc` | date | When the post was published, in UTC, without time zone |
| `date_local` | date | When the post was published, in your computer's time zone, with time zone |
| `date` | date | The same as `date_utc` (kept for compatibility) |
| `owner_username` | text | Username of the account that posted it, lowercase |
| `owner_id` | number | Numeric ID of that account |
| `owner_profile` | [Profile](#profile) | That account; its properties may need an extra request |
| `profile` | text | The same as `owner_username`; used as `{profile}` in templates |
| `caption` | text or `None` | The caption |
| `pcaption` | text | The caption shortened to one line of at most 30 characters plus `…`, for file names |
| `caption_hashtags` | list of texts | The hashtags in the caption, lowercase, without `#` |
| `caption_mentions` | list of texts | The usernames mentioned in the caption, lowercase, without `@` |
| `tagged_users` | list of texts | The usernames tagged in the post, lowercase |
| `accessibility_caption` | text or `None` | Instagram's automatic description of the picture, for screen readers |
| `title` | text or `None` | The title of a video, if it has one |
| `likes` | number | The number of likes |
| `comments` | number | The number of comments |
| `viewer_has_liked` | true/false/`None` | Whether you liked the post; only when logged in |
| `video_url` | text or `None` | The URL of the video |
| `video_duration` | number or `None` | The length of the video in seconds |
| `video_view_count` | number or `None` | The number of views of the video |
| `video_play_count` | number or `None` | The number of plays of a reel |
| `url` | text | The URL of the (first) picture |
| `location` | [PostLocation](#postlocation) or `None` | Where the post was tagged; needs an extra request for most posts |
| `is_pinned` | true/false | Whether the post is pinned to the top of the profile |
| `is_sponsored` | true/false | Whether the post is a paid partnership |
| `sponsor_users` | list of profiles | The partners of a paid partnership |

## StoryItem

Story items of `--stories`, `--highlights` and `:stories`.

| Property | Type | Meaning |
|---|---|---|
| `shortcode` | text | The story item's shortcode |
| `mediaid` | number | The story item's numeric ID |
| `typename` | text | `'GraphStoryImage'` or `'GraphStoryVideo'` |
| `is_video` | true/false | Whether it is a video |
| `date_utc`, `date_local`, `date` | date | When it was posted, as for posts |
| `expiring_utc`, `expiring_local` | date | When it expires (24 hours after posting), in UTC and your time zone |
| `owner_username`, `owner_id`, `owner_profile`, `profile` | | The account that posted it, as for posts |
| `caption` | text or `None` | Text of the story item, if any |
| `pcaption`, `caption_hashtags`, `caption_mentions` | | As for posts |
| `url` | text | The URL of the picture (or of the video's thumbnail) |
| `video_url` | text or `None` | The URL of the video |

## Profile

Reached through `owner_profile` of a post or story item. Most properties need an extra request the first time they are used for a profile.

| Property | Type | Meaning |
|---|---|---|
| `username` | text | The username, lowercase |
| `userid` | number | The numeric ID, which never changes |
| `full_name` | text | The display name |
| `biography` | text | The profile's description |
| `biography_hashtags`, `biography_mentions` | list of texts | Hashtags and usernames in the description |
| `external_url` | text or `None` | The link in the profile |
| `is_private` | true/false | Whether the profile is private |
| `is_verified` | true/false | Whether the profile has a verified badge |
| `is_business_account` | true/false | Whether it is a business account |
| `business_category_name` | text or `None` | The business category |
| `mediacount` | number or `None` | The number of posts |
| `followers` | number or `None` | The number of followers |
| `followees` | number or `None` | The number of accounts it follows |
| `followed_by_viewer`, `follows_viewer` | true/false | Whether you follow it, and whether it follows you (logged in) |
| `blocked_by_viewer`, `has_blocked_viewer` | true/false | Whether you blocked it, and whether it blocked you |
| `requested_by_viewer`, `has_requested_viewer` | true/false | Pending follow requests |
| `has_public_story`, `has_viewable_story` | true/false | Whether it has a story you can see |
| `has_highlight_reels` | true/false | Whether it has highlights |
| `profile_pic_url` | text | The URL of the profile picture, in the best available quality |

## PostLocation

Reached through `location` of a post.

| Property | Type | Meaning |
|---|---|---|
| `id` | number | The location's ID, usable as `%id` [target](Targets#locations) |
| `name` | text | The location's name |
| `lat`, `lng` | number or `None` | Latitude and longitude |
| `slug` | text or `None` | A URL-friendly name; Instagram no longer provides it, so it is usually `None` |
| `has_public_page` | true/false/`None` | Whether the location has a public page; usually `None` |

## Hashtag

The hashtag of a `"#hashtag"` target, as saved in its metadata.

| Property | Type | Meaning |
|---|---|---|
| `name` | text | The hashtag, lowercase, without `#` |
| `hashtagid` | number | Its numeric ID |
| `mediacount` | number or `None` | The number of posts with this hashtag |
| `description` | text or `None` | Its description |
| `profile_pic_url` | text | The URL of its picture |

## Highlight

The highlights downloaded with `--highlights`. Their story items have the [StoryItem](#storyitem) properties; the highlight itself is used for the folder name (its title) and the cover picture.

| Property | Type | Meaning |
|---|---|---|
| `title` | text | The highlight's title, used as folder name |
| `unique_id` | number | Its numeric ID |
| `itemcount` | number | The number of story items in it |
| `owner_username`, `owner_id`, `owner_profile` | | The profile it belongs to |
| `latest_media_utc`, `latest_media_local` | date | When its newest item was added |
| `cover_url`, `cover_cropped_url` | text | The URLs of its cover picture |

## Title pictures

Profile pictures, hashtag pictures and highlight covers are named with `--title-pattern`; their placeholders are listed in [File and folder names](File-and-folder-names#profile-pictures-and-highlight-covers).

## Values and types

| Type | In filters | In templates |
|---|---|---|
| Text | `'text'`, string methods such as `.lower()` | Inserted as it is (with [invalid characters](File-and-folder-names#invalid-characters) replaced in names); `{caption:.40}` cuts it |
| Number | Arithmetic and comparisons | `{likes:06d}`, `{video_duration:.1f}` |
| true/false | Used directly: `is_video`, `not is_video` | `True` or `False` |
| Date | Compare with `datetime(...)`, `.year` and other attributes | Formatted with `strftime` codes, default `%Y-%m-%d_%H-%M-%S` |
| List | `'x' in caption_hashtags`, `caption_hashtags[0]` | Written like Python: `['cat', 'dogs']` |
| `None` | `is None`, `is not None` | Nothing |

A property that is missing for an item (for example `video_duration` of a picture) is `None`.

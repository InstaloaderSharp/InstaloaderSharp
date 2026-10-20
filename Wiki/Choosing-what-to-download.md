By default, InstaloaderSharp downloads everything that belongs to a post (pictures, videos, video thumbnails, the caption and the metadata) and, for a profile, its profile picture and all its posts. The options on this page leave things out or add more.

## Contents

- [What is downloaded of each post](#what-is-downloaded-of-each-post)
- [Comments and geotags](#comments-and-geotags)
- [Parts of posts with several pictures](#parts-of-posts-with-several-pictures)
- [What is written to caption files](#what-is-written-to-caption-files)
- [What is downloaded of each profile](#what-is-downloaded-of-each-profile)
- [Stories, highlights, tagged posts and reels](#stories-highlights-tagged-posts-and-reels)
- [Examples](#examples)
- [Combinations that are not allowed](#combinations-that-are-not-allowed)

## What is downloaded of each post

| Option | Effect |
|---|---|
| `--no-pictures` | Do not download pictures. This also leaves out video thumbnails, but not videos. |
| `--no-videos` (`-V`) | Do not download videos. Their thumbnail pictures are still downloaded. |
| `--no-video-thumbnails` | Do not download the thumbnail pictures of videos. |
| `--no-captions` | Do not create caption `.txt` files. |
| `--no-metadata-json` | Do not create `.json.xz` metadata files. |
| `--no-compress-json` | Create plain, pretty-printed `.json` files instead of compressed `.json.xz` files. |

These options apply to posts of every kind of target, and to story items. For example, `--no-videos --no-video-thumbnails` downloads only pictures, and `--no-pictures --no-videos` downloads only captions and metadata, which is a quick way to get an overview of a profile.

## Comments and geotags

| Option | Effect |
|---|---|
| `--comments` (`-C`) | Also download the comments of each post, into `..._comments.json`. Existing files are updated. Requires login. |
| `--geotags` (`-G`) | Also save the location of each post that has one, into `..._location.txt`, with a Google Maps link. |

Both cost one or more extra requests to Instagram per post, which makes downloads slower and uses up more of the [request limits](Rate-limits-and-connections). That is why they are off by default. The files are described in [Downloaded files](Downloaded-files#comments).

## Parts of posts with several pictures

`--slide` downloads only some of the pictures and videos of posts that have several (sidecars). Posts with a single picture or video are not affected.

| Value | Downloads |
|---|---|
| `--slide 1` | Only the first one |
| `--slide 3` | Only the third one |
| `--slide last` | Only the last one |
| `--slide 2-4` | The second to the fourth |
| `--slide 2-last` | From the second to the last |

Numbers start at 1. In a range, the first number must be smaller than the second. The numbered file names stay the same as without `--slide`, so `--slide 2` saves `..._2.jpg`.

## What is written to caption files

The caption file of a post contains the post's caption by default. `--post-metadata-txt TEMPLATE` writes something else instead. The template can contain any [property](Properties-reference#post) of the post in curly braces, as in [file name templates](File-and-folder-names#templates). Give the option several times to write several lines:

```
InstaloaderSharp.exe --post-metadata-txt "{caption}" --post-metadata-txt "{likes} likes, {comments} comments" --post-metadata-txt "https://www.instagram.com/p/{shortcode}/" profilename
```

`--storyitem-metadata-txt TEMPLATE` does the same for story items, using the [story item properties](Properties-reference#storyitem). Story items get no text file by default.

If the template gives an empty text for a post (for example `{caption}` for a post without a caption), no file is written. `--no-captions` cannot be combined with these options.

## What is downloaded of each profile

| Option | Effect |
|---|---|
| `--no-posts` | Do not download the profile's posts. The profile picture, the profile's metadata and anything requested with the options below are still downloaded. |
| `--no-profile-pic` | Do not download the profile picture. |

For hashtag targets, `--no-posts` and `--no-profile-pic` work in the same way, for the hashtag's posts and picture.

## Stories, highlights, tagged posts and reels

| Option | Effect | Login |
|---|---|---|
| `--stories` (`-s`) | Also download the profile's current stories, into the profile's folder. | Required |
| `--highlights` | Also download the profile's story highlights, each into a subfolder named after the highlight, with its cover picture. | Required |
| `--tagged` | Also download the posts in which the profile is tagged, into the subfolder `：tagged`. | Recommended |
| `--reels` | Also download the profile's reels, into the profile's folder. | Recommended |
| `--igtv` | No effect: Instagram has retired IGTV. Former IGTV videos are downloaded with the posts and with `--reels`. The option is accepted so that existing commands keep working. | |

"Recommended" means that the program does not require a login, but Instagram may answer these requests only for logged-in users.

The stories of all profiles on the command line are downloaded together, after their posts. Stories are only available for 24 hours, so download them regularly if you want to keep them; highlights stay until the owner removes them.

`--storyitem-filter` selects which story items are downloaded, and `--post-filter` which posts, tagged posts and reels; see [Filtering posts and stories](Filtering-posts-and-stories).

## Examples

Only stories and highlights of two profiles, no posts:

```
InstaloaderSharp.exe --login YOUR-USERNAME --no-posts --stories --highlights profile1 profile2
```

Everything of a profile, including comments and geotags:

```
InstaloaderSharp.exe --login YOUR-USERNAME --stories --highlights --tagged --reels --comments --geotags profilename
```

Only the pictures, without videos, captions or metadata:

```
InstaloaderSharp.exe --no-videos --no-video-thumbnails --no-captions --no-metadata-json profilename
```

Only the first picture of each post, with readable metadata:

```
InstaloaderSharp.exe --slide 1 --no-compress-json profilename
```

Only the metadata, to have a look at a profile before downloading it:

```
InstaloaderSharp.exe --no-pictures --no-videos --no-captions --no-compress-json profilename
```

## Combinations that are not allowed

| Combination | Why |
|---|---|
| `--no-pictures` with `--fast-update` | `--fast-update` decides that a post was downloaded before by its picture file, which `--no-pictures` does not create. Use `--latest-stamps` instead. |
| `--no-captions` with `--post-metadata-txt` or `--storyitem-metadata-txt` | They contradict each other. |

The program stops with exit code 2 and an error message in these cases.

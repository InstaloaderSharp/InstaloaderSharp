"""Generates the reference files in this directory with Python and Instaloader 4.15.4.

The unit tests check that InstaloaderSharp reads these files and writes identical ones. Regenerate them with

    python InstaloaderSharp.Tests/TestData/generate.py

from the repository root (Python 3.8 or later, with Instaloader 4.15.4 installed: pip install instaloader==4.15.4).
If an Instaloader source tree exists in Old/ next to the solution, it is used instead. The script prints values
that are hard-coded in the tests.
"""

import json
import lzma
import os
import pickle
import sqlite3
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
OLD = os.path.join(HERE, '..', '..', 'Old')
if os.path.isdir(os.path.join(OLD, 'instaloader')):
    sys.path.insert(0, OLD)

from instaloader import InstaloaderContext, LatestStamps, Post, save_structure_to_file  # noqa: E402
from instaloader.nodeiterator import NodeIterator  # noqa: E402

SESSION = {'sessionid': '12345%3AabcDEF%3A9', 'mid': '', 'ig_pr': '1', 'csrftoken': 'tokenTOKEN',
           'ds_user_id': '12345', 'rur': '"NHA\\05412345"', 'unicode': 'café ☕'}

POST_NODE = {
    'shortcode': 'CqXy_12-Ab', 'id': '3082211244414448123', '__typename': 'GraphSidecar', 'is_video': False,
    'date': 1609502400,
    'caption': 'Hello World! #Cat #dogs_2 été @some.user and @other__x/slash\nsecond line \U0001F600',
    'edge_media_preview_like': {'count': 150}, 'owner': {'id': '25025320', 'username': 'SomeUser'},
    'display_url': 'https://scontent.cdninstagram.com/v/t51/abc_n.jpg?stp=1&se=7&x=2', 'title': None,
    'edge_media_to_comment': {'count': 7}, 'accessibility_caption': 'A cat', 'video_duration': 12.5,
    'ratio': 0.1, 'big': 12345678901234567890, 'neg': -0.0,
    'edge_media_to_tagged_user': {'edges': [{'node': {'user': {'username': 'Tagged.One'}}}]},
    'edge_sidecar_to_children': {'edges': [
        {'node': {'is_video': False, 'display_url': 'https://example.com/1.jpg'}},
        {'node': {'is_video': True, 'display_url': 'https://example.com/2.jpg',
                  'video_url': 'https://example.com/2.mp4'}}]},
}


def path(name):
    return os.path.join(HERE, name)


def main():
    # Session files as written by different Python versions (pickle protocols)
    for protocol in (0, 2, 4, 5):
        with open(path('session_protocol%d.pickle' % protocol), 'wb') as f:
            pickle.dump(SESSION, f, protocol=protocol)

    # .xz files: Instaloader's own setting (no check), the lzma default (CRC64), CRC32, SHA256,
    # two concatenated streams, and the legacy .lzma format
    text = json.dumps({'node': POST_NODE, 'list': list(range(2000))}, separators=(',', ':'))
    with open(path('text.json'), 'w', newline='') as f:
        f.write(text)
    for name, check in (('nocheck', lzma.CHECK_NONE), ('crc32', lzma.CHECK_CRC32),
                        ('crc64', lzma.CHECK_CRC64), ('sha256', lzma.CHECK_SHA256)):
        with lzma.open(path('text_%s.json.xz' % name), 'wt', check=check) as f:
            f.write(text)
    with open(path('text_concatenated.xz'), 'wb') as f:
        half = len(text) // 2
        f.write(lzma.compress(text[:half].encode()) + lzma.compress(text[half:].encode()))
    with open(path('text_legacy.lzma'), 'wb') as f:
        f.write(lzma.compress(text.encode(), format=lzma.FORMAT_ALONE))

    # A Post saved by Instaloader, pretty-printed and compressed
    context = InstaloaderContext()
    post = Post(context, json.loads(json.dumps(POST_NODE)))
    save_structure_to_file(post, path('post.json'))
    save_structure_to_file(post, path('post.json.xz'))

    # A resume file of an iteration that was interrupted after two items
    iterator = NodeIterator(
        context, None, lambda d: d, lambda n: n,
        {'data': {'count': 12, 'include': True}, 'username': 'éx'}, 'https://www.instagram.com/abc/',
        first_data={'edges': [{'node': {'id': i, 'f': 1.5}} for i in range(5)],
                    'page_info': {'has_next_page': True, 'end_cursor': 'CUR'}},
        doc_id='999')
    next(iterator)
    next(iterator)
    save_structure_to_file(iterator.freeze(), path('resume.json.xz'))
    print('resume magic:', iterator.magic)

    # A latest-stamps file
    if os.path.exists(path('latest-stamps.ini')):
        os.remove(path('latest-stamps.ini'))
    stamps = LatestStamps(path('latest-stamps.ini'))
    stamps.save_profile_id('someone', 25025320)
    stamps.set_profile_pic('someone', 'pic_n.jpg')
    from datetime import datetime, timedelta, timezone
    stamps.set_last_post_timestamp(
        'someone', datetime(2024, 5, 6, 7, 8, 9, 123456, tzinfo=timezone(timedelta(hours=2))))

    # Shortcodes and media IDs
    for shortcode in ('CqXy_12-Ab', 'DdwalmrkTSK', 'B'):
        print('mediaid', shortcode, Post.shortcode_to_mediaid(shortcode))

    # A Firefox-like cookie database with changes in an uncheckpointed write-ahead log. The database is
    # written by a child process that exits without closing it, so that the -wal file is kept.
    for suffix in ('', '-wal', '-shm'):
        if os.path.exists(path('cookies.sqlite' + suffix)):
            os.remove(path('cookies.sqlite' + suffix))
    subprocess.check_call([sys.executable, '-c', WRITE_COOKIES, path('cookies.sqlite')])
    os.remove(path('cookies.sqlite-shm'))


WRITE_COOKIES = r'''
import os, sqlite3, sys
c = sqlite3.connect(sys.argv[1])
c.execute('PRAGMA page_size=1024')
c.execute('PRAGMA journal_mode=WAL')
c.execute('PRAGMA wal_autocheckpoint=0')
c.execute("CREATE TABLE moz_cookies (id INTEGER PRIMARY KEY, originAttributes TEXT NOT NULL DEFAULT '', "
          "name TEXT, value TEXT, host TEXT, path TEXT, expiry INTEGER, isSecure INTEGER, "
          "CONSTRAINT moz_uniqueid UNIQUE (name, host, path, originAttributes))")
for i in range(300):
    c.execute('INSERT INTO moz_cookies (name, value, host, path, expiry, isSecure) VALUES (?, ?, ?, ?, ?, ?)',
              ('n%d' % i, 'v' * (i % 50), '.example%d.com' % (i % 7), '/', 1700000000 + i, i % 2))
c.execute('INSERT INTO moz_cookies (name, value, host, path) VALUES (?, ?, ?, ?)',
          ('sessionid', 'S' * 3000 + 'END', '.instagram.com', '/'))
c.commit()
c.execute('PRAGMA wal_checkpoint(TRUNCATE)')
c.execute('INSERT INTO moz_cookies (name, value, host, path) VALUES (?, ?, ?, ?)',
          ('csrftoken', 'from-wal', '.instagram.com', '/'))
c.execute("UPDATE moz_cookies SET value = 'updated' WHERE name = 'n5'")
c.commit()
os._exit(0)
'''

if __name__ == '__main__':
    main()

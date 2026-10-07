# Arirang / Maseco music-source investigation

Checked 2026-10-07. This investigation is separate from the packaged Windows
player. No Arirang song playback or working cloud video service is claimed.

## Internet Archive disc catalogue

The user's [Maseco search](https://archive.org/search?query=creator%3A%22Maseco%22)
returned 27 items through `advancedsearch.php`. The
[official metadata API](https://archive.org/developers/md-read.html) gives each
item's file list as JSON at `/metadata/{identifier}`. File URLs are formed from
the item identifier and URL-escaped filename under `/download/`.

`tools/arirang_volume_catalogue.py` implements paginated discovery, bounded
metadata requests and a volume/file catalogue. Its default output and caches
are under ignored `.reference/arirang-research`. It fetches no media. Private
files and restricted items have no download URL in the generated catalogue;
disc files, song-data containers and volume videos remain distinct. Every entry
explicitly records that individual song playback is unverified. Offline
normalization of all 27 saved item records completed without errors. Three
unit checks cover these distinctions, restricted/private files and URL paths.

Seven disc-image entries were found:

| Volume | Item identifier |
| --- | --- |
| 40 | `midi-vision-karaoke-vol-40` |
| 45 | `20250509_20250509_0803` |
| 48 | `maseco_vol.48` |
| 52 | `maseco_vol52_202508` |
| 56 | `midi-vision-karaoke-vol-56` |
| 56 | `midivisionkaraoke56` |
| 58 | `undefined_202311` |

These are entries named as disc images, not seven verified usable ISO files.
For example, a 64 KiB HTTP range read of `midivisionkaraoke56/Ariang56A.iso`
begins with the RAR5 signature, despite its ISO filename. Filename/format
metadata must not be used as proof that an ISO reader can open the file.

[Volume 48](https://archive.org/details/maseco_vol.48) additionally exposes
extracted `VIDEO_TS` files, `hk2/info.dat` (1,320,466 bytes), font files, and
`midi0/ARVNKR.d00`, `.d01`, `.d02` data containers. Its index and small container
header samples were downloaded privately. That structure is useful for song
index/decoder research; it does not establish an individual MP4 per song.
Many other items' video filenames explicitly describe background reels. Such
videos must not populate the selectable song library as if they contain music
and synchronized lyrics. The user's quoted forum reply also reports this issue.

To regenerate the catalogue:

```powershell
python tools/arirang_volume_catalogue.py
python tools/arirang_volume_catalogue.py --offline
python tools/test_arirang_volume_catalogue.py
```

## MediaFire volume 1–39 lead

The public folder API for the user's
[folder](https://www.mediafire.com/?b0788txnibb4g) reports the name
`Karaoke Arirang vol 1-39`, no subfolders, and 20 files named
`DVD Karaoke Arirang vol1-39.iso.001` through `.020`. This is one split ISO
set, not a listing of 39 separate ISOs.

The API reports `.010` as zero bytes, with the SHA-256 hash of an empty file.
Parts `.001`, `.005`, `.009`, `.013`, `.017` and `.020` are password-protected.
The listed bytes total 3,031,898,522. No parts were downloaded, no passwords
were guessed, and no completeness claim was made. The set cannot reconstruct
the intended complete disc as currently listed. Joining parts would also not
solve the absence of MIDI/music data reported in the quoted forum reply.

## Official AR-3600WTK APK lead

The [official Arirang download page](https://arirang.com.vn/huong-dan-su-dung-va-tai-ve-ung-dung-danh-rieng-cho-ar-3600wtk/)
links `3600WTK_Karaoke_1_8_2.apk`. The 11,994,863-byte APK was downloaded
privately and inspected without execution. Its resource constants and callers
contain these historical download addresses:

| Address | Observed use / current public check |
| --- | --- |
| `http://maseco.com.vn/download/3600WTK/` | Base for `YoutubeList.dat` and `Interaction.zip`; base GET returned 404 |
| `http://download.cdn.bss.com.vn/` | Song-store caller constructs `SINGMATE/SongStore/{allsongid}.dat`; base connection failed |
| `http://124.158.13.202:8036/3600.php` | App has a form POST caller; bounded public GET connection timed out |

The base checks do not prove that every individual file is deleted. No usable
media response was obtained. Embedded application credentials were neither
used nor included in public source. No VietK device identity was sent to an
Arirang host. The app also contains YouTube/other online-source code, so finding
an HTTP endpoint alone is not evidence of an independent Arirang video library.

The next substantive requirement is a verified mapping from a song number to
the disc's music and lyric data, followed by a working decoder/synthesizer or
an actual individually playable media file. Volume downloads alone do not meet
that requirement.

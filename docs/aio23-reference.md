# Operator-owned VietKTV AIO 23 reference

The operator identifies the photographed device as VietKTV AIO 23. Its About
screen shows application 1.13.86 (versionCode 1001929), firmware
version_20220304, and Android 8.1. The editable device name is not a model ID.
Hardware identifiers are private and are excluded from this document.

The supplied KTV-Plus ZIP is still the source of the current Windows port.
It has not been verified as the AIO 23 firmware. The older Plus server accepting
the photographed identifiers at login, then returning `sn not in devices_table`
for cloud status and no media URL, does not establish the AIO's entitlement.
Its displayed identifier may differ from the hardware serial used by its app.

The [VietKTV official app announcement](https://vietktv.vn/can-canh-iphone-x-plus-sap-ra-mat-co-3-camera-sau-dep-khong-the-kim-long/)
lists HD, HDPlus and KPlus device families and companion packages
`com.vietktv.connect` and `com.vietktv.connectplus`. These are companion apps;
they are not established as the AIO's installed karaoke player.
The [official software-update page](https://vietktv.vn/cach-khac-phuc-loi-iphone-bi-nong-vang-ung-dung-va-tu-dong-khoa-man-hinh/)
lists older HD, HD Plus and HD Pro updates, with no verified AIO 23 package.

An APK or firmware from this actual device is required to establish its player
package, hardware-ID derivation, backend hosts, download protocol and original
panel/TV behavior. Do not assume the Plus backend applies or transmit private
identifiers to a newly discovered host without authorization for that host.

The first yt-dlp Windows build succeeded, but its public Blender video probe
failed with YouTube's sign-in/bot-check response. This is not successful live
music playback. Public search worked separately on the operator's PC. The app
supports an explicitly selected local cookie file; no browser credentials have
been read for testing.

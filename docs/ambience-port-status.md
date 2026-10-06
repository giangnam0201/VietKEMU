# Original ambience port

The Windows `ambience_imv` command now opens the expression page. Original
supplemental pictures and sounds are imported locally, without uploading the
owner's recovered media. The page requires that local supplement.

Implemented from the decoded firmware:

- `AmbienceDialog`: 780 by 450 dialog, horizontal offset -95, close button and
  outside dismissal, purple background and selected-tab gradient.
- `SendExpressionView`: two rows in four columns, 176-wide columns, 120-square
  images, 150-high items, original Vietnamese labels.
- `AnimCommonUtils.scaleAnim`: scale 1 to 1.15 over 500 ms, no repeat, reset at end.
- `KmOSDMessageView.showTftpPic` and `km_msg_osdtv`: centered 318-square image,
  six-pixel frame padding, top margin 65, local default avatar when available.
- `KmAsyPlayer`: independent looping WAV playback at half volume, stopped with
  the picture after the 6000 ms expression timeout. Sending another expression
  replaces the previous picture and sound, resetting that timeout.

The TV and panel use the same overlay. Expressions do not replace the song
source or alter the selected queue.

Windows CI uses a synthetic red image and a one-second 1600 Hz WAV to verify
preview pixels, sound looping, timeout and concurrent karaoke decoding. These
fixtures verify the native path; they do not prove visual equivalence of every
factory asset or audio-device mixing on a physical Windows PC.

Still pending: wishes/barrage tab and original scheduling, TV-mask tab,
remembered tab selection, peripheral lighting, and complete original dialog
navigation. The expression page is a partial port, not proof of full fidelity.

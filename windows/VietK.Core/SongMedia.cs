namespace VietK.Core;

// MediaDAO.getMedia's 17 fields. VolumeUuid identifies storage; an empty UUID
// from the online import does not establish a playable file.
public sealed record SongMedia(int Id, int SongId, string FileName, int DefaultVolume,
    int OriginalTrack, int AccompanyTrack, string MediaType, string SongNameWordType,
    int VolumeBalance, string VolumeQuality, string ImageQuality, string SongVersion,
    string TotalQuality, int Price, string? Md5, string? UpdateDateTime, string? VolumeUuid);

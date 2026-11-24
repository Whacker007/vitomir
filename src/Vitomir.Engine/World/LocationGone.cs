// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Engine.World;

public sealed partial class Location
{
    /// <summary>
    /// The placed things a load took out of the world before the scripts were made — killed and
    /// taken away, picked up, broken — by their scene index, with the GUID each had. A script whose
    /// parameters name one still gets a wrapper around that GUID, not null.
    ///
    /// **1.1's, from the file.** A thing parameter is kept in a script's saved record as the GUID it
    /// was converted to when the scene first loaded (the codex record's twelve-byte values, read
    /// back and handed to the constructor again), and the record keeps the
    /// GUID after the thing has gone: A monastery save (`SaveGames\SP4`) has `MON1_Main` naming its
    /// two Cappadocians by GUIDs 2759 and 2757 when 2759 is no longer in the place. So the
    /// constructor that calls <c>Capp1.GetGUID()</c> gets 2759 back rather than dying on a null,
    /// and the monastery's main script — every one of its region conversations — survives a load
    /// made after that Cappadocian's death. Vitomir's GUIDs are its own, so the one kept here is the
    /// GUID the thing had in this build; no thing answers to it, just as none answers to 2759.
    /// </summary>
    public Dictionary<int, int> Gone { get; } = [];
}

// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Formats;

/// <summary>
/// **What each shipped script's own <c>save(int)</c> writes**, so a reader can step over it. The words
/// are untyped (the save hands the class the file): <c>SaveInt</c>, <c>SaveBoolean</c> and
/// <c>SaveFloat</c> write four bytes each, <c>SaveString</c> a length
/// word and the characters without their terminator. A layout here is one letter a call —
/// <c>w</c> a word, <c>s</c> a string, <c>o</c> a boolean written only under a condition — in the
/// order the class's <c>save</c> makes them.
/// </summary>
/// <remarks>
/// Counted from the 91 shipped classes that declare <c>save</c>. Two write
/// their word only under a condition set by their own parameters — <c>RegionTriggerSound</c> when it
/// plays once, <c>TrapFloorPlateZap</c> when it does not repeat — and both write a boolean, so their
/// layout is <c>o</c>: one word unless the next word is already the record's closing tag (0x1d), which
/// a boolean never is (for the location files, where regions and traps live). None of the chronicle
/// classes declares <c>save</c>; the ones that turn up in <c>!main.nsg</c> are the discipline effects
/// on the party — the first 1.1 save at hand with a discipline running (Celerity) was the
/// first the reader could not finish.
/// </remarks>
public static class NsgScriptLayouts
{
    /// <summary>Class name → layout, as the save names the class (case as shipped).</summary>
    public static readonly IReadOnlyDictionary<string, string> Known = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ActorAIOff_TriggerOn"] = "w",
        ["ARC3_Gargoyle"] = "w",
        ["CARG_32_1"] = "w",
        ["ClassSpiderSac"] = "w",
        ["ClickExit"] = "wss",
        ["ClickExitArdan"] = "ws",
        ["DEVN_Exit"] = "wss",
        ["DOCK_33_1_Main"] = "ww",
        ["Door"] = "w",
        ["DoorElevator"] = "w",
        ["DoorPivotSwitchPivot"] = "w",
        ["DoorRegion"] = "w",
        ["DoorSwitch"] = "w",
        ["DoorSwitchPivot"] = "w",
        ["DoubleDoor"] = "w",
        ["DoubleDoorPivotSwitchPivot"] = "w",
        ["DoublePivotDoor"] = "w",
        ["EAST_30_1_Main"] = "w",
        ["EffectDiscAtrophy"] = "w",
        ["EffectDiscAwe"] = "ww",
        ["EffectDiscBloodOfPotency"] = "w",
        ["EffectDiscCauldronOfBlood"] = "w",
        ["EffectDiscCelerity"] = "w",
        ["EffectDiscCommand"] = "w",
        ["EffectDiscDecay"] = "w",
        ["EffectDiscDreadGaze"] = "w",
        ["EffectDiscEarthMeld"] = "ww",
        ["EffectDiscEntrancement"] = "w",
        ["EffectDiscEyesOfTheBeast"] = "w",
        ["EffectDiscEyesOfTheSerpent"] = "w",
        ["EffectDiscFeralWhispers"] = "ww",
        ["EffectDiscFortitude"] = "ww",
        ["EffectDiscHeartOfStone"] = "w",
        ["EffectDiscHeightenedSenses"] = "w",
        ["EffectDiscIgnoreFlame"] = "w",
        ["EffectDiscMajesty"] = "ww",
        ["EffectDiscMask1000Faces"] = "ww",
        ["EffectDiscMesmerize"] = "w",
        ["EffectDiscMistForm"] = "w",
        ["EffectDiscPassion"] = "w",
        ["EffectDiscPossession"] = "w",
        ["EffectDiscPotence"] = "w",
        ["EffectDiscPrisonOfIce"] = "ww",
        ["EffectDiscShapeOfTheBeast"] = "w",
        ["EffectDiscSkinOfTheAdder"] = "ww",
        ["EffectDiscSubsumeTheSpirit"] = "ww",
        ["EffectDiscTheForgetfulMind"] = "w",
        ["EffectDiscTheHaunting"] = "w",
        ["EffectDiscTorch"] = "w",
        ["EffectDiscTrueFaith"] = "ww",
        ["EffectDiscTrueSight"] = "w",
        ["EffectDiscVoiceOfMadness"] = "w",
        ["FLS1_Main"] = "w",
        ["FlyRegion"] = "w",
        ["FredVarneyCigar"] = "w",
        ["Generator"] = "swwwwww",
        ["HHX1_23_1"] = "w",
        ["HHX1_Etrius"] = "w",
        ["JOS3_15_1_Door"] = "w",
        ["MagicCurtain"] = "w",
        ["MultiDoor"] = "w",
        ["MultiPivotDoor"] = "w",
        ["MysticSpawner"] = "www",
        ["OLDT_Libussa"] = "w",
        ["PivotDoor"] = "w",
        ["player_pink"] = "w",
        ["RegionProjectileAtThing"] = "w",
        ["RegionTriggerRotate"] = "w",
        ["RegionTriggerSound"] = "o",
        ["SecretDoorSwitch"] = "w",
        ["SET4_31_1_Main"] = "www",
        ["SEW1_Main"] = "www",
        ["SEW2_ElectricPlate"] = "ww",
        ["SEW2_ElectricVertical"] = "w",
        ["SEW2_Main"] = "wwww",
        ["SIM3_WaterWheel"] = "w",
        ["SOUT_20_1_HausGate"] = "w",
        ["STE1_Pendulum"] = "w",
        ["STE2_FiveLevers"] = "wwwwww",
        ["STE3_LutherGlass"] = "w",
        ["STOR_38_1_Main"] = "w",
        ["SwitchDoubleDoor"] = "w",
        ["TEU1_22_1_Door"] = "w",
        ["TEU3_22_1_Door"] = "w",
        ["TEU4_22_1_Door"] = "w",
        ["TorchBearer"] = "w",
        ["TrapCloseRoom"] = "wwwww",
        ["TrapCloseRoom2"] = "wwwww",
        ["TrapFloorPlateZap"] = "o",
        ["WalkCycle"] = "ww",
        ["WalkCycleLawmen"] = "ww",
    };

    /// <summary>The tag that closes a codex record's script state (<c>NsgTag.CodexEnd</c>).</summary>
    const uint ClosingTag = 0x1d;

    /// <summary>
    /// Steps over a known class's state, leaving the reader on the closing tag, and answers true —
    /// false, having read nothing, for a class not in the table.
    /// </summary>
    public static bool TrySkip(string? className, BinReader r)
    {
        if (className is null || !Known.TryGetValue(className, out var layout)) return false;
        foreach (char c in layout)
        {
            if (c == 's') r.Bytes(r.Count32(1 << 20));
            else if (c == 'o') { if (r.Remaining >= 4 && BitConverter.ToUInt32(r.Data, r.Pos) != ClosingTag) r.U32(); }
            else r.U32();
        }
        return true;
    }

    /// <summary>
    /// A known class's saved state, word by word — an <see cref="int"/> for each word, a
    /// <see cref="string"/> for each string — or null for a class not in the table or state that does
    /// not fit its layout. The words carry no type in the file (a boolean is 0 or 1, a float its bits),
    /// and the script's own <c>restore</c> reads each back as the type it asks for.
    /// </summary>
    public static IReadOnlyList<object>? Decode(string? className, byte[] state)
    {
        if (className is null || !Known.TryGetValue(className, out var layout)) return null;
        var r = new BinReader(state, className);
        var values = new List<object>();
        try
        {
            foreach (char c in layout)
            {
                if (c == 's') values.Add(BinReader.CString(r.Bytes(r.Count32(1 << 20))));
                else if (c == 'o') { if (r.Remaining >= 4) values.Add(r.I32()); }
                else values.Add(r.I32());
            }
        }
        catch (NodFormatException) { return null; }
        return r.Remaining == 0 ? values : null;
    }
}

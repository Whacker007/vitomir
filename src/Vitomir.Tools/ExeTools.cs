// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Moved from the old program's Program.cs at 31f36b7, word for word but the first start's hook (the old program's own,
// retired with it).
using System.Globalization;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Scripting;

namespace Vitomir
{
    /// <summary>
    /// **The tools, where a run's start hands over to them** (once in the old program; now vitomir-tools';
    /// <see cref="IRunTools"/>): the same commands, at the same points of the start and in the same order as when they were
    /// <c>RunStart.Prepare</c>'s own lines. A tool never asks for the player's install.
    /// </summary>
    sealed class ExeTools : IRunTools
    {
        static string? Arg(string[] args, string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }

        static bool Flag(string[] args, string name) => Array.IndexOf(args, name) >= 0;

        public int? Commands(ref string[] args)
        {
            // **Vitomir's own copy of the game's files** (`GameData`): made from the 1.1 install
            // once, and run from after that.
            if (Flag(args, "--install-data")) return DataCommands.Install(Arg(args, "--install-data") is { } from && !from.StartsWith("--") ? from : null, Arg(args, "--install-to"));
            if (Flag(args, "--verify-data")) return DataCommands.Verify(Arg(args, "--install-to") ?? Arg(args, "--game") ?? GameData.DefaultGame);
            if (Flag(args, "--make-setup-manifest")) return DataCommands.MakeSetupManifest(args);   // the setup's manifest
            if (Flag(args, "--recook")) return RecookCommands.Recook(Arg(args, "--recook"), Arg(args, "--game"));   // the recooked base game (ours)
            if (Flag(args, "--format")) return FormatCommands.Format(Arg(args, "--format"), Flag(args, "--check"));   // the text data files laid out (ours)
            if (Flag(args, "--template")) return RecookCommands.Template(Arg(args, "--template"), Arg(args, "--templates"), Arg(args, "--game"), Arg(args, "--recooked"));
            if (Flag(args, "--export-gltf")) return ExportGltfCommand.Run(args);   // a level as glTF (ours)
            if (RelayoutL2Command.Handle(args) is { } relayoutExit) return relayoutExit;   // --relayout-l2 (ours)
            if (ProbesAtCommand.Handle(args) is { } probesAtExit) return probesAtExit;     // --probes-at (ours)
            if (PlaceOptions.Handle(args) is { } placeExit) return placeExit;   // --place <Place>=<package.l2> (ours)
            if (CookL2Command.Handle(ref args) is { } cookExit) return cookExit;   // --cook-l2, and --level-l2 <package> (ours)
            if (ConvertL2Command.Handle(args) is { } l2Exit) return l2Exit;   // --convert-l2, and --l2 <folder> (ours)
            if (Flag(args, "--stream-fly")) return StreamFlyCommand.Run(args);     // a streamed level flown through headless (ours)
            if (Flag(args, "--view-gltf")) return ViewGltfCommand.Run(args);       // a glTF scene through the modern renderer (ours)
            return null;
        }

        public int? WithFiles(string[] args, GameFiles files)
        {
            // The interface art tool (ours, UiArtCommands): a pane's art as one
            // picture and an outline to paint to, and a painted picture cut into the files the engine reads.
            if (Flag(args, "--ui-art-list")) return UiArtCommands.List(files);
            if (Arg(args, "--ui-art-sheet") is { } artSheet) return UiArtCommands.Sheet(files, artSheet, Arg(args, "--ui-art-out"), Arg(args, "--ui-art-scale"));
            if (Arg(args, "--ui-art-cut") is { } artCut) return UiArtCommands.Cut(files, artCut, Arg(args, "--ui-art-from"), Arg(args, "--ui-art-out"));
            if (Arg(args, "--ui-art-one") is { } artOne) return UiArtCommands.One(files, artOne, Arg(args, "--ui-art-from"), Arg(args, "--ui-art-out"));

            // Every level, with no window, into a folder — and a report of what changed since an earlier run.
            if (Arg(args, "--sweep") is { } sweepDir) return Sweep.Run(files, sweepDir, Arg(args, "--compare"), Arg(args, "--size") ?? "640x360", Arg(args, "--gpu"));
            // Every single-player place started with its scripts and run a minute, no window (ours, StorySweep.cs).
            if (Arg(args, "--story-sweep") is { } storyDir)
                return StorySweep.Sweep(files, CodexCompiler.For(files, GameData.CodexCache, Flag(args, CodexCompiler.SourcesSwitch)),
                    storyDir, Arg(args, "--story-sweep-only"), float.TryParse(Arg(args, "--story-seconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out var storySeconds)
                        ? storySeconds : StorySweep.DefaultSeconds, Console.Out,
                    Flag(args, "--story-buff") ? (float.TryParse(Arg(args, "--story-buff"), out var storyBuff) && storyBuff > 0 ? storyBuff : 100f) : 0f,
                    saveCheck: Flag(args, "--story-save-check"));
            return null;
        }

        public int? ListSaves(string[] args) => Flag(args, "--list-saves") ? ReportCommands.ListSaves() : null;

        public int? Reports(string[] args, ChronicleSet chronicles, string gameDir, GameFiles files)
        {
            if (Flag(args, "--chronicles")) return ReportCommands.Chronicles(chronicles, gameDir, Flag(args, "--verbose"));

            if (Flag(args, "--saves")) return ReportCommands.Saves(Arg(args, "--saves"), gameDir, Flag(args, "--verbose"));

            if (Flag(args, "--gait")) return ReportCommands.Gait(files, Flag(args, "--verbose"));
            return null;
        }

        public int? PlaceLoaded(string[] args, Location location, CodexScenes? scenes, GameFiles files, ChronicleSet chronicles, string levelName) =>
            new LevelReports(args, location, scenes, files, chronicles, levelName).Run();
    }
}

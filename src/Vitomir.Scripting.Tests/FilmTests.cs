// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// A story film shown by a host (the main loop's request 14, state 1): `PlayVideo` queues it, the
/// end of the step hands it to the host, and the script that asked hears `videoended` only when the
/// host says the film is over — not when it starts. Driven with the
/// Embrace, whose `videoended` jumps to the haven, and a fake player that shows the film until told.
/// </summary>
[Collection("scripts")]
public class FilmTests(Scripts s)
{
    [Fact]
    public void The_Embrace_film_holds_the_story_until_it_ends_and_then_videoended_jumps_to_the_haven()
    {
        using var m = Place.Enter(s, "P1_OLDT", "OLDT_7_4");
        var chronicle = m.Scenes.Chronicle!;
        var asked = new List<string>();
        m.Scenes.PlayFilm = name => { asked.Add(name); return true; };     // it shows, and has not ended
        m.Run(1.5f);
        var region = m.Where.Things.Single(t => t.Type == ThingType.Region && t.Captures.Count > 0);
        m.Where.Move(m.Christof, region.Position);
        Assert.True(m.RunUntil(() => asked.Count > 0, 5f), "the Embrace never asked for its film");
        Assert.Equal("embrace.bik", asked.Single(), ignoreCase: true);

        // While the film shows, the script has not heard `videoended`, so the story has not moved on.
        m.Run(2f);
        Assert.False(chronicle.Pending is { Kind: SequenceKind.Jump }, "videoended came before the film ended");

        // The film ends: now it hears it, and jumps to the haven's entrance 1.
        m.Scenes.FilmEnded();
        Assert.True(m.RunUntil(() => chronicle.Pending is { Kind: SequenceKind.Jump }, 1f), "videoended never arrived");
        Assert.Equal(chronicle.Find("Haven"), chronicle.Pending!.Value.To);
        Assert.Equal(1, chronicle.Pending.Value.Entrance);
        Assert.Single(asked);
    }

    [Fact]
    public void A_film_the_host_cannot_open_ends_at_once()
    {
        using var m = Place.Enter(s, "P1_OLDT", "OLDT_7_4");
        var chronicle = m.Scenes.Chronicle!;
        m.Scenes.PlayFilm = _ => false;                                      // the film did not open
        m.Run(1.5f);
        var region = m.Where.Things.Single(t => t.Type == ThingType.Region && t.Captures.Count > 0);
        m.Where.Move(m.Christof, region.Position);
        Assert.True(m.RunUntil(() => chronicle.Pending is { Kind: SequenceKind.Jump }, 5f));
        // And a stray end from the host afterwards tells nobody anything.
        m.Scenes.FilmEnded();
    }
}

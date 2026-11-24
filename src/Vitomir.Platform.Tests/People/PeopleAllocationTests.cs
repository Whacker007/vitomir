// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.People;

namespace Vitomir.Platform.Tests.People;

/// <summary>
/// That sampling the models repository's clips allocates nothing once a skeleton is mapped, measured as the
/// platform's allocation tests measure (<see cref="Allocations"/>: alone, more than once): a clip made in the test on
/// every machine, and the standard set's walk, turn and talk on the player when <c>VITOMIR_MODELS_DIR</c> is set.
/// </summary>
[Collection(AllocationCollection.Name)]
public sealed class PeopleAllocationTests
{
    static void Measure(string what, IReadOnlyList<INotedClip> clips, Skeleton skeleton)
    {
        var local = new BoneTransform[skeleton.Count];
        var face = new float[64];
        void Window()
        {
            for (int c = 0; c < clips.Count; c++)   // indexed: a foreach over the interface would box the array's enumerator
                for (int i = 0; i < 200; i++)
                {
                    clips[c].Sample(i * 0.0137f - 0.5f, skeleton, local);
                    if (clips[c] is GltfClip g) g.SampleFace(i * 0.0137f, face);
                }
        }
        Window();   // the mapping to this skeleton is made once, here
        Allocations.None(what, () =>
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            Window();
            return GC.GetAllocatedBytesForCurrentThread() - before;
        });
    }

    [Fact]
    public void Sampling_a_clip_made_here_allocates_nothing()
    {
        string folder = Path.Combine(Path.GetTempPath(), "vitomir-people-" + Guid.NewGuid().ToString("N"));
        try
        {
            TestPeople.Write(folder);
            var lib = PeopleLibrary.TryOpen(folder, out _)!;
            var person = lib.Person(new PersonRecipe("tester", "male", "test_base", ["test_outfit"]));
            Measure("sampling the test's clips", lib.Motions("male"), person.Model.Skeleton);
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Sampling_the_standard_set_on_the_player_allocates_nothing()
    {
        var lib = PeopleLibraryTests.Lib();
        var player = lib.Person(PersonRecipe.Player);
        var clips = lib.Motions("male").Where(c => c.Name is "walk" or "turn_left_90" or "talk" or "stand_folded").ToArray();
        Assert.Equal(4, clips.Length);
        Measure("sampling the walk, a turn, talking and a stand with STEP keys on the player", clips, player.Model.Skeleton);
    }
}

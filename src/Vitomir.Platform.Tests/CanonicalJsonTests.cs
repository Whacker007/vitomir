// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text;
using System.Text.Json;
using Vitomir.Platform.Config;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The canonical JSON writer and the SHA-256 helper: one document, however
/// laid out, gives one byte string and one hash; numbers keep the token text they were written with.
/// </summary>
public sealed class CanonicalJsonTests
{
    static string Canon(string json) => Encoding.UTF8.GetString(CanonicalJson.Canonicalize(json));

    [Fact]
    public void Keys_are_sorted_ordinally_and_whitespace_goes()
    {
        Assert.Equal("""{"A":1,"a":[true,false,null],"b":{"y":"z","z":2}}""",
            Canon("""
                {
                  "b": { "z": 2, "y": "z" },
                  "a": [ true, false, null ],
                  "A": 1
                }
                """));
    }

    [Fact]
    public void Numbers_keep_their_original_token_text()
    {
        Assert.Equal("""[1.0,1,2.25,1E3,-0,0.1000]""", Canon("[1.0, 1, 2.25, 1E3, -0, 0.1000]"));
    }

    [Fact]
    public void Strings_are_escaped_one_way_and_written_as_utf8()
    {
        // An escaped é and a literal é are the same string, so they canonicalise the same.
        Assert.Equal(Canon("\"caf\\u00e9 \\\"q\\\" \\n\""), Canon("\"café \\\"q\\\" \\n\""));
        var bytes = CanonicalJson.Canonicalize("\"1\\u20135 \\u0001\"");
        Assert.Equal("\"1–5 \\u0001\"", Encoding.UTF8.GetString(bytes));
        Assert.Equal(new byte[] { 0xE2, 0x80, 0x93 }, bytes[2..5]);
    }

    [Fact]
    public void Two_layouts_of_one_document_hash_the_same_and_a_changed_value_does_not()
    {
        string a = ContentHash.Sha256OfCanonicalJson("""{"x": 2.25, "y": [1, 2]}""");
        string b = ContentHash.Sha256OfCanonicalJson("""{ "y":[1,2] ,"x":2.25 }""");
        string c = ContentHash.Sha256OfCanonicalJson("""{"x": 2.5, "y": [1, 2]}""");
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Matches("^[0-9a-f]{64}$", a);
    }

    [Fact]
    public void The_sha256_helper_agrees_with_the_published_test_vector()
    {
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", ContentHash.Sha256Hex("abc"));
    }

    [Fact]
    public void A_duplicated_key_is_refused()
    {
        Assert.Throws<JsonException>(() => CanonicalJson.Canonicalize("""{"a":1,"a":2}"""));
    }
}

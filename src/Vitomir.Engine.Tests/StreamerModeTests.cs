// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Formats;
using Vitomir.Platform.Presentation;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Streamer mode in the Classic game's screens**: each place an address, the user's folder or a
/// secret would show is hidden with it on and shown with it off. The join screen's address box draws a dot for each character typed (a
/// password box stars always); the menus' lists (the lobby's games, its room's talk and status) and the message box (the ~
/// console's lines, the router's and the connections' lines a host is told) and the chat box hide them as they are laid out;
/// the Fixes page shows the setting and hands a change on. The setting is process-wide, so these run alone
/// (<see cref="StreamerModeCollection"/>), each putting it back.
/// </summary>
[Collection(nameof(StreamerModeCollection))]
public sealed class StreamerModeTests : IDisposable
{
    readonly bool was = StreamerMode.On;
    readonly string? profile = StreamerMode.Profile;

    public void Dispose()
    {
        StreamerMode.On = was;
        StreamerMode.Profile = profile;
        StreamerMode.Forget();
    }

    const string Profile = @"C:\Users\Anna";

    [Fact]
    public void The_join_address_box_draws_dots_on_a_stream_and_the_address_without_and_a_password_box_stars_always()
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find);
        string crypt = Path.Combine(Path.GetTempPath(), "vitomir-crypt-" + Guid.NewGuid().ToString("N"));
        var mp = new MpMenus(menus, files, text.Find, crypt) { Port = 0, Upnp = false, SettingsPath = null };
        try
        {
            // The join screen's By Address… (the address box, until the new flow the Internet button's).
            mp.OpenJoin(lan: false);
            var chat = menus.Pane(MpMenus.ChatFile);
            var byAddress = chat.Element("mpChatChangeRoomButton")!;
            var (x, y) = chat.At(byAddress);
            menus.PointerAt(x + byAddress.Size[0] / 2f, y + byAddress.Size[1] / 2f);
            menus.Frame(menus.NowMs);
            menus.LeftHeld = true;
            menus.Press();
            menus.LeftHeld = false;
            menus.Release();
            menus.Frame(menus.NowMs);
            var box = menus.Pane(FrontEnd.InputBoxFile);
            Assert.True(menus.IsUp(box));
            Assert.Equal(MpMenus.AddressTitle, box.Texts["ibTitle"]);
            var edit = box.Edits["ibEdit"];
            Assert.True(edit.Address);
            edit.Set("10.20.30.40:4994");

            StreamerMode.On = false;
            Assert.StartsWith("10.20.30.40:4994", edit.Shown);
            StreamerMode.On = true;
            Assert.StartsWith(new string(StreamerMode.Dot, "10.20.30.40:4994".Length), edit.Shown);
            Assert.DoesNotContain("10.20", edit.Shown);
            Assert.DoesNotContain("4994", edit.Shown);

            // A password box stars, on or off (1.1's own).
            menus.Hide(box);
            menus.Input("password", "", 32, password: true);
            var pw = menus.Pane(FrontEnd.InputBoxFile).Edits["ibEdit"];
            Assert.False(pw.Address);
            pw.Set("garlic");
            foreach (bool on in new[] { true, false })
            {
                StreamerMode.On = on;
                Assert.StartsWith("******", pw.Shown);
                Assert.DoesNotContain("garlic", pw.Shown);
            }
        }
        finally
        {
            mp.Server?.Dispose();
            mp.Client?.Dispose();
            if (Directory.Exists(crypt)) Directory.Delete(crypt, recursive: true);
        }
    }

    [Fact]
    public void The_lobby_s_lists_hide_an_address_a_host_s_name_as_typed_and_the_user_s_folder_on_a_stream()
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find);
        var room = menus.Pane(MpMenus.ChatFile).Lists["mpChatRoom"];
        StreamerMode.Profile = Profile;
        StreamerMode.Hide("friend.example.org:4994");

        StreamerMode.On = false;
        room.Add("Anna at 192.168.1.20:4994 and [fe80::1]:4994 via friend.example.org", 0);
        room.Add(@"saved in C:\Users\Anna\AppData\Local\Vitomir", 0);
        string shown = string.Join(" ", room.Rows.TakeLast(2).SelectMany(r => r.Lines));
        Assert.Contains("192.168.1.20:4994", shown);
        Assert.Contains("fe80::1", shown);
        Assert.Contains("friend.example.org", shown);
        Assert.Contains(@"C:\Users\Anna", shown);

        StreamerMode.On = true;
        room.Add("Anna at 192.168.1.20:4994 and [fe80::1]:4994 via friend.example.org", 0);
        room.Add(@"saved in C:\Users\Anna\AppData\Local\Vitomir", 0);
        string hidden = string.Join(" ", room.Rows.TakeLast(2).SelectMany(r => r.Lines));
        Assert.DoesNotContain("192.168", hidden);
        Assert.DoesNotContain("fe80", hidden);
        Assert.DoesNotContain("friend.example", hidden);
        Assert.DoesNotContain(@"Users\Anna", hidden);
        Assert.Contains(StreamerMode.Hidden, hidden);
        Assert.Contains(StreamerMode.ProfileWord + @"\AppData\Local\Vitomir", hidden);
        // What the row holds is what was said: only its drawing hides it.
        Assert.Contains("192.168.1.20:4994", room.Rows[^2].Text);
    }

    const string Nui = """
        name=statusBarSP
        pos=0,0
        element scrollslider name=sbscrollslider pos=474,93 size=8,8 bitmap=slider.tga
        element scrollbar name=sbscrollbar pos=474,93 size=8,42 flags=0x02 vertical=1 slider=sbscrollslider
        element textbox name=sbtextbox pos=164,94 size=2000,41 flags=0x02 maxitems=100 textboxflags=0x04 vscrollbar=sbscrollbar
        """;

    static NfdFont Font()
    {
        var glyphs = new NfdGlyph[256];
        for (int i = 0; i < 256; i++) glyphs[i] = new NfdGlyph(0, 0, 6, 10, 6, 0);
        return new NfdFont { LineHeight = 10, StripWidth = 1, StripHeight = 1, Glyphs = glyphs };
    }

    [Fact]
    public void The_message_box_hides_the_console_s_router_s_and_connections_lines_and_chat_on_a_stream_and_shows_them_again_without()
    {
        var pane = NuiPane.Parse(Nui, "statusBarSP.nui");
        UiControl Control(string name) => new(pane.Elements.First(e => e.Name == name));
        var log = new MessageLog();
        var box = new MessageBox(log, Font(), Control("sbtextbox"), Control("sbscrollbar"), Control("sbscrollslider"));
        StreamerMode.Profile = Profile;
        log.Add("net: UPnP mapped UDP 4994; players join at 203.0.113.7:4994");
        log.Add("net: connection from [2001:db8::5]:51234, round trip 40 ms");
        log.Add("<Anna> come to 198.51.100.2 now");
        log.Add(@"bundle: C:\Users\Anna\AppData\Local\Vitomir\Vitomir-logs.zip");
        log.Add("net: connection from ::ffff:10.0.0.2:5000 at 12:30:45");

        StreamerMode.On = false;
        box.Sync();
        string shown = string.Join("\n", box.Items.SelectMany(i => i.Lines));
        foreach (var said in new[] { "203.0.113.7:4994", "2001:db8::5", "198.51.100.2", @"C:\Users\Anna", "::ffff:10.0.0.2:5000" })
            Assert.Contains(said, shown);

        StreamerMode.On = true;
        box.Sync();                                                    // laid out again for the change, with nothing new said
        string hidden = string.Join("\n", box.Items.SelectMany(i => i.Lines));
        foreach (var gone in new[] { "203.0.113", "2001:db8", "198.51", @"Users\Anna", "10.0.0.2", ":5000" })
            Assert.DoesNotContain(gone, hidden);
        Assert.Contains("UDP 4994", hidden);                           // a port by itself names nothing
        Assert.Contains("12:30:45", hidden);                           // a clock is no address
        Assert.Contains(StreamerMode.ProfileWord, hidden);

        StreamerMode.On = false;
        box.Sync();
        Assert.Contains("203.0.113.7:4994", string.Join("\n", box.Items.SelectMany(i => i.Lines)));
    }

    [Fact]
    public void The_fixes_page_shows_streamer_mode_and_hands_on_a_change_by_ok_and_nothing_by_cancel()
    {
        var files = Install.Open();
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find) { State = FrontEnd.StateGame };
        string ini = Path.Combine(Path.GetTempPath(), $"vitomir-fixespage-{Guid.NewGuid():N}.ini");
        try
        {
            foreach (bool start in new[] { false, true })
            {
                StreamerMode.On = start;
                var page = new FixesPage(menus) { Host = () => null, SavePath = ini };
                bool? handed = null;
                page.StreamerChanged += on => handed = on;
                page.Open();
                var pane = page.Pane;
                Assert.Equal(start, pane.Checks[FixesPage.StreamerRow]);
                Assert.Equal(FixesPage.StreamerTitle, pane.Texts[FixesPage.StreamerRow + "label"]);
                Assert.DoesNotContain(FixesPage.StreamerRow, pane.Locked);
                page.Hover(pane, FixesPage.StreamerRow);
                Assert.Equal(FixesPage.StreamerWhat, pane.Texts["fxhint"]);

                pane.Check(FixesPage.StreamerRow, !start);
                page.Checked(pane, FixesPage.StreamerRow);
                Assert.Equal(!start, page.EditingStreamer);
                page.Clicked("fxcancelbutton");
                Assert.Null(handed);

                page.Open();
                Assert.Equal(start, pane.Checks[FixesPage.StreamerRow]);
                pane.Check(FixesPage.StreamerRow, !start);
                page.Checked(pane, FixesPage.StreamerRow);
                page.Clicked("fxokbutton");
                Assert.Equal(!start, handed);
                Assert.Equal(start, StreamerMode.On);                 // the page sets nothing itself: the host does
                // The game's ini holds no streamer key: the setting is the platform's.
                Assert.DoesNotContain("streamer", File.ReadAllText(ini), StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            if (File.Exists(ini)) File.Delete(ini);
        }
    }
}

/// <summary>The streamer mode tests, which set a process-wide setting, alone.</summary>
[CollectionDefinition(nameof(StreamerModeCollection), DisableParallelization = true)]
public sealed class StreamerModeCollection;

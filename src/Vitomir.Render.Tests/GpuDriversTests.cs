// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Render.Tests;

/// <summary>
/// **Which GPU driver the game opens (ours).** The new town crashed on its first frame under
/// SDL's Direct3D 12 driver on a fast NVIDIA card and runs on Vulkan, so on Windows the levels
/// build tries Vulkan first and SDL's own choice only when no Vulkan device can be made; <c>--gpu</c> names
/// one driver and nothing else is tried. These pin <see cref="GpuDrivers.Order"/> and the one line the
/// console says; opening a device is SDL's and cannot be exercised without the drivers themselves.
/// </summary>
public class GpuDriversTests
{
    [Fact]
    public void On_Windows_with_nothing_asked_Vulkan_is_tried_first_and_then_SDLs_own_choice()
    {
        Assert.Equal(new string?[] { "vulkan", null }, GpuDrivers.Order(null, windows: true).AsEnumerable());
        Assert.Equal(new string?[] { "vulkan", null }, GpuDrivers.Order("  ", windows: true).AsEnumerable());
    }

    [Fact]
    public void Off_Windows_with_nothing_asked_SDL_chooses()
    {
        Assert.Equal(new string?[] { null }, GpuDrivers.Order(null, windows: false).AsEnumerable());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_driver_asked_for_is_the_only_one_tried(bool windows)
    {
        Assert.Equal(new string?[] { "direct3d12" }, GpuDrivers.Order("direct3d12", windows).AsEnumerable());
        Assert.Equal(new string?[] { "vulkan" }, GpuDrivers.Order(" vulkan ", windows).AsEnumerable());
    }

    [Fact]
    public void The_console_says_Vulkan_is_the_levels_builds_default_and_how_to_get_Direct3D_12()
    {
        Assert.Equal("gpu: vulkan (the default for Vitomir's levels build; --gpu direct3d12 for Direct3D 12)",
                     GpuDrivers.Line("vulkan", asked: null, refused: null, windows: true));
    }

    [Fact]
    public void When_Vulkan_was_refused_the_console_says_what_was_taken_and_SDLs_reason()
    {
        string refused = GpuDrivers.Refused("vulkan", "Vulkan: no suitable device found ");
        Assert.Equal("no Vulkan device: Vulkan: no suitable device found", refused);
        Assert.Equal("gpu: direct3d12 (no Vulkan device: Vulkan: no suitable device found)",
                     GpuDrivers.Line("direct3d12", asked: null, refused, windows: true));
        Assert.Equal("no Vulkan device: SDL gave no reason", GpuDrivers.Refused("vulkan", ""));
    }

    [Fact]
    public void A_driver_asked_for_is_named_with_its_flag()
    {
        Assert.Equal("gpu: direct3d12 (--gpu direct3d12)", GpuDrivers.Line("direct3d12", "direct3d12", null, windows: true));
    }

    [Fact]
    public void Off_Windows_the_console_names_SDLs_choice()
    {
        Assert.Equal("gpu: vulkan (SDL's choice; --gpu <name> for another)", GpuDrivers.Line("vulkan", null, null, windows: false));
        Assert.Equal("gpu: metal (SDL's choice; --gpu <name> for another)", GpuDrivers.Line("metal", null, null, windows: false));
    }
}

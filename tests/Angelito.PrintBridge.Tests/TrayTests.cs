// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

using Angelito.PrintBridge.Desktop.Services;
using Avalonia;

namespace Angelito.PrintBridge.Tests;

public class PanelPlacementTests
{
    private static readonly PixelSize Panel = new(380, 420);

    private static void AssertInside(PixelRect work, PixelPoint p, PixelSize size)
    {
        Assert.InRange(p.X, work.X, work.Right - size.Width);
        Assert.InRange(p.Y, work.Y, work.Bottom - size.Height);
    }

    [Fact]
    public void Bottom_taskbar_opens_above_the_click_near_the_clock()
    {
        var bounds = new PixelRect(0, 0, 1920, 1080);
        var work = new PixelRect(0, 0, 1920, 1032);
        var p = PanelPlacement.Compute(bounds, work, new PixelPoint(1850, 1060), Panel, 1.0);
        Assert.Equal(1032 - 420 - 12, p.Y);
        Assert.Equal(1920 - 380 - 12, p.X); // clamped: never off the right edge
        AssertInside(work, p, Panel);
    }

    [Theory]
    [InlineData(0, 48, 1920, 1032, "top")]
    [InlineData(48, 0, 1872, 1080, "left")]
    [InlineData(0, 0, 1872, 1080, "right")]
    public void Panel_sits_next_to_the_taskbar_on_any_edge(int x, int y, int w, int h, string edge)
    {
        var bounds = new PixelRect(0, 0, 1920, 1080);
        var work = new PixelRect(x, y, w, h);
        var cursor = edge switch { "top" => new PixelPoint(1800, 20), "left" => new PixelPoint(20, 900), _ => new PixelPoint(1900, 900) };
        var p = PanelPlacement.Compute(bounds, work, cursor, Panel, 1.0);
        AssertInside(work, p, Panel);
        switch (edge)
        {
            case "top": Assert.Equal(48 + 12, p.Y); break;
            case "left": Assert.Equal(48 + 12, p.X); break;
            default: Assert.Equal(1872 - 380 - 12, p.X); break;
        }
    }

    [Fact]
    public void Secondary_monitor_with_negative_coordinates_and_150_percent_scaling()
    {
        // Monitor to the left of the primary, 4K at 150 %: panel 360x400 DIP → 540x600 px.
        var bounds = new PixelRect(-3840, 0, 3840, 2160);
        var work = new PixelRect(-3840, 0, 3840, 2088);
        var size = PixelSize.FromSize(new Size(360, 400), 1.5);
        var p = PanelPlacement.Compute(bounds, work, new PixelPoint(-100, 2120), size, 1.5);
        Assert.Equal(2088 - 600 - 18, p.Y);
        Assert.Equal(-540 - 18, p.X);
        AssertInside(work, p, size);
    }

    [Fact]
    public void Panel_larger_than_work_area_is_pinned_to_the_top_left_margin()
    {
        var work = new PixelRect(0, 0, 300, 300);
        var p = PanelPlacement.Compute(work, work, new PixelPoint(150, 150), Panel, 1.0);
        Assert.Equal(new PixelPoint(12, 12), p);
    }
}

// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.InteropServices;
using Avalonia;

namespace Angelito.PrintBridge.Desktop.Services;

/// <summary>Places the tray mini panel next to the taskbar, near the click, fully inside the work area. All values in physical pixels.</summary>
public static class PanelPlacement
{
    public const int Margin = 12;

    public static PixelPoint Compute(PixelRect bounds, PixelRect workArea, PixelPoint cursor, PixelSize panel, double scaling)
    {
        var gap = (int)Math.Round(Margin * scaling);
        int x, y;
        if (workArea.Y > bounds.Y)
        {   // taskbar on top
            x = cursor.X - panel.Width / 2;
            y = workArea.Y + gap;
        }
        else if (workArea.X > bounds.X)
        {   // taskbar on the left
            x = workArea.X + gap;
            y = cursor.Y - panel.Height / 2;
        }
        else if (workArea.Right < bounds.Right)
        {   // taskbar on the right
            x = workArea.Right - panel.Width - gap;
            y = cursor.Y - panel.Height / 2;
        }
        else
        {   // taskbar at the bottom (default) or auto-hidden: open above the click like Windows 11 flyouts
            x = cursor.X - panel.Width / 2;
            y = workArea.Bottom - panel.Height - gap;
        }
        x = Math.Clamp(x, workArea.X + gap, Math.Max(workArea.X + gap, workArea.Right - panel.Width - gap));
        y = Math.Clamp(y, workArea.Y + gap, Math.Max(workArea.Y + gap, workArea.Bottom - panel.Height - gap));
        return new PixelPoint(x, y);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT p);

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    public static PixelPoint Cursor() => GetCursorPos(out var p) ? new PixelPoint(p.X, p.Y) : default;

    public static long DoubleClickMs => GetDoubleClickTime();
}

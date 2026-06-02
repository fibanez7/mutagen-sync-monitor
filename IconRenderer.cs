using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows;
using MutagenManager.Models;

namespace MutagenManager;

/// <summary>
/// Generates tray icons and status bitmaps using GDI+.
/// Ported from the PS1 icon generation logic.
/// </summary>
public static class IconRenderer
{
    private static Icon? _baseIcon;
    private static Bitmap? _baseIconBitmap;

    // GDI leak fix (v3.1): tray icons + status dots were re-rendered every poll.
    // GetHicon() allocates an unmanaged HICON that Icon.FromHandle does NOT own,
    // so it was never destroyed → GDI handles climbed toward the 10000/process
    // limit until submenu rendering failed (blank items, hang, crash). There are
    // only 5 status codes, so render each once and cache for the app lifetime.
    private static readonly Dictionary<SyncStatusCode, Icon>   _trayIconCache   = new();
    private static readonly Dictionary<SyncStatusCode, Bitmap> _statusDotCache  = new();
    private static readonly Dictionary<string, Bitmap>         _glyphCache      = new();

    public static void LoadBaseIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Resources/mutagen.ico");
            var info = System.Windows.Application.GetResourceStream(uri);
            if (info != null)
            {
                _baseIcon = new Icon(info.Stream, 16, 16);
                _baseIconBitmap = _baseIcon.ToBitmap();
            }
        }
        catch
        {
            _baseIconBitmap = null;
        }
    }

    /// <summary>
    /// Creates the tray icon: base icon + colored overlay dot in bottom-right corner.
    /// </summary>
    public static Icon GetTrayIcon(SyncStatusCode status)
    {
        if (_trayIconCache.TryGetValue(status, out var cached))
            return cached;

        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            // Draw base icon (hexagon fallback if no icon file)
            if (_baseIconBitmap != null)
                g.DrawImage(_baseIconBitmap, 0, 0, 16, 16);
            else
                DrawHexagon(g, 16);

            // Overlay dot
            var color = StatusColor(status);
            const int overlaySize = 6;
            const int overlayX = 16 - overlaySize - 1;
            const int overlayY = 16 - overlaySize - 1;

            using var white = new SolidBrush(Color.White);
            g.FillEllipse(white, overlayX - 1, overlayY - 1, overlaySize + 2, overlaySize + 2);

            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, overlayX, overlayY, overlaySize, overlaySize);
        }

        // Clone the GDI icon into a managed Icon that owns its data, then destroy
        // the transient HICON so no unmanaged handle leaks.
        var hIcon = bmp.GetHicon();
        using var tmp = Icon.FromHandle(hIcon);
        var icon = (Icon)tmp.Clone();
        DestroyIcon(hIcon);

        _trayIconCache[status] = icon;
        return icon;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>
    /// Renders a Segoe MDL2 Assets glyph as a 16×16 bitmap for use in ContextMenuStrip.
    /// Falls back to a colored dot if the font is unavailable.
    /// </summary>
    public static Bitmap MakeGlyphIcon(string glyph, Color color, int size = 16)
    {
        // Glyphs are a fixed, small set reused across every menu rebuild — cache them
        // so RebuildSyncMenuItems doesn't re-render (and leak) GDI bitmaps each time.
        var key = $"{glyph}|{color.ToArgb()}|{size}";
        if (_glyphCache.TryGetValue(key, out var hit)) return hit;

        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode      = SmoothingMode.AntiAlias;
        g.TextRenderingHint  = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        try
        {
            using var font  = new Font("Segoe MDL2 Assets", size * 0.68f, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(color);
            var sf = new StringFormat
            {
                Alignment     = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            g.DrawString(glyph, font, brush, new RectangleF(0, 0, size, size), sf);
        }
        catch
        {
            // Fallback: colored dot
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 2, 2, size - 4, size - 4);
        }

        _glyphCache[key] = bmp;
        return bmp;
    }

    /// <summary>Small colored circle used for sync items in the context menu.</summary>
    public static Bitmap GetStatusDot(SyncStatusCode status, int size = 16)
    {
        // Cache only the default size (used per-poll on every sync menu item).
        if (size == 16 && _statusDotCache.TryGetValue(status, out var cached))
            return cached;

        var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(StatusColor(status));
            g.FillEllipse(brush, 2, 2, size - 4, size - 4);
        }

        if (size == 16)
            _statusDotCache[status] = bmp;
        return bmp;
    }

    private static Color StatusColor(SyncStatusCode status) => status switch
    {
        SyncStatusCode.Ok       => Color.FromArgb(76,  175, 80),
        SyncStatusCode.Paused   => Color.FromArgb(255, 152, 0),
        SyncStatusCode.Conflict => Color.FromArgb(244, 67,  54),
        SyncStatusCode.Error    => Color.FromArgb(244, 67,  54),
        _                       => Color.FromArgb(158, 158, 158),
    };

    private static void DrawHexagon(Graphics g, int size)
    {
        var cx = size / 2f;
        var cy = size / 2f;
        var r  = size / 2f - 1;
        var pts = new PointF[6];
        for (int i = 0; i < 6; i++)
        {
            var angle = Math.PI / 3 * i - Math.PI / 2;
            pts[i] = new PointF(
                cx + r * (float)Math.Cos(angle),
                cy + r * (float)Math.Sin(angle));
        }
        using var brush = new SolidBrush(Color.FromArgb(100, 100, 100));
        g.FillPolygon(brush, pts);
    }
}

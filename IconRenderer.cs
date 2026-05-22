using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
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
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
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

        var hIcon = bmp.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    /// <summary>
    /// Renders a Segoe MDL2 Assets glyph as a 16×16 bitmap for use in ContextMenuStrip.
    /// Falls back to a colored dot if the font is unavailable.
    /// </summary>
    public static Bitmap MakeGlyphIcon(string glyph, Color color, int size = 16)
    {
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

        return bmp;
    }

    /// <summary>Small colored circle used for sync items in the context menu.</summary>
    public static Bitmap GetStatusDot(SyncStatusCode status, int size = 16)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(StatusColor(status));
        g.FillEllipse(brush, 2, 2, size - 4, size - 4);
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

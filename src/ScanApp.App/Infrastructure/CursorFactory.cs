using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScanApp.App.Infrastructure;

/// <summary>Builds a rotate-glyph mouse cursor at runtime (falls back to null on any failure).</summary>
public static class CursorFactory
{
    public static Cursor? CreateRotate()
    {
        try
        {
            const int size = 32;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // A ~270° arc with an arrowhead — the "rotate" glyph. Drawn with a dark halo so it
                // reads on both light and dark backgrounds.
                var center = new Point(size / 2.0, size / 2.0);
                const double r = 9;
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    var start = new Point(center.X, center.Y - r);
                    ctx.BeginFigure(start, false, false);
                    ctx.ArcTo(new Point(center.X + r, center.Y + r * 0.2), new Size(r, r),
                        0, true, SweepDirection.Clockwise, true, false);
                }
                geo.Freeze();

                var halo = new Pen(new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)), 4.5);
                var stroke = new Pen(Brushes.White, 2.2);
                dc.DrawGeometry(null, halo, geo);
                dc.DrawGeometry(null, stroke, geo);

                // Arrowhead near the arc end.
                var tip = new Point(center.X + r, center.Y + r * 0.2);
                var head = new StreamGeometry();
                using (var ctx = head.Open())
                {
                    ctx.BeginFigure(new Point(tip.X - 5, tip.Y - 3), true, true);
                    ctx.LineTo(new Point(tip.X + 3, tip.Y - 1), true, false);
                    ctx.LineTo(new Point(tip.X - 2, tip.Y + 5), true, false);
                }
                head.Freeze();
                dc.DrawGeometry(Brushes.White, new Pen(new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)), 1), head);
            }

            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var png = new MemoryStream();
            encoder.Save(png);
            var pngBytes = png.ToArray();

            // Wrap the PNG in a .cur (icon dir type 2) with the hotspot at the centre.
            using var cur = new MemoryStream();
            var w = new BinaryWriter(cur);
            w.Write((short)0);      // reserved
            w.Write((short)2);      // type: cursor
            w.Write((short)1);      // count
            w.Write((byte)size);    // width
            w.Write((byte)size);    // height
            w.Write((byte)0);       // colors
            w.Write((byte)0);       // reserved
            w.Write((short)(size / 2)); // hotspot x
            w.Write((short)(size / 2)); // hotspot y
            w.Write(pngBytes.Length);   // bytes in resource
            w.Write(6 + 16);            // offset to image
            w.Write(pngBytes);
            w.Flush();
            cur.Position = 0;
            return new Cursor(cur);
        }
        catch
        {
            return null;
        }
    }
}

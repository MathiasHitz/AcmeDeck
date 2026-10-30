using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Draws the AcmeDeck icon on a 256 canvas and writes a multi-size .ico,
// 64/256 px PNGs and a preview sheet.
//
//   cd tools\IconGen
//   dotnet run -- ..\..\src\AcmeDeck\Assets
//
// Delete the preview.png it leaves in Assets afterwards.
//   >= 48 px: padlock inside a renewal arrow ring
//   <= 32 px: simplified, larger padlock only
var outDir = args.Length > 0 ? args[0] : ".";
Directory.CreateDirectory(outDir);

var teal = (Color)ColorConverter.ConvertFromString("#0F766E");
var bg = new LinearGradientBrush(
    (Color)ColorConverter.ConvertFromString("#16A394"),
    (Color)ColorConverter.ConvertFromString("#0B5650"),
    new Point(0, 0), new Point(1, 1));
var white = Brushes.White;
var keyhole = new SolidColorBrush(teal);

void DrawBackground(DrawingContext dc) =>
    dc.DrawRoundedRectangle(bg, null, new Rect(8, 8, 240, 240), 54, 54);

void DrawPadlock(DrawingContext dc, double cx, double bodyTop, double bodyW, double bodyH, double radius,
                 double shackleR, double shackleStroke, double holeR)
{
    // Shackle: open-bottom arc that ends inside the body
    var pen = new Pen(white, shackleStroke) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
    var left = cx - shackleR;
    var right = cx + shackleR;
    var top = bodyTop - shackleR * 0.25;
    var shackle = new PathGeometry([new PathFigure(new Point(left, bodyTop + 6), [
        new LineSegment(new Point(left, top), true),
        new ArcSegment(new Point(right, top), new Size(shackleR, shackleR), 0, false, SweepDirection.Clockwise, true),
        new LineSegment(new Point(right, bodyTop + 6), true)], false)]);
    dc.DrawGeometry(null, pen, shackle);

    // Body
    dc.DrawRoundedRectangle(white, null, new Rect(cx - bodyW / 2, bodyTop, bodyW, bodyH), radius, radius);

    // Keyhole
    var holeY = bodyTop + bodyH * 0.42;
    dc.DrawEllipse(keyhole, null, new Point(cx, holeY), holeR, holeR);
    dc.DrawRoundedRectangle(keyhole, null,
        new Rect(cx - holeR * 0.42, holeY, holeR * 0.84, bodyH * 0.33), holeR * 0.3, holeR * 0.3);
}

void DrawRing(DrawingContext dc)
{
    const double cx = 128, cy = 128, r = 86, stroke = 15;
    // Screen angles in degrees: 0 = right, 90 = down. Gap at the top right.
    const double start = -18, end = 288;
    Point At(double deg) => new(cx + r * Math.Cos(deg * Math.PI / 180), cy + r * Math.Sin(deg * Math.PI / 180));

    var arc = new PathGeometry([new PathFigure(At(start), [
        new ArcSegment(At(end), new Size(r, r), 0, true, SweepDirection.Clockwise, true)], false)]);
    dc.DrawGeometry(null, new Pen(white, stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Flat }, arc);

    // Arrow head at the end, pointing along the clockwise direction
    var t = end * Math.PI / 180;
    var dir = new Vector(-Math.Sin(t), Math.Cos(t));
    var normal = new Vector(Math.Cos(t), Math.Sin(t));
    var p = At(end);
    var tip = p + dir * 26;
    var a = p + normal * 22 - dir * 4;
    var b = p - normal * 22 - dir * 4;
    var head = new PathGeometry([new PathFigure(tip, [new LineSegment(a, true), new LineSegment(b, true)], true)]);
    dc.DrawGeometry(white, null, head);
}

void DrawIcon(DrawingContext dc, bool small)
{
    DrawBackground(dc);
    if (small)
    {
        DrawPadlock(dc, 128, 118, 140, 100, 20, 42, 24, 17);
    }
    else
    {
        DrawRing(dc);
        DrawPadlock(dc, 128, 122, 76, 58, 11, 23, 12, 9);
    }
}

BitmapSource Render(int size)
{
    var visual = new DrawingVisual();
    using (var dc = visual.RenderOpen())
    {
        dc.PushTransform(new ScaleTransform(size / 256.0, size / 256.0));
        DrawIcon(dc, size <= 32);
        dc.Pop();
    }
    var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
    RenderOptions.SetEdgeMode(visual, EdgeMode.Unspecified);
    bmp.Render(visual);
    return bmp;
}

byte[] Png(BitmapSource source)
{
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(source));
    using var ms = new MemoryStream();
    encoder.Save(ms);
    return ms.ToArray();
}

int[] sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];
var images = sizes.Select(s => (Size: s, Bitmap: Render(s))).ToList();

// .ico with PNG-compressed entries (supported since Windows Vista)
using (var ico = new BinaryWriter(File.Create(Path.Combine(outDir, "AcmeDeck.ico"))))
{
    var data = images.Select(i => Png(i.Bitmap)).ToList();
    ico.Write((short)0);
    ico.Write((short)1);
    ico.Write((short)images.Count);
    var offset = 6 + 16 * images.Count;
    for (var i = 0; i < images.Count; i++)
    {
        var s = images[i].Size;
        ico.Write((byte)(s >= 256 ? 0 : s));
        ico.Write((byte)(s >= 256 ? 0 : s));
        ico.Write((byte)0);
        ico.Write((byte)0);
        ico.Write((short)1);
        ico.Write((short)32);
        ico.Write(data[i].Length);
        ico.Write(offset);
        offset += data[i].Length;
    }
    foreach (var d in data)
    {
        ico.Write(d);
    }
}
File.WriteAllBytes(Path.Combine(outDir, "AcmeDeck-256.png"), Png(images.Last().Bitmap));
File.WriteAllBytes(Path.Combine(outDir, "AcmeDeck-64.png"), Png(images.First(i => i.Size == 64).Bitmap));

// Preview sheet: real sizes on light and dark, plus enlarged small sizes
var sheet = new DrawingVisual();
using (var dc = sheet.RenderOpen())
{
    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 900, 560));
    dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x20, 0x22, 0x26)), null, new Rect(0, 280, 900, 280));
    dc.DrawImage(images.Last().Bitmap, new Rect(24, 12, 256, 256));
    double x = 310;
    foreach (var (s, bmp) in images.Where(i => i.Size <= 128))
    {
        dc.DrawImage(bmp, new Rect(x, 140 - s / 2.0, s, s));
        dc.DrawImage(bmp, new Rect(x, 420 - s / 2.0, s, s));
        x += s + 18;
    }
    // 16 and 32 px enlarged 6x / 3x without smoothing, to judge the pixels
    dc.DrawImage(images.First(i => i.Size == 16).Bitmap, new Rect(24, 296, 96, 96));
    dc.DrawImage(images.First(i => i.Size == 32).Bitmap, new Rect(140, 296, 96, 96));
}
var sheetBmp = new RenderTargetBitmap(900, 560, 96, 96, PixelFormats.Pbgra32);
RenderOptions.SetBitmapScalingMode(sheet, BitmapScalingMode.NearestNeighbor);
sheetBmp.Render(sheet);
File.WriteAllBytes(Path.Combine(outDir, "preview.png"), Png(sheetBmp));
Console.WriteLine("written to " + Path.GetFullPath(outDir));

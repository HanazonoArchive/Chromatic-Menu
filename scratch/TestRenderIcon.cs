using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

class IconTester
{
    [STAThread]
    static void Main()
    {
        string[] names = new[] { "terminal", "code", "download", "upload", "unlink", "search", "pencil" };
        string[] geoms = new[]
        {
            "M 12 19  h 8 M 4 17  l 6 -6  -6 -6",
            "M 16 18  l 6 -6  -6 -6 M 8 6  l -6 6  6 6",
            "M 12 15  V 3 M 7 10  l 5 5  5 -5 M 21 15  v 4  a 2 2 0 0 1 -2 2  H 5  a 2 2 0 0 1 -2 -2  v -4",
            "M 17 8  l -5 -5  -5 5 M 12 3  v 12 M 21 15  v 4  a 2 2 0 0 1 -2 2  H 5  a 2 2 0 0 1 -2 -2  v -4",
            "M 18.84 12.25  l 1.72 -1.71  h -.02  a 5.004 5.004 0 0 0 -.12 -7.07 5.006 5.006 0 0 0 -6.95 0  l -1.72 1.71 M 5.17 11.75  l -1.71 1.71  a 5.004 5.004 0 0 0 .12 7.07 5.006 5.006 0 0 0 6.95 0  l 1.71 -1.71 M 8,2 L 8,5 M 2,8 L 5,8 M 16,19 L 16,22 M 19,16 L 22,16",
            "M 21 21  l -4.34 -4.34 M 3,11 A 8,8 0 1 0 19,11 A 8,8 0 1 0 3,11",
            "M 21.174 6.812  a 1 1 0 0 0 -3.986 -3.987  L 3.842 16.174  a 2 2 0 0 0 -.5 .83  l -1.321 4.352  a .5 .5 0 0 0 .623 .622  l 4.353 -1.32  a 2 2 0 0 0 .83 -.497  z M 15 5  l 4 4"
        };

        for (int i = 0; i < names.Length; i++)
        {
            var geom = Geometry.Parse(geoms[i]);
            var path = new System.Windows.Shapes.Path
            {
                Data = geom,
                Stroke = Brushes.White,
                StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            };

            var canvas = new Canvas { Width = 24, Height = 24 };
            canvas.Children.Add(path);

            var viewbox = new Viewbox { Width = 48, Height = 48, Child = canvas };
            viewbox.Measure(new Size(48, 48));
            viewbox.Arrange(new Rect(0, 0, 48, 48));

            var rtb = new RenderTargetBitmap(48, 48, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(viewbox);

            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(@"c:\Users\Hanazono Archive\Desktop\Chromatic Menu\scratch\test_" + names[i] + ".png"))
            {
                enc.Save(fs);
            }
            Console.WriteLine("Saved test_" + names[i] + ".png");
        }
    }
}

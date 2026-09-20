using System;
using System.Windows.Media;

class Program
{
    [STAThread]
    static void Main()
    {
        string data = "M 18 6  L 6 18 M 6 6  l 12 12";
        var geom = Geometry.Parse(data);
        var pathGeom = PathGeometry.CreateFromGeometry(geom);
        Console.WriteLine("Figures count: " + pathGeom.Figures.Count);
        for (int i = 0; i < pathGeom.Figures.Count; i++)
        {
            var fig = pathGeom.Figures[i];
            Console.WriteLine(string.Format("Figure {0}: StartPoint={1}, Segments={2}", i, fig.StartPoint, fig.Segments.Count));
            foreach (var seg in fig.Segments)
            {
                LineSegment ls = seg as LineSegment;
                if (ls != null)
                {
                    Console.WriteLine("  LineSegment Point=" + ls.Point);
                }
                else
                {
                    Console.WriteLine("  Segment=" + seg.GetType().Name);
                }
            }
        }
    }
}

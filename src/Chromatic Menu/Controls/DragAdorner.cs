using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace ChromaticMenu.Controls
{
    public class DragAdorner : Adorner
    {
        private readonly UIElement _child;
        private Point _location;

        public DragAdorner(UIElement adornedElement, UIElement child) : base(adornedElement)
        {
            _child = child;
            IsHitTestVisible = false;
        }

        public void UpdatePosition(Point location)
        {
            _location = location;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (_child == null) return;

            var brush = new VisualBrush(_child)
            {
                Opacity = 0.75
            };

            var size = _child.RenderSize;
            dc.DrawRectangle(brush, null, new Rect(new Point(_location.X - size.Width / 2, _location.Y - size.Height / 2), size));
        }
    }
}

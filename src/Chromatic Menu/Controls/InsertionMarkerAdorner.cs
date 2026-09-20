using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace ChromaticMenu.Controls
{
    public class InsertionMarkerAdorner : Adorner
    {
        private Rect _targetRect;
        private bool _insertAfter;
        private readonly Pen _markerPen;

        public InsertionMarkerAdorner(UIElement adornedElement) : base(adornedElement)
        {
            IsHitTestVisible = false;
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0284C7"));
            brush.Freeze();
            _markerPen = new Pen(brush, 3.0);
            _markerPen.Freeze();
        }

        public void UpdateMarker(Rect targetRect, bool insertAfter)
        {
            _targetRect = targetRect;
            _insertAfter = insertAfter;
            InvalidateVisual();
        }

        public void ClearMarker()
        {
            _targetRect = Rect.Empty;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (_targetRect.IsEmpty) return;

            double x = _insertAfter ? _targetRect.Right + 3 : _targetRect.Left - 3;
            double top = _targetRect.Top;
            double bottom = _targetRect.Bottom;

            dc.DrawLine(_markerPen, new Point(x, top), new Point(x, bottom));
            dc.DrawEllipse(_markerPen.Brush, null, new Point(x, top), 4, 4);
            dc.DrawEllipse(_markerPen.Brush, null, new Point(x, bottom), 4, 4);
        }
    }
}

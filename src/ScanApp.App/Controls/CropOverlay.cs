using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ScanApp.App.Infrastructure;
using ScanApp.Core.Models;

namespace ScanApp.App.Controls;

/// <summary>
/// A draggable / resizable crop rectangle drawn over the preview image. It stores the crop in
/// normalized (0..1) coordinates via <see cref="CropRect"/> (two-way bindable), independent of the
/// image's pixel size, and maps to/from screen space using the same <c>Uniform</c> letterbox the
/// preview <see cref="Image"/> uses. Non-destructive: it only reports a rectangle; cropping happens
/// at export.
/// </summary>
public sealed class CropOverlay : Canvas
{
    private readonly Rectangle _selection;
    private readonly Thumb[] _corners = new Thumb[4]; // TL, TR, BL, BR
    private Rect _selPx; // current selection in control coordinates
    private static readonly Cursor? RotateCursor = CursorFactory.CreateRotate();

    public CropOverlay()
    {
        ClipToBounds = true;
        Background = Brushes.Transparent;
        // The empty area around the crop box rotates (straighten); the box body moves, handles resize.
        Cursor = RotateCursor ?? Cursors.Cross;
        MouseLeftButtonDown += OnRotateDown;
        MouseMove += OnRotateMove;
        MouseLeftButtonUp += OnRotateUp;

        _selection = new Rectangle
        {
            Stroke = Brushes.White,
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Fill = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)),
            Cursor = Cursors.SizeAll
        };
        _selection.MouseLeftButtonDown += OnBodyDown;
        _selection.MouseMove += OnBodyMove;
        _selection.MouseLeftButtonUp += OnBodyUp;
        Children.Add(_selection);

        for (int i = 0; i < 4; i++)
        {
            var t = new Thumb
            {
                Width = 14,
                Height = 14,
                Tag = i,
                Cursor = (i == 0 || i == 3) ? Cursors.SizeNWSE : Cursors.SizeNESW,
                Template = HandleTemplate()
            };
            t.DragDelta += OnCornerDrag;
            _corners[i] = t;
            Children.Add(t);
        }

        SizeChanged += (_, _) => UpdateVisual();
    }

    public static readonly DependencyProperty CropRectProperty = DependencyProperty.Register(
        nameof(CropRect), typeof(NormalizedRect), typeof(CropOverlay),
        new FrameworkPropertyMetadata(NormalizedRect.Full,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnCropChanged));

    public NormalizedRect CropRect
    {
        get => (NormalizedRect)GetValue(CropRectProperty);
        set => SetValue(CropRectProperty, value);
    }

    public static readonly DependencyProperty ImagePixelWidthProperty = DependencyProperty.Register(
        nameof(ImagePixelWidth), typeof(double), typeof(CropOverlay),
        new FrameworkPropertyMetadata(1.0, OnGeometryChanged));

    public double ImagePixelWidth
    {
        get => (double)GetValue(ImagePixelWidthProperty);
        set => SetValue(ImagePixelWidthProperty, value);
    }

    public static readonly DependencyProperty ImagePixelHeightProperty = DependencyProperty.Register(
        nameof(ImagePixelHeight), typeof(double), typeof(CropOverlay),
        new FrameworkPropertyMetadata(1.0, OnGeometryChanged));

    public double ImagePixelHeight
    {
        get => (double)GetValue(ImagePixelHeightProperty);
        set => SetValue(ImagePixelHeightProperty, value);
    }

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(CropOverlay),
        new FrameworkPropertyMetadata(false, OnGeometryChanged));

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public static readonly DependencyProperty RotationAngleProperty = DependencyProperty.Register(
        nameof(RotationAngle), typeof(double), typeof(CropOverlay),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>Straighten angle in degrees, driven by dragging the area around the crop box.</summary>
    public double RotationAngle
    {
        get => (double)GetValue(RotationAngleProperty);
        set => SetValue(RotationAngleProperty, value);
    }

    private bool _rotating;
    private double _rotateStartPointerAngle;
    private double _rotateBaseValue;

    private void OnRotateDown(object sender, MouseButtonEventArgs e)
    {
        // Only the empty background (not the selection/handles) starts a rotate gesture.
        if (!IsActive || !ReferenceEquals(e.OriginalSource, this))
        {
            return;
        }
        _rotating = true;
        _rotateStartPointerAngle = PointerAngle(e.GetPosition(this));
        _rotateBaseValue = RotationAngle;
        CaptureMouse();
    }

    private void OnRotateMove(object sender, MouseEventArgs e)
    {
        if (!_rotating)
        {
            return;
        }
        double delta = PointerAngle(e.GetPosition(this)) - _rotateStartPointerAngle;
        double next = Math.Clamp(_rotateBaseValue + delta, -45, 45);
        SetCurrentValue(RotationAngleProperty, Math.Round(next, 1));
    }

    private void OnRotateUp(object sender, MouseButtonEventArgs e)
    {
        _rotating = false;
        ReleaseMouseCapture();
    }

    private double PointerAngle(Point p)
    {
        var center = new Point(_selPx.X + (_selPx.Width / 2), _selPx.Y + (_selPx.Height / 2));
        return Math.Atan2(p.Y - center.Y, p.X - center.X) * 180.0 / Math.PI;
    }

    private static void OnCropChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((CropOverlay)d).UpdateVisual();

    private static void OnGeometryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((CropOverlay)d).UpdateVisual();

    /// <summary>The letterboxed rectangle (control coordinates) the image actually occupies.</summary>
    private Rect DisplayedRect()
    {
        double cw = ActualWidth, ch = ActualHeight;
        if (cw <= 0 || ch <= 0 || ImagePixelWidth <= 0 || ImagePixelHeight <= 0)
        {
            return Rect.Empty;
        }

        double aspectImg = ImagePixelWidth / ImagePixelHeight;
        double aspectCtrl = cw / ch;
        double dispW, dispH;
        if (aspectImg > aspectCtrl)
        {
            dispW = cw;
            dispH = cw / aspectImg;
        }
        else
        {
            dispH = ch;
            dispW = ch * aspectImg;
        }
        return new Rect((cw - dispW) / 2, (ch - dispH) / 2, dispW, dispH);
    }

    private void UpdateVisual()
    {
        bool show = IsActive;
        IsHitTestVisible = show;
        _selection.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        foreach (var t in _corners)
        {
            t.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }
        if (!show)
        {
            return;
        }

        var disp = DisplayedRect();
        if (disp.IsEmpty)
        {
            return;
        }

        var c = CropRect.Clamped();
        _selPx = new Rect(
            disp.X + (c.X * disp.Width),
            disp.Y + (c.Y * disp.Height),
            c.Width * disp.Width,
            c.Height * disp.Height);
        LayoutSelection();
    }

    private void LayoutSelection()
    {
        SetLeft(_selection, _selPx.X);
        SetTop(_selection, _selPx.Y);
        _selection.Width = Math.Max(1, _selPx.Width);
        _selection.Height = Math.Max(1, _selPx.Height);

        PlaceCorner(0, _selPx.Left, _selPx.Top);
        PlaceCorner(1, _selPx.Right, _selPx.Top);
        PlaceCorner(2, _selPx.Left, _selPx.Bottom);
        PlaceCorner(3, _selPx.Right, _selPx.Bottom);
    }

    private void PlaceCorner(int i, double x, double y)
    {
        SetLeft(_corners[i], x - (_corners[i].Width / 2));
        SetTop(_corners[i], y - (_corners[i].Height / 2));
    }

    private void OnCornerDrag(object sender, DragDeltaEventArgs e)
    {
        var disp = DisplayedRect();
        if (disp.IsEmpty)
        {
            return;
        }

        int corner = (int)((Thumb)sender).Tag;
        double left = _selPx.Left, top = _selPx.Top, right = _selPx.Right, bottom = _selPx.Bottom;
        switch (corner)
        {
            case 0: left += e.HorizontalChange; top += e.VerticalChange; break;
            case 1: right += e.HorizontalChange; top += e.VerticalChange; break;
            case 2: left += e.HorizontalChange; bottom += e.VerticalChange; break;
            default: right += e.HorizontalChange; bottom += e.VerticalChange; break;
        }

        const double minSize = 12;
        left = Math.Clamp(left, disp.Left, right - minSize);
        top = Math.Clamp(top, disp.Top, bottom - minSize);
        right = Math.Clamp(right, left + minSize, disp.Right);
        bottom = Math.Clamp(bottom, top + minSize, disp.Bottom);

        _selPx = new Rect(left, top, right - left, bottom - top);
        LayoutSelection();
        CommitToCrop(disp);
    }

    private Point _dragStart;
    private bool _dragging;

    private void OnBodyDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _dragStart = e.GetPosition(this);
        _selection.CaptureMouse();
    }

    private void OnBodyMove(object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }
        var disp = DisplayedRect();
        if (disp.IsEmpty)
        {
            return;
        }

        var pos = e.GetPosition(this);
        double dx = pos.X - _dragStart.X, dy = pos.Y - _dragStart.Y;
        _dragStart = pos;

        double left = Math.Clamp(_selPx.Left + dx, disp.Left, disp.Right - _selPx.Width);
        double top = Math.Clamp(_selPx.Top + dy, disp.Top, disp.Bottom - _selPx.Height);
        _selPx = new Rect(left, top, _selPx.Width, _selPx.Height);
        LayoutSelection();
        CommitToCrop(disp);
    }

    private void OnBodyUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        _selection.ReleaseMouseCapture();
    }

    private void CommitToCrop(Rect disp)
    {
        var norm = new NormalizedRect(
            (_selPx.X - disp.X) / disp.Width,
            (_selPx.Y - disp.Y) / disp.Height,
            _selPx.Width / disp.Width,
            _selPx.Height / disp.Height).Clamped();
        SetCurrentValue(CropRectProperty, norm);
    }

    private static ControlTemplate HandleTemplate()
    {
        var factory = new FrameworkElementFactory(typeof(Ellipse));
        factory.SetValue(Shape.FillProperty, Brushes.White);
        factory.SetValue(Shape.StrokeProperty, new SolidColorBrush(Color.FromRgb(0x4F, 0x8C, 0xFF)));
        factory.SetValue(Shape.StrokeThicknessProperty, 2.0);
        return new ControlTemplate(typeof(Thumb)) { VisualTree = factory };
    }
}

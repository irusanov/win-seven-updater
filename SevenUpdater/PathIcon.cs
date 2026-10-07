using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace SevenUpdater
{
    /// <summary>
    /// Draws a vector icon from a <see cref="Geometry"/> resource (see Themes/Icons.xaml), e.g.
    /// <c>&lt;local:PathIcon Data="{StaticResource IconDvd}" Width="20" Height="20" /&gt;</c>.
    /// The geometry is laid out in a <see cref="ViewBox"/> (24×24 by default, like Material Design Icons), so
    /// icons keep their built-in padding and line up with each other. It is filled with <see cref="Foreground"/>,
    /// which is inherited, so icons follow the text color of the surrounding control and the current theme.
    /// </summary>
    public class PathIcon : FrameworkElement
    {
        public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
            nameof(Data),
            typeof(Geometry),
            typeof(PathIcon),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ViewBoxProperty = DependencyProperty.Register(
            nameof(ViewBox),
            typeof(Rect),
            typeof(PathIcon),
            new FrameworkPropertyMetadata(new Rect(0, 0, 24, 24), FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
            typeof(PathIcon),
            new FrameworkPropertyMetadata(SystemColors.ControlTextBrush, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

        public Geometry Data
        {
            get => (Geometry)GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        /// <summary>Coordinate space of <see cref="Data"/>; defaults to 0,0,24,24.</summary>
        public Rect ViewBox
        {
            get => (Rect)GetValue(ViewBoxProperty);
            set => SetValue(ViewBoxProperty, value);
        }

        public Brush Foreground
        {
            get => (Brush)GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            Rect viewBox = ViewBox;
            if (viewBox.Width <= 0 || viewBox.Height <= 0)
            {
                return new Size(0, 0);
            }

            double scale = double.PositiveInfinity;
            if (!double.IsInfinity(availableSize.Width))
            {
                scale = Math.Min(scale, availableSize.Width / viewBox.Width);
            }
            if (!double.IsInfinity(availableSize.Height))
            {
                scale = Math.Min(scale, availableSize.Height / viewBox.Height);
            }
            if (double.IsInfinity(scale))
            {
                scale = 1;
            }

            return new Size(viewBox.Width * scale, viewBox.Height * scale);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            Geometry data = Data;
            Rect viewBox = ViewBox;
            if (data == null || Foreground == null || viewBox.Width <= 0 || viewBox.Height <= 0)
            {
                return;
            }

            double scale = Math.Min(RenderSize.Width / viewBox.Width, RenderSize.Height / viewBox.Height);
            if (double.IsNaN(scale) || scale <= 0)
            {
                return;
            }

            double offsetX = (RenderSize.Width - viewBox.Width * scale) / 2 - viewBox.X * scale;
            double offsetY = (RenderSize.Height - viewBox.Height * scale) / 2 - viewBox.Y * scale;

            drawingContext.PushTransform(new MatrixTransform(scale, 0, 0, scale, offsetX, offsetY));
            drawingContext.DrawGeometry(Foreground, null, data);
            drawingContext.Pop();
        }
    }
}

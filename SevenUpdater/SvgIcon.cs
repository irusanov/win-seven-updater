using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Xml.Linq;

namespace SevenUpdater
{
    /// <summary>
    /// Draws a single-color SVG icon from an embedded resource (Build Action: Resource), e.g.
    /// <c>&lt;local:SvgIcon Source="/Assets/Icons/dvd.svg" Width="20" Height="20" /&gt;</c>.
    /// Supports the subset used by icon sets such as Material Design Icons: a viewBox and &lt;path d="..."&gt;
    /// elements (honoring fill-rule). The icon is painted with <see cref="Foreground"/>, which is inherited,
    /// so it follows the text color of the surrounding control and the current theme.
    /// </summary>
    public class SvgIcon : FrameworkElement
    {
        private static readonly Dictionary<string, SvgData> Cache = new Dictionary<string, SvgData>(StringComparer.OrdinalIgnoreCase);

        public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
            nameof(Source),
            typeof(string),
            typeof(SvgIcon),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnSourceChanged));

        public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
            typeof(SvgIcon),
            new FrameworkPropertyMetadata(SystemColors.ControlTextBrush, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

        private SvgData _data;

        /// <summary>Resource path ("/Assets/Icons/x.svg") or full pack URI of the SVG file.</summary>
        public string Source
        {
            get => (string)GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        public Brush Foreground
        {
            get => (Brush)GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((SvgIcon)d)._data = Load(e.NewValue as string);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            if (_data == null)
            {
                return new Size(0, 0);
            }

            Rect viewBox = _data.ViewBox;
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
            if (_data == null || Foreground == null)
            {
                return;
            }

            Rect viewBox = _data.ViewBox;
            double scale = Math.Min(RenderSize.Width / viewBox.Width, RenderSize.Height / viewBox.Height);
            if (double.IsNaN(scale) || scale <= 0)
            {
                return;
            }

            double offsetX = (RenderSize.Width - viewBox.Width * scale) / 2 - viewBox.X * scale;
            double offsetY = (RenderSize.Height - viewBox.Height * scale) / 2 - viewBox.Y * scale;

            drawingContext.PushTransform(new MatrixTransform(scale, 0, 0, scale, offsetX, offsetY));
            drawingContext.DrawGeometry(Foreground, null, _data.Geometry);
            drawingContext.Pop();
        }

        private static SvgData Load(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return null;
            }

            lock (Cache)
            {
                SvgData cached;
                if (Cache.TryGetValue(source, out cached))
                {
                    return cached;
                }

                SvgData data = null;
                try
                {
                    data = Parse(source);
                }
                catch (Exception ex)
                {
                    // A broken icon must never take the window down (also keeps the designer working).
                    System.Diagnostics.Debug.WriteLine($"SvgIcon: could not load '{source}': {ex.Message}");
                }

                Cache[source] = data;
                return data;
            }
        }

        private static SvgData Parse(string source)
        {
            Uri uri = source.StartsWith("pack://", StringComparison.OrdinalIgnoreCase)
                ? new Uri(source, UriKind.Absolute)
                : new Uri("pack://application:,,,/" + source.Replace('\\', '/').TrimStart('/'), UriKind.Absolute);

            var resource = Application.GetResourceStream(uri);
            if (resource == null)
            {
                return null;
            }

            XDocument document;
            using (resource.Stream)
            {
                document = XDocument.Load(resource.Stream);
            }

            XElement root = document.Root;
            if (root == null)
            {
                return null;
            }

            Rect viewBox = ParseViewBox(root);

            var group = new GeometryGroup { FillRule = FillRule.Nonzero };
            foreach (XElement path in root.Descendants().Where(e => e.Name.LocalName == "path"))
            {
                string data = (string)path.Attribute("d");
                if (string.IsNullOrWhiteSpace(data))
                {
                    continue;
                }

                // SVG's default fill rule is nonzero; WPF's path markup defaults to even-odd unless prefixed.
                string fillRule = (string)path.Attribute("fill-rule") ?? (string)root.Attribute("fill-rule");
                string prefix = string.Equals(fillRule, "evenodd", StringComparison.OrdinalIgnoreCase) ? "F0 " : "F1 ";
                group.Children.Add(Geometry.Parse(prefix + data));
            }

            group.Freeze();
            return new SvgData(viewBox, group);
        }

        private static Rect ParseViewBox(XElement root)
        {
            string viewBox = (string)root.Attribute("viewBox");
            if (!string.IsNullOrWhiteSpace(viewBox))
            {
                double[] parts = viewBox
                    .Split(new[] { ' ', ',', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => double.Parse(p, NumberStyles.Float, CultureInfo.InvariantCulture))
                    .ToArray();
                if (parts.Length == 4 && parts[2] > 0 && parts[3] > 0)
                {
                    return new Rect(parts[0], parts[1], parts[2], parts[3]);
                }
            }

            double width = ParseLength((string)root.Attribute("width"));
            double height = ParseLength((string)root.Attribute("height"));
            return new Rect(0, 0, width > 0 ? width : 24, height > 0 ? height : 24);
        }

        private static double ParseLength(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0;
            }

            double result;
            string number = new string(value.TakeWhile(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
            return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ? result : 0;
        }

        private sealed class SvgData
        {
            public SvgData(Rect viewBox, Geometry geometry)
            {
                ViewBox = viewBox;
                Geometry = geometry;
            }

            public Rect ViewBox { get; }
            public Geometry Geometry { get; }
        }
    }
}

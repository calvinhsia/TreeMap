using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System.Collections.Concurrent;
using System.Linq;

namespace TreeMap;

public static class TreemapPort
{
    // Recursive slice-and-dice treemap. Alternates horizontal/vertical splits.
    public static void MakeTreemap(ConcurrentDictionary<string, MapDataItem> dict, Canvas canvas, string parentPath, Rect parentRect, long parentTotalSize, bool horizontal = true)
    {
        var parentDepth = dict.ContainsKey(parentPath) ? dict[parentPath].Depth : parentPath.Count(c => c == TreeMapConstants.PathSep);
        var childKeys = dict.Keys.Where(k => k.StartsWith(parentPath) && dict[k].Depth == parentDepth + 1).OrderByDescending(k => dict[k].Size).ToList();
        double x = parentRect.X;
        double y = parentRect.Y;
        double w = parentRect.Width;
        double h = parentRect.Height;

        var total = (double)parentTotalSize;
        double offset = 0;

        for (int i = 0; i < childKeys.Count; i++)
        {
            var key = childKeys[i];
            var size = dict[key].Size;
            double fraction = total == 0 ? 0 : (double)size / total;

            Rect r;
            if (horizontal)
            {
                var rw = w * fraction;
                r = new Rect(x + offset, y, rw, h);
                offset += rw;
            }
            else
            {
                var rh = h * fraction;
                r = new Rect(x, y + offset, w, rh);
                offset += rh;
            }

            // create rectangle shape
            var fillColor = Avalonia.Media.Color.FromArgb(0xFF, (byte)((i * 97) % 255), (byte)((size / 7) % 255), (byte)(((i + 3) * 59) % 255));
            var fillBrush = new SolidColorBrush(fillColor);
            var rectW = r.Width < 0 ? 0 : r.Width;
            var rectH = r.Height < 0 ? 0 : r.Height;

            // Check if this item contains cloud-only files
            var isCloudItem = dict.ContainsKey(key) && dict[key].IsCloudOnly;
            var strokeBrush = isCloudItem ? Brushes.Cyan : Brushes.Black;
            var strokeThickness = isCloudItem ? 3.0 : 1.0;

            var rect = new Avalonia.Controls.Shapes.Rectangle
            {
                Fill = fillBrush,
                Width = rectW,
                Height = rectH,
                Stroke = strokeBrush,
                StrokeThickness = strokeThickness
            };
            rect.DataContext = key;
            // Tooltip shows full path and size (and cloud status)
            var sizeStr = size >= 1_000_000_000 ? $"{size / 1_000_000_000.0:F2} GB" :
                          size >= 1_000_000 ? $"{size / 1_000_000.0:F2} MB" :
                          size >= 1_000 ? $"{size / 1_000.0:F2} KB" : $"{size} bytes";
            var cloudInfo = isCloudItem ? $"\n☁ Contains {dict[key].CloudFileCount} cloud file(s)" : "";
            ToolTip.SetTip(rect, $"{key}\n{sizeStr}{cloudInfo}");
            rect.PointerPressed += (s, e) =>
            {
                // on click, just redraw treemap for this node (drill down)
                canvas.Children.Clear();
                long childTotal = dict.ContainsKey(key) ? dict[key].Size : size;
                MakeTreemap(dict, canvas, key, new Rect(0, 0, canvas.Bounds.Width, canvas.Bounds.Height), childTotal, !horizontal);
                e.Handled = true;
            };

            Canvas.SetLeft(rect, r.X);
            Canvas.SetTop(rect, r.Y);
            canvas.Children.Add(rect);

            // Add text label - show full path like WPF version
            // Use vertical text for tall narrow rectangles
            if (r.Width > 20 && r.Height > 14)
            {
                var txt = new TextBlock
                { 
                    Text = key, // Full path
                    Foreground = Brushes.Black,
                    TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
                };
                txt.DataContext = key;
                
                // Rotate text 90 degrees for tall narrow rectangles (height > width)
                if (r.Height > r.Width * 1.5 && r.Height > 60)
                {
                    txt.RenderTransform = new RotateTransform(90);
                    txt.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
                    txt.MaxWidth = r.Height - 8; // Use height as max width since rotated
                    Canvas.SetLeft(txt, r.X + 14);
                    Canvas.SetTop(txt, r.Y + 4);
                }
                else
                {
                    txt.MaxWidth = r.Width - 8;
                    Canvas.SetLeft(txt, r.X + 4);
                    Canvas.SetTop(txt, r.Y + 4);
                }
                canvas.Children.Add(txt);
            }

            // recurse into children if present and rectangle is large enough
            var hasChildren = dict.Keys.Any(k => k.StartsWith(key) && dict[k].Depth == dict[key].Depth + 1);
            if (hasChildren && (r.Width > 40 && r.Height > 20))
            {
                MakeTreemap(dict, canvas, key, r, dict[key].Size, !horizontal);
            }
        }
    }
}

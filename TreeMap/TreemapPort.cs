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
            var rect = new Avalonia.Controls.Shapes.Rectangle
            {
                Fill = fillBrush,
                Width = rectW,
                Height = rectH,
                Stroke = Brushes.Black,
                StrokeThickness = 1
            };
            rect.DataContext = key;
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

            if (r.Width > 30 && r.Height > 16)
            {
                var txt = new TextBlock { Text = System.IO.Path.GetFileName(key.TrimEnd(TreeMapConstants.PathSep)), Foreground = Brushes.Black };
                txt.DataContext = key;
                Canvas.SetLeft(txt, r.X + 4);
                Canvas.SetTop(txt, r.Y + 4);
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

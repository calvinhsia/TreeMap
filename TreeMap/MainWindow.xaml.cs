using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.Objects.DataClasses;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace TreeMap
{
    public partial class MainWindow : Window, System.Windows.Forms.IWin32Window
    {
        public static char PathSep = System.IO.Path.DirectorySeparatorChar;
        public static string DataSuffix = "*" + PathSep; // a node whose size consists of files in the node (excluding children)
        public TextBlock _txtStatus;
        public string _rootPath;
        public Rect _rootRect;
        public long _rootSize;
        public ConcurrentDictionary<string, MapDataItem> _DataDict = new ConcurrentDictionary<string, MapDataItem>();

        public MainWindow()
        {
            InitializeComponent();
            this.WindowState = WindowState.Maximized;
            _txtStatus = new TextBlock();
            Title = "Treemap by Calvin Hsia";
            this.Content = _txtStatus;
            this.Loaded += async (object o, RoutedEventArgs e) =>
            { // run this after the window has been initialized:

                var args = Environment.GetCommandLineArgs(); // 1st is fullpath to exe
                if (args.Length > 1 && Directory.Exists(args[1]))
                {
                    _rootPath = args[1];
                }
                else
                {
                    var fldrDialog = new System.Windows.Forms.FolderBrowserDialog();
                    fldrDialog.Description = @"Choose a root folder. A small subtree will be faster, like c:\Program Files";
                    fldrDialog.SelectedPath = Environment.CurrentDirectory;
                    if (fldrDialog.ShowDialog(this) != System.Windows.Forms.DialogResult.OK)
                    {
                        Application.Current.Shutdown();
                    }
                    _rootPath = fldrDialog.SelectedPath;
                }
                if (!_rootPath.EndsWith(MainWindow.PathSep.ToString()))
                {
                    _rootPath += PathSep;// need a trailing backslash to distinguish dir name matches
                }
                var startTime = DateTime.Now;
                _rootSize = await FillDictionaryAsync(_rootPath);
                var totNumFiles = _DataDict.Values.Sum(x => x.NumFiles);
                this.Title += string.Format(" {0} Size = {1:n0}  # Files={2:n0}  # Items = {3:n0}", _rootPath, _rootSize, totNumFiles, _DataDict.Count()); ;
                _rootRect = new Rect(0, 0,
                    this.ActualWidth,
                    this.ActualHeight);
                this.Content = new TreeMap(this, _rootPath, _rootSize);
                var elapsed = DateTime.Now - startTime;
                this.Title += $" Calculated in {elapsed.TotalMinutes:n1} minutes";
            };
        }

        public class MapDataItem
        {
            public int Depth; // # of "\" in path
            public long Size; // # of bytes
            public int NumFiles; //# of files in curdir only
            public int Index; // the order in which the item was encountered
            public Rect rect;
            public override string ToString()
            {
                return string.Format("Depth = {0} Size = {1:n0}, NumFiles = {2:n0} Index = {3:n0}", Depth, Size, NumFiles, Index);
            }
        }
        public async Task<long> FillDictionaryAsync(string cPath) // includes trailing "\"
        { // runs on background thread
            long totalSize = 0;
            long curdirFileSize = 0;
            long curdirFolderSize = 0;
            await Task.Run(async () =>
            {
                try
                {
                    if (_DataDict.Count() % 100 == 0)
                    { // upgrade status on foreground thread
                        _txtStatus.Dispatcher.Invoke(
                            DispatcherPriority.Normal,
                            new Action<TextBlock>(otxtblk =>
                            {
                                otxtblk.Text = cPath; // update status
                            }
                                ),
                            _txtStatus);
                    }
                    var dirInfo = new DirectoryInfo(cPath);
                    if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                    { // some folders are not really there: they're redirect junction points.

                        return;
                    }
                    var nDepth = cPath.Where(c => c == PathSep).Count();
                    var curDirFiles = Directory.GetFiles(cPath);
                    if (curDirFiles.Length > 0) // if cur folder contains any files (not dirs)
                    {
                        foreach (var file in curDirFiles)
                        {
                            /*
C:\Users\calvinh\AppData\Local\Packages\WINSTO~1\LOCALS~1\Cache\0\0-DevApps-https∺∯∯next-services.apps.microsoft.com∯search∯6.3.9600-0∯788∯en-US_en-US∯c∯US∯cp∯10005001∯DevApps∯pc∯0∯pt∯x64∯af∯0∯lf∯1∯pn∯1∿developerName=Microsoft%20Corporation.dat
C:\Users\calvinh\AppData\Local\Packages\winstore_cw5n1h2txyewy\LocalState\Cache\0\0-DevApps-https∺∯∯next-services.apps.microsoft.com∯search∯6.3.9600-0∯788∯en-US_en-US∯c∯US∯cp∯10005001∯DevApps∯pc∯0∯pt∯x64∯af∯0∯lf∯1∯pn∯1∿developerName=Microsoft%20Corporation.dat
                              */
                            try
                            {
                                var finfo = new FileInfo(file);
                                curdirFileSize += finfo.Length;
                            }
                            catch (PathTooLongException)
                            {
                            }
                        }
                        _DataDict[cPath + DataSuffix] = // size of files in cur folder, excluding children
                             new MapDataItem()
                             {
                                 Depth = nDepth + 1,
                                 Size = curdirFileSize,
                                 NumFiles = curDirFiles.Length,
                                 Index = _DataDict.Count
                             };
                    }
                    var curDirFolders = Directory.GetDirectories(cPath); // now any subfolders
                    if (curDirFolders.Length > 0)
                    {
                        foreach (var dir in curDirFolders)
                        {
                            curdirFolderSize += await FillDictionaryAsync(System.IO.Path.Combine(cPath, dir) + PathSep); // recur
                        }
                    }
                    totalSize += curdirFileSize + curdirFolderSize;
                    _DataDict[cPath] = new MapDataItem() { Depth = nDepth, Size = curdirFileSize + curdirFolderSize, Index = _DataDict.Count };
                }
                catch (PathTooLongException)
                {

                }
                catch (Exception ex)
                {
                    if (ex is UnauthorizedAccessException)
                    {
                        System.Diagnostics.Trace.WriteLine("Ex: " + ex.Message);
                    }
                    else
                    {
                        throw;
                    }
                }

            });
            return totalSize;
        }

        public class TreeMap : Canvas
        {
            internal MainWindow _mainWindow;
            internal string _rootPath; // root for this canvas
            internal long _rootSize; // size for this root
            public int _EvenOdd = 0; // even or odd determines horiz or vert first
            public TreeMap(MainWindow mainWindow, string rootPath, long rootSize)
            {
                _mainWindow = mainWindow;
                _rootPath = rootPath;
                _rootSize = rootSize;
                MakeTreeMap(_rootPath, mainWindow._rootRect, rootSize);
            }
            public void MakeTreeMap(string parentPath, Rect parentRect, long parentTotalSize)
            {
                var nCurDepth = parentPath.Where(c => c == MainWindow.PathSep).Count(); // count the # of "\"
                var querySubDirs = from subPath in _mainWindow._DataDict.Keys
                                   where subPath.StartsWith(parentPath)
                                        && _mainWindow._DataDict[subPath].Depth == nCurDepth + 1    // we want those 1 level deeper
                                   orderby _mainWindow._DataDict[subPath].Size descending
                                   select subPath;

                long nRunTot = 0;
                foreach (var subDir in querySubDirs)
                {
                    var curSize = _mainWindow._DataDict[subDir].Size;
                    Rect newRectStruct;
                    if (nCurDepth % 2 == _EvenOdd) // even or odd?
                    {
                        newRectStruct = new Rect(
                            parentRect.Left + parentRect.Width * nRunTot / parentTotalSize,
                            parentRect.Top,
                            parentRect.Width * curSize / parentTotalSize,
                            parentRect.Height
                        );
                    }
                    else
                    {
                        newRectStruct = new Rect(
                            parentRect.Left,
                            parentRect.Top + parentRect.Height * nRunTot / parentTotalSize,
                            parentRect.Width,
                            parentRect.Height * curSize / parentTotalSize
                        );
                    }
                    nRunTot += curSize;
                    var data = _mainWindow._DataDict[subDir];
                    var rectMapItem = new TreeMapItem(
                        subDir,
                        string.Format("{0} Files ={1:n0} Size ={2:n0} Index = {3:n0} ({4:n0},{5:n0})",
                            subDir, data.NumFiles, curSize, data.Index, newRectStruct.Width, newRectStruct.Height),
                        newRectStruct,
                        nCurDepth,
                        this
                        );
                    this.Children.Add(rectMapItem);
                    if (newRectStruct.Width > 5 && newRectStruct.Height > 5)
                    { // if it's big enough to drill down, figure out the next level down.
                        var newq = from k in _mainWindow._DataDict.Keys
                                   where k.StartsWith(subDir) && k.LastIndexOf("*") < 0
                                        && _mainWindow._DataDict[k].Depth >= nCurDepth + 1
                                   orderby _mainWindow._DataDict[k].Size descending
                                   select k;
                        var newParent = newq.FirstOrDefault();
                        if (!string.IsNullOrEmpty(newParent))
                        {
                            MakeTreeMap(newParent, newRectStruct, curSize); //recur
                        }
                    }
                }
            }
            internal class TreeMapItem : TextBlock
            {
                internal static uint curColor = 0xffffff;
                internal TreeMap _treeMap;
                public TreeMapItem(string subDir, string toolTip, Rect newRectStruct, int nDepth, TreeMap treeMap)
                {
                    _treeMap = treeMap;
                    var newColor = Color.FromArgb(
                                    (byte)(0xff), //opaque
                                    (byte)(curColor & 0xff), //red
                                    (byte)((curColor >> 4) & 0xff),//green
                                    (byte)((curColor >> 8) & 0xff) //blue
                                    );
                    curColor -= 100; // change the color some way
                    Background = new SolidColorBrush(newColor);
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                    VerticalAlignment = System.Windows.VerticalAlignment.Top;
                    if (newRectStruct.Height > newRectStruct.Width)
                    { // let's rotate the text for tall skinny rects
                        var trans = new RotateTransform(-90);
                        this.LayoutTransform = trans;
                        Height = newRectStruct.Width;
                        Width = newRectStruct.Height;
                    }
                    else
                    {
                        Height = newRectStruct.Height;
                        Width = newRectStruct.Width;
                    }
                    treeMap._mainWindow._DataDict[subDir].rect = newRectStruct;
                    Canvas.SetTop(this, newRectStruct.Top);
                    Canvas.SetLeft(this, newRectStruct.Left);
                    Text = subDir;
                    ToolTip = toolTip;
                    this.ContextMenu = _ContextMenu; // all share the same menu
                }
                public override string ToString()
                {
                    return string.Format("({0}) {1}", _treeMap._mainWindow._DataDict[Text].rect, Text);
                }
            }
            private static ContextMenu __ContextMenu;
            public static ContextMenu _ContextMenu
            {
                get
                {
                    if (__ContextMenu == null)
                    { // create the menu only once
                        __ContextMenu = new ContextMenu();
                        RoutedEventHandler menuItemHandler = (object o, RoutedEventArgs e) =>
                        {
                            var curMapItem = _ContextMenu.PlacementTarget as TreeMapItem;
                            var subDir = curMapItem.Text;
                            var treeMap = GetAncestor<TreeMap>(curMapItem);
                            var curWin = GetAncestor<Window>(treeMap);
                            curWin.Cursor = Cursors.Wait;// hourglass
                            bool fResetCursorDelay = true; // do we wait til rendering is done to turn off hourglass?
                            e.Handled = true;
                            try
                            {
                                switch (((MenuItem)o).Header.ToString())
                                {
                                    case "_Explorer":
                                        var ndx = subDir.IndexOf("*");
                                        if (ndx > 0)
                                        {
                                            subDir = subDir.Substring(0, ndx - 1);
                                        }
                                        System.Diagnostics.Process.Start(subDir);
                                        break;
                                    case "_SubTreeMap":
                                        {
                                            var winMain = treeMap._mainWindow;
                                            var newWin = new Window();
                                            newWin.WindowState = WindowState.Maximized;
                                            newWin.Height = winMain.ActualHeight;
                                            newWin.Width = winMain.ActualWidth;
                                            var winRect = winMain._rootRect;
                                            var newDepth = curMapItem._treeMap._rootPath.Where(c => c == PathSep).Count() + 1;
                                            var newPath = curMapItem.Text;
                                            // this is drilled in many levels: we only want to drill in 1
                                            var split = newPath.Split(new[] { PathSep }); // create an array of path pieces
                                            if (newDepth <= split.Length)
                                            {
                                                var joinedPath = String.Join(PathSep.ToString(), split, 0, newDepth) +
                                                    PathSep;
                                                var newSize = curMapItem._treeMap._mainWindow._DataDict[joinedPath].Size;
                                                newWin.Title = string.Format("{0} {1:n0}", joinedPath, newSize);
                                                newWin.Content = new TreeMap(winMain, joinedPath, newSize);
                                                newWin.Show();
                                            }
                                        }
                                        break;
                                    case "_New TreeMap Root":
                                        {
                                            var newRootWin = new MainWindow();
                                            newRootWin._rootPath = treeMap._rootPath; // use same root path as initial default
                                            newRootWin.Show();
                                        }
                                        break;
                                    case "_Flip Horizontal/Vertical":
                                        treeMap._EvenOdd = 1 - treeMap._EvenOdd;
                                        goto case "_ReColor";
                                    case "_ReColor":
                                        treeMap.Children.Clear();
                                        treeMap.MakeTreeMap(treeMap._rootPath, treeMap._mainWindow._rootRect, treeMap._rootSize);
                                        break;
                                    case "_Browse Data":
                                        {
                                            var win = new Window();
                                            var query = from dat in treeMap._mainWindow._DataDict
                                                        select new
                                                        {
                                                            dat.Value.Index,
                                                            dat.Key,
                                                            dat.Value.Size,
                                                            dat.Value.Depth,
                                                            dat.Value.NumFiles
                                                        };
                                            win.Content = new Browse(query);
                                            win.WindowState = WindowState.Maximized;
                                            win.Title = string.Format("Treemap Data # items = {0:n0} ", query.Count());
                                            win.Show();
                                        }
                                        break;
                                    case "_Quit":
                                        Application.Current.Shutdown();
                                        break;
                                }
                            }
                            catch (Exception)
                            {
//                                throw;
                            }
                            finally
                            {
                                if (fResetCursorDelay)
                                { // we want to wait til after render by synchronously running low pri empty code 
                                    curWin.Dispatcher.Invoke(DispatcherPriority.Render, EmptyDelegate);
                                    {
                                        curWin.Cursor = Cursors.Arrow;
                                    };
                                }
                                else
                                {
                                    curWin.Cursor = Cursors.Arrow;
                                }
                            }
                        };
                        __ContextMenu.AddMenuItem(menuItemHandler, "_SubTreeMap", "Create a new subtree map");
                        __ContextMenu.AddMenuItem(menuItemHandler, "_Explorer", "Open Windows Explorer");
                        __ContextMenu.AddMenuItem(menuItemHandler, "_New TreeMap Root", "Choose a new root folder");
                        __ContextMenu.AddMenuItem(menuItemHandler, "_Flip Horizontal/Vertical", "reflect through line x==y");
                        __ContextMenu.AddMenuItem(menuItemHandler, "_ReColor", "ReDraw with different colors");
                        __ContextMenu.AddMenuItem(menuItemHandler, "_Browse Data", "show the raw disk data in a grid");
                        __ContextMenu.AddMenuItem(menuItemHandler, "_Quit", "exit program");
                    }
                    return __ContextMenu;
                }
            }

        }

        public static Action EmptyDelegate = () => { };
        public static T GetAncestor<T>(DependencyObject element) where T : DependencyObject
        {
            while (element != null && !(element is T))
            {
                element = VisualTreeHelper.GetParent(element);
            }
            return (T)element;
        }

        public IntPtr Handle
        {//System.Windows.Forms.IWin32Window for parent window of FolderBrowserDialog
            get
            {
                IntPtr hndle = ((System.Windows.Interop.HwndSource)PresentationSource.FromVisual(this)).Handle;
                return hndle;
            }
        }
    }

    public static class MyExtensions
    {
        public static MenuItem AddMenuItem(this ContextMenu menu, RoutedEventHandler handler, string menuItemContent, string tooltip)
        {
            var newItem = new MenuItem()
            {
                Header = menuItemContent,
                ToolTip = tooltip
            };
            newItem.Click += handler;
            menu.Items.Add(newItem);
            return newItem;
        }
    }
    // This Browse class is identical to prior post: Write your own Linq Query Viewer http://blogs.msdn.com/b/calvin_hsia/archive/2010/12/30/10110463.aspx
    public class Browse : ListView
    {
        public Browse(IEnumerable query)
        {
            this.Margin = new System.Windows.Thickness(8);
            this.ItemsSource = query;
            var gridvw = new GridView();
            this.View = gridvw;
            var ienum = query.GetType().GetInterface(typeof(IEnumerable<>).FullName);

            var members = ienum.GetGenericArguments()[0].GetMembers().Where(m => m.MemberType == System.Reflection.MemberTypes.Property);
            foreach (var mbr in members)
            {
                if (mbr.DeclaringType == typeof(EntityObject)) // if using Entity framework, filter out EntityKey, etc.
                {
                    continue;
                }
                var gridcol = new GridViewColumn();
                var colheader = new GridViewColumnHeader() { Content = mbr.Name };
                gridcol.Header = colheader;
                colheader.Click += new RoutedEventHandler(colheader_Click);
                gridvw.Columns.Add(gridcol);

                // now we make a dataTemplate with a Stackpanel containing a TextBlock
                // The template must create many instances, so factories are used.
                var dataTemplate = new DataTemplate();
                gridcol.CellTemplate = dataTemplate;
                var stackPanelFactory = new FrameworkElementFactory(typeof(StackPanel));
                stackPanelFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

                var txtBlkFactory = new FrameworkElementFactory(typeof(TextBlock));
                var binder = new Binding(mbr.Name)
                {
                    Converter = new MyValueConverter() // truncate things that are too long, add commas for numbers
                };
                txtBlkFactory.SetBinding(TextBlock.TextProperty, binder);
                stackPanelFactory.AppendChild(txtBlkFactory);
                txtBlkFactory.SetBinding(TextBlock.ToolTipProperty, new Binding(mbr.Name)); // the tip will have the non-truncated content

                txtBlkFactory.SetValue(TextBlock.FontFamilyProperty, new FontFamily("courier new"));
                txtBlkFactory.SetValue(TextBlock.FontSizeProperty, 10.0);

                dataTemplate.VisualTree = stackPanelFactory;
            }
            // now create a style for the items
            var style = new Style(typeof(ListViewItem));

            style.Setters.Add(new Setter(ForegroundProperty, Brushes.Blue));

            var trig = new Trigger()
            {
                Property = IsSelectedProperty,// if Selected, use a different color
                Value = true
            };
            trig.Setters.Add(new Setter(ForegroundProperty, Brushes.Red));
            trig.Setters.Add(new Setter(BackgroundProperty, Brushes.Cyan));
            style.Triggers.Add(trig);

            this.ItemContainerStyle = style;
        }

        private ListSortDirection _LastSortDir = ListSortDirection.Ascending;
        private GridViewColumnHeader _LastHeaderClicked = null;
        void colheader_Click(object sender, RoutedEventArgs e)
        {
            GridViewColumnHeader gvh = sender as GridViewColumnHeader;
            if (gvh != null)
            {
                var dir = ListSortDirection.Ascending;
                if (gvh == _LastHeaderClicked) // if clicking on already sorted col, reverse dir
                {
                    dir = 1 - _LastSortDir;
                }
                try
                {
                    var dataView = CollectionViewSource.GetDefaultView(this.ItemsSource);
                    dataView.SortDescriptions.Clear();

                    var sortDesc = new SortDescription(gvh.Content.ToString(), dir);
                    dataView.SortDescriptions.Add(sortDesc);
                    dataView.Refresh();
                    if (_LastHeaderClicked != null)
                    {
                        _LastHeaderClicked.Column.HeaderTemplate = null; // clear arrow of prior column
                    }
                    SetHeaderTemplate(gvh);
                    _LastHeaderClicked = gvh;
                    _LastSortDir = dir;
                }
                catch (Exception)
                {
                    // some types aren't sortable
                }
            }
        }

        void SetHeaderTemplate(GridViewColumnHeader gvh)
        {
            // now we'll create a header template that will show a little Up or Down indicator
            var hdrTemplate = new DataTemplate();
            var dockPanelFactory = new FrameworkElementFactory(typeof(DockPanel));
            var textBlockFactory = new FrameworkElementFactory(typeof(TextBlock));
            var binder = new Binding();
            binder.Source = gvh.Content; // the column name
            textBlockFactory.SetBinding(TextBlock.TextProperty, binder);
            textBlockFactory.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            dockPanelFactory.AppendChild(textBlockFactory);

            // a lot of code for a little arrow
            var pathFactory = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
            pathFactory.SetValue(System.Windows.Shapes.Path.FillProperty, Brushes.DarkGray);
            var pathGeometry = new PathGeometry();
            pathGeometry.Figures = new PathFigureCollection();
            var pathFigure = new PathFigure();
            pathFigure.Segments = new PathSegmentCollection();
            if (_LastSortDir != ListSortDirection.Ascending)
            {//"M 4,4 L 12,4 L 8,2"
                pathFigure.StartPoint = new Point(4, 4);
                pathFigure.Segments.Add(new LineSegment() { Point = new Point(12, 4) });
                pathFigure.Segments.Add(new LineSegment() { Point = new Point(8, 2) });
            }
            else
            {//"M 4,2 L 8,4 L 12,2"
                pathFigure.StartPoint = new Point(4, 2);
                pathFigure.Segments.Add(new LineSegment() { Point = new Point(8, 4) });
                pathFigure.Segments.Add(new LineSegment() { Point = new Point(12, 2) });
            }
            pathGeometry.Figures.Add(pathFigure);
            pathFactory.SetValue(System.Windows.Shapes.Path.DataProperty, pathGeometry);

            dockPanelFactory.AppendChild(pathFactory);
            hdrTemplate.VisualTree = dockPanelFactory;

            gvh.Column.HeaderTemplate = hdrTemplate;
        }
    }

    public class MyValueConverter : IValueConverter
    {
        private const int maxwidth = 700;
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (null != value)
            {
                Type type = value.GetType();
                //trim len of long strings. Doesn't work if type has ToString() override
                if (type == typeof(string))
                {
                    var str = value.ToString().Trim();
                    var ndx = str.IndexOfAny(new[] { '\r', '\n' });
                    var lenlimit = maxwidth;
                    if (ndx >= 0)
                    {
                        lenlimit = ndx - 1;
                    }
                    if (ndx >= 0 || str.Length > lenlimit)
                    {
                        value = str.Substring(0, lenlimit);
                    }
                    else
                    {
                        value = str;
                    }
                }
                else if (type == typeof(Int32))
                {
                    value = ((int)value).ToString("n0"); // Add commas, like 1,000,000
                }
                else if (type == typeof(Int64))
                {
                    value = ((Int64)value).ToString("n0"); // Add commas, like 1,000,000
                }
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

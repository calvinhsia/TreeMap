using System;
using Avalonia;
using Avalonia.Layout;
using System.Collections.Concurrent;

namespace TreeMap
{
    // Shared non-UI models moved out of MainWindow so they can be reused by scanner/tests.
    public static class TreeMapConstants
    {
        public static readonly char PathSep = System.IO.Path.DirectorySeparatorChar;
        public static readonly string DataSuffix = "*" + PathSep;
    }

    public class MapDataItem
    {
        public int Depth; // # of separators in path
        public long Size; // bytes
        public int NumFiles;
        public int Index;
        public Rect rect;
        public override string ToString() => $"Depth = {Depth} Size = {Size:n0}, NumFiles = {NumFiles:n0} Index = {Index:n0}";
    }
}

# TreeMap

A disk space visualization tool that displays directory sizes as a treemap, helping you quickly identify what's consuming your storage.

![TreeMap Icon](TreeMap/Assets/treemap-icon.png)

## Features

- **Treemap Visualization** - Hierarchical view of disk usage with proportionally-sized rectangles
- **Fast Scanning** - Asynchronous directory scanning with progress reporting
- **Cloud File Support** - Handles OneDrive/cloud placeholder files without triggering downloads
  - Show logical size (as if downloaded)
  - Exclude from size calculations
  - Show placeholder size (~1KB each)
- **Interactive Navigation** - Click to drill down, right-click context menu
- **File List View** - Toggle between treemap and sortable file list
- **MRU History** - Recently scanned paths remembered in dropdown

## Requirements

- Windows 10/11
- .NET 10 Runtime

## Building

```bash
dotnet build
```

## Running

```bash
dotnet run --project TreeMap
```

Or open `TreeMap.sln` in Visual Studio 2022.

## Usage

1. Enter a path or click **Browse** to select a folder
2. Click **Scan** to analyze disk usage
3. Use the treemap to visualize space usage - larger rectangles = more space
4. Right-click for options:
   - Open in Explorer
   - New window at selection
   - Swap horizontal/vertical orientation
   - Toggle between treemap and file list views

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TreeMap;

/// <summary>
/// Persists user settings like MRU paths to a JSON file in the user's app data folder.
/// </summary>
public class UserSettings
{
    private static readonly string SettingsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TreeMap");
    
    private static readonly string SettingsFile = Path.Combine(SettingsFolder, "settings.json");
    
    private const int MaxMruPaths = 10;
    
    /// <summary>
    /// Most recently used paths (most recent first)
    /// </summary>
    public List<string> MruPaths { get; set; } = new();
    
    /// <summary>
    /// Last selected cloud handling mode index
    /// </summary>
    public int CloudHandlingIndex { get; set; } = 0;
    
    /// <summary>
    /// Loads settings from disk, or returns default settings if file doesn't exist
    /// </summary>
    public static UserSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                var settings = JsonSerializer.Deserialize<UserSettings>(json);
                return settings ?? new UserSettings();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
        }
        return new UserSettings();
    }
    
    /// <summary>
    /// Saves settings to disk
    /// </summary>
    public void Save()
    {
        try
        {
            if (!Directory.Exists(SettingsFolder))
            {
                Directory.CreateDirectory(SettingsFolder);
            }
            
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Adds a path to the MRU list (moves to top if already exists)
    /// </summary>
    public void AddMruPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
            
        // Remove if already exists (we'll add at top)
        MruPaths.Remove(path);
        
        // Insert at beginning
        MruPaths.Insert(0, path);
        
        // Trim to max size
        while (MruPaths.Count > MaxMruPaths)
        {
            MruPaths.RemoveAt(MruPaths.Count - 1);
        }
    }
}

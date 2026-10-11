using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace JinxyClicker;

/// <summary>
/// Reads and writes Roblox's saved mouse sensitivity, which lives in
/// <c>%LOCALAPPDATA%\Roblox\GlobalBasicSettings_13.xml</c>. Roblox loads this on
/// launch and saves it on close, so a change takes effect next launch and a change
/// made while Roblox is running is overwritten when it closes.
/// </summary>
public static class RobloxSens
{
    public static string? SettingsFile()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Roblox", "GlobalBasicSettings_13.xml");
        return File.Exists(path) ? path : null;
    }

    /// <summary>The current saved sensitivity, or null if it can't be read.</summary>
    public static float? Read()
    {
        try
        {
            string? path = SettingsFile();
            if (path == null) return null;
            Match m = Regex.Match(File.ReadAllText(path),
                "<float name=\"MouseSensitivity\">([^<]*)</float>");
            return m.Success && float.TryParse(m.Groups[1].Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out float v) ? v : null;
        }
        catch { return null; }
    }

    /// <summary>Writes the sensitivity back. Returns false if the file isn't there.</summary>
    public static bool Write(float value)
    {
        string? path = SettingsFile();
        if (path == null) return false;
        File.WriteAllText(path, SetSensitivityInXml(File.ReadAllText(path), value));
        return true;
    }

    /// <summary>
    /// Pure XML edit: sets the general sensitivity and the first-person X/Y to the
    /// same value, leaving the rest of the file untouched. Exposed for testing.
    /// </summary>
    public static string SetSensitivityInXml(string xml, float value)
    {
        string v = value.ToString("R", CultureInfo.InvariantCulture);

        // Braced group refs so a value that starts with a digit can't glue onto the
        // reference (e.g. "$1" + "0.5" would be read as group $10).
        xml = Regex.Replace(xml,
            "(<float name=\"MouseSensitivity\">)[^<]*(</float>)",
            "${1}" + v + "${2}");

        xml = Regex.Replace(xml,
            "(<Vector2 name=\"MouseSensitivityFirstPerson\">\\s*<X>)[^<]*(</X>\\s*<Y>)[^<]*(</Y>)",
            "${1}" + v + "${2}" + v + "${3}", RegexOptions.Singleline);

        return xml;
    }
}

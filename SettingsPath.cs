using System;
using System.IO;

namespace JinxyClicker;

/// <summary>
/// Resolves where the application's settings files live.
/// </summary>
/// <remarks>
/// These used to be bare file names, which resolve against the working
/// directory. One install therefore read a different set of settings depending
/// on how it was started: launched from Visual Studio it found them, launched
/// from a desktop shortcut or by double-clicking the executable it silently
/// began from defaults and wrote a second copy somewhere nobody would look.
///
/// %APPDATA% rather than the executable's own folder, for two reasons. It does
/// not depend on where the app was installed and stays writable even under
/// Program Files, and it survives deleting bin/, which the executable's folder
/// plainly does not.
/// </remarks>
public static class SettingsPath
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JinxyClicker");

    /// <summary>
    /// Full path for a settings file, creating the folder if it is missing and
    /// carrying across a copy left behind by an older build.
    /// </summary>
    public static string For(string fileName)
    {
        string target = Path.Combine(Folder, fileName);

        try
        {
            Directory.CreateDirectory(Folder);
            if (!File.Exists(target)) Migrate(fileName, target);
        }
        catch
        {
            // A settings file that cannot be placed is not worth a crash. Every
            // caller already treats a missing file as "use the defaults".
        }

        return target;
    }

    /// <summary>
    /// Copies a file left in the working directory by a build that resolved
    /// these names against it, so upgrading does not present as losing every
    /// setting, preset and binding at once.
    /// </summary>
    private static void Migrate(string fileName, string target)
    {
        try
        {
            string legacy = Path.GetFullPath(fileName);

            if (File.Exists(legacy) && !string.Equals(legacy, target, StringComparison.OrdinalIgnoreCase))
                File.Copy(legacy, target);
        }
        catch
        {
            // Nothing to carry across, or not ours to read. Either way the
            // defaults are a working starting point.
        }
    }
}

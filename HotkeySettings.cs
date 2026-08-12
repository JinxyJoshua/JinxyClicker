using System;
using System.Windows.Input;
using System.IO;
using System.Text.Json;

namespace MyBlinkStyleClicker;

/// <summary>
/// A bindable trigger, stored as a Windows virtual-key code so that keyboard
/// keys and mouse side buttons are the same kind of thing. GetAsyncKeyState
/// polls both identically.
/// </summary>
public sealed record HotkeyBinding(int VirtualKey, string Name)
{
    public const int VkXButton1 = 0x05;
    public const int VkXButton2 = 0x06;

    public static HotkeyBinding FromKey(Key key) =>
        new(KeyInterop.VirtualKeyFromKey(key), key.ToString());

    /// <summary>Null for buttons that must not be bound — left in particular,
    /// which is the button this app is busy synthesising.</summary>
    public static HotkeyBinding? FromMouse(MouseButton button) => button switch
    {
        MouseButton.XButton1 => new HotkeyBinding(VkXButton1, "Mouse 4"),
        MouseButton.XButton2 => new HotkeyBinding(VkXButton2, "Mouse 5"),
        _ => null
    };

    public bool IsValid => VirtualKey != 0;
}

public class HotkeySettings
{
    private const string SETTINGS_FILE = "hotkey_settings.json";

    public HotkeyBinding Hotkey { get; set; } = HotkeyBinding.FromKey(Key.F6);
    public HotkeyBinding ShakeHotkey { get; set; } = HotkeyBinding.FromKey(Key.F7);

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(new
            {
                HotkeyVk = Hotkey.VirtualKey,
                HotkeyName = Hotkey.Name,
                ShakeVk = ShakeHotkey.VirtualKey,
                ShakeName = ShakeHotkey.Name
            });

            File.WriteAllText(SETTINGS_FILE, json);
        }
        catch { }
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(SETTINGS_FILE)) return;

            var json = File.ReadAllText(SETTINGS_FILE);
            var data = JsonSerializer.Deserialize<JsonElement>(json);

            Hotkey = ReadBinding(data, "HotkeyVk", "HotkeyName", "HotkeyKey", Hotkey);
            ShakeHotkey = ReadBinding(data, "ShakeVk", "ShakeName", "ShakeHotkeyKey", ShakeHotkey);
        }
        catch { }
    }

    /// <summary>
    /// Reads the virtual-key form, falling back to the older
    /// "name of a WPF Key" form so existing settings files still load.
    /// </summary>
    private static HotkeyBinding ReadBinding(
        JsonElement data, string vkProperty, string nameProperty, string legacyProperty, HotkeyBinding fallback)
    {
        if (data.TryGetProperty(vkProperty, out JsonElement vkElement)
            && vkElement.TryGetInt32(out int vk)
            && vk != 0)
        {
            string name = data.TryGetProperty(nameProperty, out JsonElement nameElement)
                ? nameElement.GetString() ?? vk.ToString()
                : vk.ToString();

            return new HotkeyBinding(vk, name);
        }

        if (data.TryGetProperty(legacyProperty, out JsonElement legacy)
            && Enum.TryParse(legacy.GetString(), out Key key))
        {
            return HotkeyBinding.FromKey(key);
        }

        return fallback;
    }
}

using System;
using System.Windows.Input;
using System.IO;
using System.Text.Json;

namespace JinxyClicker;

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

    /// <summary>
    /// A mouse button as a binding, or null for one that must not be bound.
    /// </summary>
    /// <remarks>
    /// Only the side pair. Left, right and middle are all refused, and the
    /// reason is the same for all three: the clicker sends one of them. Which
    /// one is a setting on the clicker page, and GetAsyncKeyState cannot tell a
    /// synthesised press from a real one, so any of the three could end up
    /// firing the action it was bound to for as long as the clicker ran.
    ///
    /// Right and middle were briefly allowed here on the reasoning that the app
    /// only ever sends left. That was read off the MOUSEEVENTF constants in
    /// MainWindow, which are the left pair — the right and middle flags live in
    /// ClickButtons, and the BUTTON selector chooses between all three. The
    /// numbering below still follows the usual convention, so the names line up
    /// with what people call them: 1 left, 2 right, 3 middle, 4 and 5 the side
    /// pair.
    /// </remarks>
    public static HotkeyBinding? FromMouse(MouseButton button) => button switch
    {
        MouseButton.XButton1 => new HotkeyBinding(VkXButton1, "Mouse 4"),
        MouseButton.XButton2 => new HotkeyBinding(VkXButton2, "Mouse 5"),
        _ => null
    };

    /// <summary>
    /// The side buttons as bindings, found by virtual key rather than by a WPF
    /// mouse event.
    /// </summary>
    /// <remarks>
    /// The rebind is captured a second way, from the hotkey poll thread, and
    /// this is what that thread has to work with — it sees virtual keys, not
    /// buttons.
    ///
    /// Only the side pair. Left, right and middle are all reachable through the
    /// window's own mouse event, and polling for them would capture the very
    /// left click that armed the rebind.
    /// </remarks>
    public static HotkeyBinding? FromSideButtonKey(int virtualKey) => virtualKey switch
    {
        VkXButton1 => new HotkeyBinding(VkXButton1, "Mouse 4"),
        VkXButton2 => new HotkeyBinding(VkXButton2, "Mouse 5"),
        _ => null
    };

    /// <summary>No key. Polls as never-pressed, and reads as "Not set" on its button.</summary>
    public static readonly HotkeyBinding Unbound = new(0, "Not set");

    public bool IsValid => VirtualKey != 0;
}

public class HotkeySettings
{
    private static readonly string SETTINGS_FILE = SettingsPath.For("hotkey_settings.json");

    public HotkeyBinding Hotkey { get; set; } = HotkeyBinding.FromKey(Key.F6);
    public HotkeyBinding ReplayHotkey { get; set; } = HotkeyBinding.FromKey(Key.F8);
    /// <summary>
    /// Unbound by default, unlike the other three. Shipping a default would drop
    /// it on top of whatever an existing user had already bound to that key —
    /// their settings file predates this action and cannot say it is taken.
    /// </summary>
    public HotkeyBinding RecordHotkey { get; set; } = HotkeyBinding.Unbound;

    /// <summary>Starts and stops the clicker and shake together. Also unbound
    /// by default, for the same reason as the record key.</summary>
    public HotkeyBinding ComboHotkey { get; set; } = HotkeyBinding.Unbound;

    /// <summary>Clicks at a fixed building rate, ignoring both sliders. Unbound
    /// by default, like every action added after the first release.</summary>
    public HotkeyBinding BuildHotkey { get; set; } = HotkeyBinding.Unbound;

    /// <summary>Turns the auto switcher on and off. Unbound by default, like
    /// every action added after the first release.</summary>
    public HotkeyBinding SwitcherHotkey { get; set; } = HotkeyBinding.Unbound;

    /// <summary>
    /// Starts the clicker and the auto switcher together.
    /// </summary>
    /// <remarks>
    /// The pairing the switch technique actually needs: clicking and rotating
    /// begin on one press, at the moment there is least time to reach for two
    /// keys. Unbound by default, like every action added after the first
    /// release.
    /// </remarks>
    public HotkeyBinding ClickSwitchHotkey { get; set; } = HotkeyBinding.Unbound;

    /// <summary>
    /// Turns every other hotkey off, and back on again.
    /// </summary>
    /// <remarks>
    /// Deliberately not disabled by itself. It is polled outside the armed
    /// check, so switching hotkeys off leaves this one listening — a master
    /// switch that could turn itself off would be a one-way door.
    /// </remarks>
    public HotkeyBinding MasterHotkey { get; set; } = HotkeyBinding.Unbound;

    public void Save()
    {
        try
        {
            // Every binding, not a subset. Replay was previously written by
            // neither Save nor Load, so rebinding it held for the session and
            // silently reverted to F8 on the next launch.
            var json = JsonSerializer.Serialize(new
            {
                HotkeyVk = Hotkey.VirtualKey,
                HotkeyName = Hotkey.Name,
                ReplayVk = ReplayHotkey.VirtualKey,
                ReplayName = ReplayHotkey.Name,
                RecordVk = RecordHotkey.VirtualKey,
                RecordName = RecordHotkey.Name,
                ComboVk = ComboHotkey.VirtualKey,
                ComboName = ComboHotkey.Name,
                BuildVk = BuildHotkey.VirtualKey,
                BuildName = BuildHotkey.Name,
                SwitcherVk = SwitcherHotkey.VirtualKey,
                SwitcherName = SwitcherHotkey.Name,
                MasterVk = MasterHotkey.VirtualKey,
                MasterName = MasterHotkey.Name,
                ClickSwitchVk = ClickSwitchHotkey.VirtualKey,
                ClickSwitchName = ClickSwitchHotkey.Name
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
            ReplayHotkey = ReadBinding(data, "ReplayVk", "ReplayName", "ReplayHotkeyKey", ReplayHotkey);
            RecordHotkey = ReadBinding(data, "RecordVk", "RecordName", "RecordHotkeyKey", RecordHotkey);
            ComboHotkey = ReadBinding(data, "ComboVk", "ComboName", "ComboHotkeyKey", ComboHotkey);
            BuildHotkey = ReadBinding(data, "BuildVk", "BuildName", "BuildHotkeyKey", BuildHotkey);
            SwitcherHotkey = ReadBinding(data, "SwitcherVk", "SwitcherName", "SwitcherHotkeyKey", SwitcherHotkey);
            MasterHotkey = ReadBinding(data, "MasterVk", "MasterName", "MasterHotkeyKey", MasterHotkey);
            ClickSwitchHotkey = ReadBinding(data, "ClickSwitchVk", "ClickSwitchName", "ClickSwitchHotkeyKey", ClickSwitchHotkey);
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

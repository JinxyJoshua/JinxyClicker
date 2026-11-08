using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace JinxyClicker;

/// <summary>
/// The Customize tab on the Settings page: hide/show a sidebar tab (the "–"),
/// reorder it within its group, or rename it. Layers on top of the drag-reorder
/// system (<see cref="MainWindow.NavReorder"/>): hide and rename are stored in the
/// same saved <see cref="NavLayout"/> and applied to the existing nav buttons, so
/// nothing is regenerated. Group headers and the pinned Plus/Discord buttons are
/// not customizable.
/// </summary>
public partial class MainWindow
{
    private readonly Dictionary<string, string> _navDefaultLabels = new();   // id -> default label
    private List<(string Group, List<string> Items)> _navDefaultStructure = new();
    private bool _navDefaultsCaptured;

    /// <summary>Captured once, before any saved layout is applied, so the defaults
    /// are the real XAML labels and order rather than a customized state.</summary>
    private void CaptureNavDefaults()
    {
        if (_navDefaultsCaptured) return;

        foreach ((Button _, StackPanel p) in _navGroups)
            foreach (Button b in NamedButtons(p))
                _navDefaultLabels[b.Name] = b.Content?.ToString() ?? b.Name;

        _navDefaultStructure = CurrentNavStructure()
            .Select(g => (g.Group, g.Items.ToList()))
            .ToList();

        _navDefaultsCaptured = true;
    }

    /// <summary>Applies the saved hidden set and custom labels to the nav buttons.</summary>
    private void ApplyNavHideAndLabels(NavLayout? layout)
    {
        var hidden = new HashSet<string>(layout?.Hidden ?? new List<string>());
        Dictionary<string, string> labels = layout?.Labels ?? new Dictionary<string, string>();

        foreach ((Button _, StackPanel p) in _navGroups)
            foreach (Button b in NamedButtons(p))
            {
                b.Visibility = hidden.Contains(b.Name) ? Visibility.Collapsed : Visibility.Visible;
                b.Content = labels.TryGetValue(b.Name, out string? lbl) && !string.IsNullOrWhiteSpace(lbl)
                    ? lbl
                    : _navDefaultLabels.GetValueOrDefault(b.Name, b.Name);
            }
    }

    private NavLayout CurrentNavLayoutModel() => NavLayout.Deserialize(_navLayoutJson) ?? new NavLayout();

    // A layout from the CURRENT sidebar order plus the given hide/labels.
    private NavLayout LayoutFromCurrent(IEnumerable<string> hidden, Dictionary<string, string> labels)
    {
        var layout = new NavLayout { Hidden = hidden.ToList(), Labels = labels };
        foreach ((Button _, StackPanel p) in CurrentGroupBlocks())
            layout.Groups.Add(new NavGroupOrder
            {
                Group = p.Name,
                Items = NamedButtons(p).Select(b => b.Name).ToList(),
            });
        return layout;
    }

    private void SaveNavCustomization(NavLayout layout)
    {
        _navLayoutJson = NavLayout.Serialize(layout);
        _settingsDirty = true;
        SaveAppSettings();
        ApplyNavLayout();
        BuildNavCustomizeEditor();
    }

    // ---- edits ----

    private void ToggleNavHidden(string id)
    {
        NavLayout layout = CurrentNavLayoutModel();
        var hidden = new HashSet<string>(layout.Hidden);
        if (!hidden.Add(id)) hidden.Remove(id);
        SaveNavCustomization(LayoutFromCurrent(hidden, layout.Labels));
    }

    private void RenameNav(string id, string? text)
    {
        NavLayout layout = CurrentNavLayoutModel();

        string def = _navDefaultLabels.GetValueOrDefault(id, id);
        string trimmed = (text ?? "").Trim();
        string? newLabel = trimmed.Length == 0 || trimmed == def ? null : trimmed;

        layout.Labels.TryGetValue(id, out string? currentLabel);
        if (newLabel == currentLabel) return;   // unchanged: don't save or rebuild

        var labels = new Dictionary<string, string>(layout.Labels);
        if (newLabel is null) labels.Remove(id); else labels[id] = newLabel;

        // Persist and update the sidebar, but do NOT rebuild the editor here. The
        // rename fires on LostFocus, which happens synchronously as another button
        // takes focus on mouse-down — rebuilding (Children.Clear) would remove that
        // button mid-press and swallow the click. The row's box already shows the
        // new name; only the sidebar button needs it.
        _navLayoutJson = NavLayout.Serialize(LayoutFromCurrent(new HashSet<string>(layout.Hidden), labels));
        _settingsDirty = true;
        SaveAppSettings();
        ApplyNavLayout();
    }

    private void NavMoveWithinGroup(string id, int dir)
    {
        StackPanel? panel = _navGroups.Select(g => g.Panel)
            .FirstOrDefault(p => NamedButtons(p).Any(b => b.Name == id));
        if (panel is null) return;

        List<Button> buttons = NamedButtons(panel).ToList();
        int idx = buttons.FindIndex(b => b.Name == id);
        int target = idx + dir;
        if (idx < 0 || target < 0 || target >= buttons.Count) return;

        Button moving = buttons[idx];
        Button anchor = buttons[target];

        panel.Children.Remove(moving);
        int anchorIdx = panel.Children.IndexOf(anchor);
        panel.Children.Insert(dir < 0 ? anchorIdx : anchorIdx + 1, moving);

        CaptureNavLayout();   // saves the new order, keeps hide/labels, rebuilds the editor
    }

    private void NavCustomizeReset_Click(object sender, RoutedEventArgs e)
    {
        var layout = new NavLayout();
        foreach ((string group, List<string> items) in _navDefaultStructure)
            layout.Groups.Add(new NavGroupOrder { Group = group, Items = items.ToList() });

        SaveNavCustomization(layout);
    }

    // ---- editor ----

    private void BuildNavCustomizeEditor()
    {
        if (NavCustomizeList is null) return;
        NavCustomizeList.Children.Clear();

        NavLayout layout = CurrentNavLayoutModel();
        var hidden = new HashSet<string>(layout.Hidden);

        foreach ((Button _, StackPanel p) in CurrentGroupBlocks())
        {
            List<Button> buttons = NamedButtons(p).ToList();
            for (int i = 0; i < buttons.Count; i++)
                NavCustomizeList.Children.Add(
                    NavCustomizeRow(buttons[i].Name, hidden, layout, i == 0, i == buttons.Count - 1));
        }
    }

    private UIElement NavCustomizeRow(string id, HashSet<string> hidden, NavLayout layout, bool first, bool last)
    {
        bool isHidden = hidden.Contains(id);
        string def = _navDefaultLabels.GetValueOrDefault(id, id);
        layout.Labels.TryGetValue(id, out string? custom);

        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = true };

        var hide = new Button
        {
            Content = isHidden ? "+" : "–",
            Width = 30, Height = 30, FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = isHidden ? "Show this tab" : "Hide this tab"
        };
        hide.Click += (_, _) => ToggleNavHidden(id);
        DockPanel.SetDock(hide, Dock.Left);
        row.Children.Add(hide);

        var up = new Button { Content = "↑", Width = 30, Height = 30, IsEnabled = !first, Margin = new Thickness(0, 0, 4, 0), ToolTip = "Move up" };
        up.Click += (_, _) => NavMoveWithinGroup(id, -1);
        DockPanel.SetDock(up, Dock.Left);
        row.Children.Add(up);

        var down = new Button { Content = "↓", Width = 30, Height = 30, IsEnabled = !last, Margin = new Thickness(0, 0, 8, 0), ToolTip = "Move down" };
        down.Click += (_, _) => NavMoveWithinGroup(id, 1);
        DockPanel.SetDock(down, Dock.Left);
        row.Children.Add(down);

        var name = new TextBox
        {
            Text = custom ?? def,
            Height = 30,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(8, 0, 8, 0),
            Opacity = isHidden ? 0.5 : 1.0
        };
        name.LostFocus += (_, _) => RenameNav(id, name.Text);
        name.KeyDown += (_, ke) => { if (ke.Key == System.Windows.Input.Key.Enter) RenameNav(id, name.Text); };
        row.Children.Add(name);

        return row;
    }
}

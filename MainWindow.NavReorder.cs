using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace JinxyClicker;

/// <summary>
/// Drag-to-reorder for the sidebar: drag a group header to reorder the groups, or
/// drag a tab to move it anywhere — within its group or into another one. The list
/// reflows live as you drag, and the order is saved and restored across launches
/// (reconciled against the real sidebar, so new tabs still show up — see
/// <see cref="NavLayout"/>). Dragging is distinguished from a normal click by a small
/// movement threshold, so tapping a tab still just navigates.
/// </summary>
public partial class MainWindow
{
    private string? _navLayoutJson;

    private bool _navReorderInit;
    private int _navFixedPrefix;                                   // children before the first group (wordmark, divider)
    private readonly List<(Button Header, StackPanel Panel)> _navGroups = new();

    private const double NavDragThreshold = 6;
    private Point _navDragStart;
    private Button? _navDragItem;        // a tab being dragged
    private Button? _navDragHeader;      // a group header being dragged
    private StackPanel? _navDragHeaderPanel;
    private bool _navDragging;

    private void InitNavReorder()
    {
        if (_navReorderInit || NavList is null) return;
        _navReorderInit = true;

        BuildNavModel();
        ApplyNavLayout();

        NavList.PreviewMouseLeftButtonDown += NavList_PreviewDown;
        NavList.PreviewMouseMove += NavList_PreviewMove;
        NavList.PreviewMouseLeftButtonUp += NavList_PreviewUp;
    }

    // Reads the group blocks (a header immediately followed by its panel) out of the
    // sidebar once. The elements are reused, only ever repositioned.
    private void BuildNavModel()
    {
        _navGroups.Clear();
        var kids = NavList.Children.Cast<UIElement>().ToList();
        for (int i = 0; i < kids.Count - 1; i++)
            if (kids[i] is Button h && IsGroupHeader(h) && kids[i + 1] is StackPanel p)
            {
                _navGroups.Add((h, p));
                i++;   // skip the panel we just paired
            }

        _navFixedPrefix = _navGroups.Count > 0 ? NavList.Children.IndexOf(_navGroups[0].Header) : 0;
    }

    private bool IsGroupHeader(Button b)
    {
        try { return ReferenceEquals(b.Style, FindResource("GroupHeader")); }
        catch { return false; }
    }

    // The group blocks in their current on-screen order.
    private List<(Button Header, StackPanel Panel)> CurrentGroupBlocks()
    {
        var result = new List<(Button, StackPanel)>();
        foreach (UIElement child in NavList.Children)
            if (child is StackPanel sp)
            {
                var g = _navGroups.FirstOrDefault(x => x.Panel == sp);
                if (g.Panel is not null) result.Add((g.Header, g.Panel));
            }
        return result;
    }

    // Named nav buttons in a group panel (an unnamed one — e.g. a future dynamic
    // button — can't be persisted by name, so it's left out of the layout).
    private static IEnumerable<Button> NamedButtons(StackPanel panel) =>
        panel.Children.OfType<Button>().Where(b => !string.IsNullOrEmpty(b.Name));

    // The sidebar as it is right now: each group with its tab names, in order.
    private List<(string Group, IReadOnlyList<string> Items)> CurrentNavStructure() =>
        CurrentGroupBlocks()
            .Select(b => (b.Panel.Name, (IReadOnlyList<string>)NamedButtons(b.Panel).Select(x => x.Name).ToList()))
            .ToList();

    private void ApplyNavLayout()
    {
        var ordered = NavLayout.Reconcile(NavLayout.Deserialize(_navLayoutJson), CurrentNavStructure());
        ReorderNavTree(ordered);
    }

    private void ReorderNavTree(List<(string Group, List<string> Items)> ordered)
    {
        // 1. Items within each group (an item may have moved groups, so place by name).
        var byName = _navGroups
            .SelectMany(g => NamedButtons(g.Panel))
            .ToDictionary(b => b.Name);
        foreach (Button b in byName.Values.ToList())
            if (b.Parent is StackPanel sp) sp.Children.Remove(b);

        foreach ((string group, List<string> items) in ordered)
        {
            StackPanel? panel = _navGroups.FirstOrDefault(g => g.Panel.Name == group).Panel;
            if (panel is null) continue;
            foreach (string name in items)
                if (byName.TryGetValue(name, out Button? b)) panel.Children.Add(b);
        }

        // 2. Group blocks, in order, between the fixed prefix and whatever follows.
        foreach ((Button h, StackPanel p) in _navGroups)
        {
            NavList.Children.Remove(h);
            NavList.Children.Remove(p);
        }
        int at = _navFixedPrefix;
        foreach ((string group, _) in ordered)
        {
            var g = _navGroups.FirstOrDefault(x => x.Panel.Name == group);
            if (g.Panel is null) continue;
            NavList.Children.Insert(at++, g.Header);
            NavList.Children.Insert(at++, g.Panel);
        }
    }

    private void CaptureNavLayout()
    {
        var layout = new NavLayout();
        foreach ((Button _, StackPanel p) in CurrentGroupBlocks())
            layout.Groups.Add(new NavGroupOrder
            {
                Group = p.Name,
                Items = NamedButtons(p).Select(b => b.Name).ToList(),
            });

        _navLayoutJson = NavLayout.Serialize(layout);
        _settingsDirty = true;
        SaveAppSettings();
    }

    // ---- dragging ----

    private void NavList_PreviewDown(object sender, MouseButtonEventArgs e)
    {
        _navDragItem = null;
        _navDragHeader = null;
        _navDragging = false;
        _navDragStart = e.GetPosition(NavList);

        Button? btn = FindNavButton(e.OriginalSource as DependencyObject);
        if (btn is null) return;

        var asGroup = _navGroups.FirstOrDefault(g => g.Header == btn);
        if (asGroup.Header is not null) { _navDragHeader = btn; _navDragHeaderPanel = asGroup.Panel; }
        else if (!string.IsNullOrEmpty(btn.Name) && btn.Parent is StackPanel sp && _navGroups.Any(g => g.Panel == sp))
            _navDragItem = btn;
    }

    private void NavList_PreviewMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            if (!_navDragging) { _navDragItem = null; _navDragHeader = null; }
            return;
        }
        if (_navDragItem is null && _navDragHeader is null) return;

        Point p = e.GetPosition(NavList);
        if (!_navDragging)
        {
            if (Math.Abs(p.Y - _navDragStart.Y) <= NavDragThreshold && Math.Abs(p.X - _navDragStart.X) <= NavDragThreshold)
                return;
            BeginNavDrag();   // crossed the threshold — fall through and place it this tick too
        }

        if (_navDragItem is not null) ReorderItemLive(p.Y);
        else ReorderGroupLive(p.Y);
        e.Handled = true;
    }

    private void NavList_PreviewUp(object sender, MouseButtonEventArgs e)
    {
        if (_navDragging)
        {
            EndNavDrag();
            e.Handled = true;   // a drag happened — don't let it register as a click
        }
        _navDragItem = null;
        _navDragHeader = null;
    }

    private void BeginNavDrag()
    {
        _navDragging = true;
        NavList.CaptureMouse();
        NavList.Cursor = Cursors.SizeAll;
        if (_navDragItem is not null) _navDragItem.Opacity = 0.4;
        if (_navDragHeader is not null)
        {
            _navDragHeader.Opacity = 0.5;
            if (_navDragHeaderPanel is not null) _navDragHeaderPanel.Opacity = 0.5;
        }
    }

    private void EndNavDrag()
    {
        NavList.ReleaseMouseCapture();
        NavList.Cursor = null;
        if (_navDragItem is not null) _navDragItem.Opacity = 1;
        if (_navDragHeader is not null)
        {
            _navDragHeader.Opacity = 1;
            if (_navDragHeaderPanel is not null) _navDragHeaderPanel.Opacity = 1;
        }
        _navDragging = false;
        CaptureNavLayout();
    }

    // Moves the dragged tab to wherever the cursor is — into any group, at the slot
    // its vertical position implies.
    private void ReorderItemLive(double y)
    {
        if (_navDragItem is null) return;
        StackPanel? target = GroupPanelAt(y);
        if (target is null) return;

        int idx = 0;
        foreach (Button b in target.Children.OfType<Button>())
        {
            if (b == _navDragItem) continue;
            double top = b.TranslatePoint(new Point(0, 0), NavList).Y;
            if (y > top + b.ActualHeight / 2) idx++;
        }

        if (_navDragItem.Parent is not StackPanel cur) return;
        if (cur == target && cur.Children.IndexOf(_navDragItem) == idx) return;

        cur.Children.Remove(_navDragItem);
        target.Children.Insert(Math.Min(idx, target.Children.Count), _navDragItem);
    }

    private void ReorderGroupLive(double y)
    {
        if (_navDragHeader is null || _navDragHeaderPanel is null) return;
        List<(Button Header, StackPanel Panel)> blocks = CurrentGroupBlocks();

        int target = 0;
        foreach ((Button h, StackPanel p) in blocks)
        {
            if (h == _navDragHeader) continue;
            double top = h.TranslatePoint(new Point(0, 0), NavList).Y;
            double bottom = p.TranslatePoint(new Point(0, 0), NavList).Y + p.ActualHeight;
            if (y > (top + bottom) / 2) target++;
        }

        int cur = blocks.FindIndex(b => b.Header == _navDragHeader);
        if (cur == target) return;

        NavList.Children.Remove(_navDragHeader);
        NavList.Children.Remove(_navDragHeaderPanel);

        List<(Button Header, StackPanel Panel)> rest = CurrentGroupBlocks();
        int insert = target >= rest.Count
            ? NavList.Children.IndexOf(rest[^1].Panel) + 1
            : NavList.Children.IndexOf(rest[target].Header);
        NavList.Children.Insert(insert, _navDragHeader);
        NavList.Children.Insert(insert + 1, _navDragHeaderPanel);
    }

    // The group panel the cursor is over (the last group whose header sits at or above
    // the cursor), so a tab can be dropped into any group including an empty one.
    private StackPanel? GroupPanelAt(double y)
    {
        List<(Button Header, StackPanel Panel)> blocks = CurrentGroupBlocks();
        if (blocks.Count == 0) return null;

        StackPanel target = blocks[0].Panel;
        foreach ((Button h, StackPanel p) in blocks)
        {
            if (y >= h.TranslatePoint(new Point(0, 0), NavList).Y) target = p;
            else break;
        }
        return target;
    }

    private Button? FindNavButton(DependencyObject? src)
    {
        while (src is not null && src != NavList)
        {
            if (src is Button b) return b;
            src = System.Windows.Media.VisualTreeHelper.GetParent(src);
        }
        return null;
    }
}

using System.Windows;

namespace JinxyClicker;

/// <summary>
/// The glyph shown beside a navigation button's label.
/// </summary>
/// <remarks>
/// An attached property rather than a second Content, so each button stays one
/// line of XAML with its icon written next to its name. The alternative — a
/// StackPanel of icon and text as the Content of every button — repeats the
/// same four lines twelve times and puts the label somewhere the selection
/// trigger cannot reach.
///
/// The glyphs come from the icon font Windows already ships, so nothing has to
/// be drawn, bundled, or kept in step with the app's colours.
/// </remarks>
public static class NavIcon
{
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.RegisterAttached(
            "Glyph", typeof(string), typeof(NavIcon), new PropertyMetadata(""));

    public static string GetGlyph(DependencyObject element) =>
        (string)element.GetValue(GlyphProperty);

    public static void SetGlyph(DependencyObject element, string value) =>
        element.SetValue(GlyphProperty, value);
}

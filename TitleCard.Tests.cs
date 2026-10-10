using System.Windows.Media;
using JinxyClicker;
using Xunit;

public class TitleCardTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Blank_text_renders_no_card(string? text)
    {
        // No text means no intro: return null before touching the filesystem,
        // so the montage simply has no title segment.
        Assert.Null(TitleCard.Render(text!, 1920, 1080, Colors.Red, @"C:\does\not\exist\title.png"));
    }
}

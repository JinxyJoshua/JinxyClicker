using System;
using System.IO;
using System.Linq;
using JinxyClicker;
using Xunit;

public class ClipLibraryTests
{
    private static string TempDir()
    {
        string d = Path.Combine(Path.GetTempPath(), "jinxyclip_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void Missing_folder_lists_nothing()
    {
        Assert.Empty(ClipLibrary.List(Path.Combine(Path.GetTempPath(), "nope_" + Guid.NewGuid().ToString("N"))));
    }

    [Fact]
    public void Lists_only_mp4_newest_first()
    {
        string d = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(d, "a.mp4"), "x");
            System.Threading.Thread.Sleep(20);
            File.WriteAllText(Path.Combine(d, "b.mp4"), "x");
            File.WriteAllText(Path.Combine(d, "notes.txt"), "x");

            var list = ClipLibrary.List(d);
            Assert.Equal(2, list.Count);
            Assert.Equal("b.mp4", list[0].Name); // newest first
            Assert.Equal("a.mp4", list[1].Name);
        }
        finally { Directory.Delete(d, true); }
    }

    [Fact]
    public void Delete_removes_the_file()
    {
        string d = TempDir();
        try
        {
            string p = Path.Combine(d, "c.mp4");
            File.WriteAllText(p, "x");
            Assert.True(ClipLibrary.Delete(p));
            Assert.False(File.Exists(p));
            Assert.True(ClipLibrary.Delete(p)); // already gone -> still "gone"
        }
        finally { Directory.Delete(d, true); }
    }
}

using System;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace JinxyClicker.Tests;

public class FeedbackTests
{
    // ---- Validate ----

    [Theory]
    [InlineData("", "A summary is required")]
    [InlineData("   ", "whitespace-only is empty")]
    public void Validate_RejectsEmptySummary(string summary, string _why)
    {
        Assert.NotNull(Feedback.Validate(summary, "details"));
    }

    [Fact]
    public void Validate_RejectsTooLongSummary()
    {
        string summary = new string('x', Feedback.MaxSummary + 1);
        Assert.NotNull(Feedback.Validate(summary, ""));
    }

    [Fact]
    public void Validate_RejectsTooLongDetails()
    {
        string details = new string('y', Feedback.MaxDetails + 1);
        Assert.NotNull(Feedback.Validate("ok summary", details));
    }

    [Fact]
    public void Validate_AcceptsGoodInput()
    {
        Assert.Null(Feedback.Validate("Add a dark theme", "It would be nice to have one."));
    }

    [Fact]
    public void Validate_AcceptsEmptyDetails()
    {
        // Summary alone is enough; details are optional.
        Assert.Null(Feedback.Validate("Crosshair resets after update", ""));
    }

    // ---- type mapping ----

    [Theory]
    [InlineData(FeedbackType.Feature, "feature")]
    [InlineData(FeedbackType.Improvement, "improvement")]
    [InlineData(FeedbackType.Bug, "bug")]
    public void TypeSlug_RoundTrips(FeedbackType type, string slug)
    {
        Assert.Equal(slug, Feedback.TypeSlug(type));
        Assert.Equal(type, Feedback.ParseType(slug));
    }

    [Fact]
    public void ParseType_UnknownFallsBackToFeature()
    {
        Assert.Equal(FeedbackType.Feature, Feedback.ParseType("nonsense"));
        Assert.Equal(FeedbackType.Feature, Feedback.ParseType(null));
    }

    // ---- BuildSubmitJson ----

    [Fact]
    public void BuildSubmitJson_CarriesEveryFieldAndTrims()
    {
        var draft = new FeedbackDraft(FeedbackType.Bug, "  mouse sticks  ", "  it stuck once  ");
        string json = Feedback.BuildSubmitJson(draft, "1.4.15");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("bug", root.GetProperty("type").GetString());
        Assert.Equal("mouse sticks", root.GetProperty("summary").GetString());
        Assert.Equal("it stuck once", root.GetProperty("details").GetString());
        Assert.Equal("1.4.15", root.GetProperty("version").GetString());
    }

    [Fact]
    public void BuildSubmitJson_EscapesQuotesAndNewlines()
    {
        var draft = new FeedbackDraft(FeedbackType.Feature, "add \"combo\" mode", "line1\nline2");
        string json = Feedback.BuildSubmitJson(draft, "1.0.0");

        // Must be valid JSON that round-trips the exact text.
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("add \"combo\" mode", doc.RootElement.GetProperty("summary").GetString());
        Assert.Equal("line1\nline2", doc.RootElement.GetProperty("details").GetString());
    }

    // ---- ParseSubmitResponse ----

    [Fact]
    public void ParseSubmitResponse_OkTrueIsSuccess()
    {
        var r = Feedback.ParseSubmitResponse(200, "{\"ok\":true}");
        Assert.True(r.Success);
    }

    [Fact]
    public void ParseSubmitResponse_ServerErrorMessageIsShown()
    {
        var r = Feedback.ParseSubmitResponse(400, "{\"ok\":false,\"error\":\"Summary too long.\"}");
        Assert.False(r.Success);
        Assert.Contains("too long", r.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseSubmitResponse_429IsRateLimitMessage()
    {
        var r = Feedback.ParseSubmitResponse(429, "");
        Assert.False(r.Success);
        Assert.Contains("fast", r.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseSubmitResponse_ServerDownIsFailure()
    {
        var r = Feedback.ParseSubmitResponse(503, "");
        Assert.False(r.Success);
    }

    [Fact]
    public void ParseSubmitResponse_EmptyBodyNon2xxFails()
    {
        var r = Feedback.ParseSubmitResponse(500, null);
        Assert.False(r.Success);
    }

    // ---- ParseList ----

    [Fact]
    public void ParseList_ReturnsItemsNewestFirst()
    {
        string body = """
        [
          {"id":"a","type":"bug","summary":"older","details":"d1","version":"1.0.0","ts":1000},
          {"id":"b","type":"feature","summary":"newer","details":"d2","version":"1.1.0","ts":5000}
        ]
        """;
        var items = Feedback.ParseList(body);
        Assert.Equal(2, items.Count);
        Assert.Equal("newer", items[0].Summary);          // ts 5000 first
        Assert.Equal(FeedbackType.Feature, items[0].Type);
        Assert.Equal("older", items[1].Summary);
    }

    [Fact]
    public void ParseList_AcceptsObjectWrappedItems()
    {
        string body = "{\"items\":[{\"id\":\"a\",\"type\":\"improvement\",\"summary\":\"s\",\"details\":\"\",\"version\":\"1.0.0\",\"ts\":1}]}";
        var items = Feedback.ParseList(body);
        Assert.Single(items);
        Assert.Equal(FeedbackType.Improvement, items[0].Type);
    }

    [Fact]
    public void ParseList_MalformedReturnsEmpty()
    {
        Assert.Empty(Feedback.ParseList("not json"));
        Assert.Empty(Feedback.ParseList(""));
        Assert.Empty(Feedback.ParseList(null));
    }
}

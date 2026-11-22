using CoCoReTrade.Application;

namespace CoCoReTrade.Tests;

public sealed class DashboardRulesTests
{
    [Theory]
    [InlineData("7d", "2026-11-16", "2026-11-09", 7)]
    [InlineData("30d", "2026-10-24", "2026-09-24", 30)]
    [InlineData("3m", "2026-08-23", "2026-05-23", 92)]
    [InlineData("12m", "2025-11-23", "2024-11-23", 365)]
    public void PresetsHaveEqualPreviousPeriods(string period, string from, string previous, int days)
    {
        var range = DashboardRange.Create(new(period), DateTimeOffset.Parse("2026-11-22T10:00:00Z"));
        Assert.Equal(DateOnly.Parse(from), range.From); Assert.Equal(DateOnly.Parse(previous), range.PreviousFrom);
        Assert.Equal(new DateOnly(2026, 11, 22), range.Through); Assert.Equal(days, range.Days);
        Assert.Equal(days, range.PreviousThrough.DayNumber - range.PreviousFrom.DayNumber + 1);
        Assert.Equal(range.From.AddDays(-1), range.PreviousThrough);
        Assert.Equal(TimeSpan.FromHours(17), range.FromUtc.TimeOfDay);
        Assert.Equal(DateTimeKind.Utc, range.FromUtc.Kind);
    }
    [Fact]
    public void DayRolloverUsesVietnamTimeAndCustomIncludesTheWholeLastDay()
    {
        var before = DashboardRange.Create(new("7d"), DateTimeOffset.Parse("2026-11-21T16:59:59Z"));
        var after = DashboardRange.Create(new("7d"), DateTimeOffset.Parse("2026-11-21T17:00:00Z"));
        Assert.Equal(new DateOnly(2026, 11, 21), before.Through); Assert.Equal(new DateOnly(2026, 11, 22), after.Through);
        var custom = DashboardRange.Create(new("custom", new(2026, 11, 1), new(2026, 11, 5)), DateTimeOffset.Parse("2026-11-22T10:00:00Z"));
        Assert.Equal(DateTime.Parse("2026-10-31T17:00:00Z").ToUniversalTime(), custom.FromUtc);
        Assert.Equal(DateTime.Parse("2026-11-05T17:00:00Z").ToUniversalTime(), custom.ToUtc);
    }
    [Fact]
    public void InvalidAndFutureDatesAreRejected()
    {
        var now = DateTimeOffset.Parse("2026-11-22T10:00:00Z");
        foreach (var filter in new DashboardFilter[] { new("invalid"), new("custom"), new("custom", new(2026, 11, 23), new(2026, 11, 24)),
                     new("custom", new(2026, 11, 3), new(2026, 11, 1)), new("custom", new(1999, 1, 1), new(2026, 1, 1)) })
            Assert.Throws<ArgumentException>(() => DashboardRange.Create(filter, now));
    }
    [Fact]
    public void ChangesDoNotInventGrowthWhenPreviousPeriodIsZero()
    {
        var fresh = new DashboardMeasure("test", "Test", 5, 0);
        Assert.Null(fresh.Percent); Assert.Equal("Mới phát sinh", fresh.ChangeLabel);
        Assert.Equal("Không đổi", new DashboardMeasure("test", "Test", 0, 0).ChangeLabel);
        var growth = new DashboardMeasure("test", "Test", 15, 10);
        Assert.Equal(50, growth.Percent); Assert.True(growth.Favorable);
        Assert.False((growth with { IncreaseIsGood = false }).Favorable);
        Assert.Equal(-100, (growth with { Value = 0 }).Percent);
        var denied = growth with { Value = null, Previous = null };
        Assert.Null(denied.Percent); Assert.Equal("Chưa được cấp quyền", denied.ChangeLabel);
    }
}

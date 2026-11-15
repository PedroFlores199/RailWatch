using FluentAssertions;
using RailWatch.Shared;
using Xunit;

namespace RailWatch.Tests;

public sealed class LogicTests
{
    [Theory]
    [InlineData(300, 0, 300)]
    [InlineData(-60, 180, -60)]
    [InlineData(null, 180, 180)]
    public void ExplicitDelayTakesPriority(int? value, int difference, int expected)
    {
        var scheduled = DateTimeOffset.Parse("2026-11-15T10:00:00Z"); DelayCalculator.Calculate(value, scheduled.AddSeconds(difference), scheduled).Should().Be(expected);
    }
    [Fact] public void UnknownDelayStaysUnknown() => DelayCalculator.Calculate(null, null, null).Should().BeNull();
    [Theory]
    [InlineData(900, false)]
    [InlineData(901, true)]
    [InlineData(-60, false)]
    [InlineData(null, false)]
    public void AlertThresholdIsStrict(int? delay, bool expected) => DelayCalculator.IsLate(delay).Should().Be(expected);
    [Fact] public void GtfsSupportsTimesBeyondMidnight() => DelayCalculator.ParseGtfsTime("25:12:30").Should().Be(TimeSpan.FromHours(25) + TimeSpan.FromMinutes(12) + TimeSpan.FromSeconds(30));
    [Theory]
    [InlineData("12:70:00")]
    [InlineData("not-a-time")]
    [InlineData("-1:00:00")]
    public void InvalidGtfsTimesAreRejected(string value) { var parse = () => DelayCalculator.ParseGtfsTime(value); parse.Should().Throw<FormatException>(); }
    [Fact] public void MadridWinterScheduleConvertsToUtc() => DelayCalculator.ScheduledUtc(new(2026, 11, 15), "10:30:00").Should().Be(DateTimeOffset.Parse("2026-11-15T09:30:00Z"));
    [Fact] public void MadridSummerScheduleConvertsToUtc() => DelayCalculator.ScheduledUtc(new(2026, 7, 15), "10:30:00").Should().Be(DateTimeOffset.Parse("2026-07-15T08:30:00Z"));
    [Fact]
    public void ViewModelDeduplicatesAlertsAndFiltersTrains()
    {
        var vm = new ControlRoomViewModel { SoundEnabled = true }; var sounds = 0; vm.SoundRequested += () => sounds++;
        var now = DateTimeOffset.UtcNow; var t = new TrainPosition("T1", "TRIP", "C1", "Cercanías", "C1-100", 40, -3, 1000, "S1", "Atocha", now, null);
        var feed = new FeedSnapshot("Replay", "Test", now, now, [t, t with { Id = "T2", Line = "C2", DelaySeconds = null }]);
        vm.Apply(feed); vm.Apply(feed); vm.Alerts.Should().HaveCount(1); sounds.Should().Be(1); vm.TrainCount.Should().Be(2);
        vm.Filter = "C2"; vm.Trains.Should().ContainSingle(x => x.Id == "T2"); vm.Punctuality.Should().Be(0);
        vm.Apply(feed with { Trains = [t with { DelaySeconds = 0 }] }); vm.Apply(feed); vm.Alerts.Should().HaveCount(2);
    }
}

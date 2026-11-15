using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RailWatch.Infrastructure;
using RailWatch.Shared;
using Xunit;

namespace RailWatch.Tests;

public sealed class PredictionTests
{
    [Fact]
    public async Task ModelTrainsOnStationPairsAndReportsHoldoutError()
    {
        await using var db = new RailDb(new DbContextOptionsBuilder<RailDb>().UseSqlite("Data Source=:memory:").Options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        var start = DateTimeOffset.Parse("2026-11-15T08:00:00Z");
        for (var i = 0; i < 250; i++)
        {
            var observed = start.AddMinutes(i);
            db.Observations.AddRange(
                new DelayObservation { TrainId = "T" + i, Line = "C1", StationId = "S1", DelaySeconds = i % 12 * 60, ObservedAt = observed, SourceMode = "Live" },
                new DelayObservation { TrainId = "T" + i, Line = "C1", StationId = "S2", DelaySeconds = (i % 12 + 1) * 60, ObservedAt = observed.AddMinutes(2), SourceMode = "Live" });
        }
        await db.SaveChangesAsync();
        var predictor = new DelayPredictor();
        var trained = await predictor.TrainAsync(db, "Live", default);
        trained.Should().BeTrue();
        predictor.Samples.Should().Be(250);
        predictor.MeanAbsoluteError.Should().NotBeNull();
        double.IsFinite(predictor.MeanAbsoluteError!.Value).Should().BeTrue();
        var value = predictor.Predict(new TrainPosition("TEST", "TRIP", "C1", "Cercanías", "Test", 40, -3, 300, "S1", "Prueba", start, null));
        value.Should().NotBeNull();
        value!.Value.Should().BeInRange(0, 120);
        var position = new TrainPosition("TEST", "TRIP", "C1", "Cercanías", "Test", 40, -3, 300, "S1", "Prueba", start, null);
        var workerState = new LiveState(new DelayPredictor());
        workerState.Apply(new("Replay", "Prueba", start, start, [position]));
        workerState.Snapshot.Trains.Single().PredictedDelayMinutes.Should().BeNull();
        var apiState = new LiveState(predictor);
        apiState.Apply(workerState.Snapshot);
        apiState.Snapshot.Trains.Single().PredictedDelayMinutes.Should().NotBeNull();
    }

    [Fact]
    public async Task ModelDoesNotInventPredictionsWithInsufficientHistory()
    {
        await using var db = new RailDb(new DbContextOptionsBuilder<RailDb>().UseSqlite("Data Source=:memory:").Options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        var predictor = new DelayPredictor();
        (await predictor.TrainAsync(db, "Replay", default)).Should().BeFalse();
        predictor.Predict(new TrainPosition("T", "TRIP", "C1", "Cercanías", "Test", 40, -3, 0, null, "", DateTimeOffset.UtcNow, null)).Should().BeNull();
    }
}

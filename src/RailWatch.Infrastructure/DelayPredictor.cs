using Microsoft.EntityFrameworkCore;
using Microsoft.ML;
using Microsoft.ML.Data;
using RailWatch.Shared;

namespace RailWatch.Infrastructure;

public sealed class DelayFeatures
{
    public float DelayMinutes { get; set; }
    public float Hour { get; set; }
    public string Line { get; set; } = "";
    public float Label { get; set; }
}
public sealed class DelayPrediction
{
    [ColumnName("Score")] public float Value { get; set; }
}
public sealed class DelayPredictor
{
    private readonly MLContext ml = new(seed: 42);
    private readonly object gate = new();
    private PredictionEngine<DelayFeatures, DelayPrediction>? engine;
    public int Samples { get; private set; }
    public double? MeanAbsoluteError { get; private set; }
    public string Status { get; private set; } = "Sin entrenar: se necesitan 200 pares de estaciones consecutivas.";
    public float? Predict(TrainPosition train)
    {
        lock (gate) return engine is null || train.DelaySeconds is null ? null : Math.Max(0, engine.Predict(new() { DelayMinutes = train.DelaySeconds.Value / 60f, Hour = train.ObservedAt.Hour, Line = train.Line }).Value);
    }
    public async Task<bool> TrainAsync(RailDb db, string mode, CancellationToken ct)
    {
        var rows = await db.Observations.AsNoTracking().Where(x => x.SourceMode == mode && x.DelaySeconds != null).OrderBy(x => x.ObservedAt).Take(100000).ToListAsync(ct);
        var pairs = rows.GroupBy(x => x.TrainId).SelectMany(g => g.Zip(g.Skip(1))).Where(p => p.First.StationId != p.Second.StationId && p.Second.ObservedAt - p.First.ObservedAt < TimeSpan.FromMinutes(45)).OrderBy(p => p.Second.ObservedAt).Select(p => new DelayFeatures { DelayMinutes = p.First.DelaySeconds!.Value / 60f, Hour = p.First.ObservedAt.Hour, Line = p.First.Line, Label = p.Second.DelaySeconds!.Value / 60f }).ToArray();
        if (pairs.Length < 200) { Status = $"Datos insuficientes: {pairs.Length}/200 pares de estaciones. No se publican predicciones."; return false; }
        var split = (int)(pairs.Length * .8);
        var pipeline = ml.Transforms.Categorical.OneHotEncoding("EncodedLine", nameof(DelayFeatures.Line)).Append(ml.Transforms.Concatenate("Features", nameof(DelayFeatures.DelayMinutes), nameof(DelayFeatures.Hour), "EncodedLine")).Append(ml.Regression.Trainers.Sdca(maximumNumberOfIterations: 100));
        var model = pipeline.Fit(ml.Data.LoadFromEnumerable(pairs.Take(split)));
        var evaluation = ml.Regression.Evaluate(model.Transform(ml.Data.LoadFromEnumerable(pairs.Skip(split))));
        lock (gate) { engine = ml.Model.CreatePredictionEngine<DelayFeatures, DelayPrediction>(model); Samples = pairs.Length; MeanAbsoluteError = evaluation.MeanAbsoluteError; Status = "Entrenado · división temporal 80/20 · " + mode; }
        return true;
    }
}

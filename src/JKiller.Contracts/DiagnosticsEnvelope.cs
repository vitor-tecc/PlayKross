namespace JKiller.Contracts;
public sealed record DiagnosticsEnvelope(Guid StreamId, string Role, IReadOnlyDictionary<string, double?> Metrics, IReadOnlyDictionary<string, string> Metadata, Guid? ObserverId = null);
public sealed record RedactedReport(int SchemaVersion, IReadOnlyList<DiagnosticsEnvelope> Samples);

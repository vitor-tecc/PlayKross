using System.Text.Json;
using JKiller.Core.Diagnostics;
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
var evidence = JsonSerializer.Deserialize<AcceptanceEvidence>(await File.ReadAllTextAsync(args.Single()), options) ?? throw new InvalidDataException("Missing acceptance evidence.");
Console.WriteLine(JsonSerializer.Serialize(new { evidence, checks = AcceptanceReportValidator.Validate(evidence) }, options));

using System.Text;

namespace JKiller.Host.Media;

// MediaMTX 1.21.2 accepts/remuxes 1080p60 H.264, but advertises Level 3.1.
// Firefox obeys that ceiling even when capture and sender parameters request 60 fps.
// This is receiver capability signaling, not an encoder or transcoding change.
internal static class WhipH264Capabilities
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private sealed record Section(int Start, int End, string[] Payloads, string? Mid);
    private sealed record Codec(int Line, int ParametersStart, Dictionary<string, string> Parameters);
    private static readonly string[] AdditionalLimits = ["max-recv-level", "max-fs", "max-mbps", "max-smbps", "max-br", "max-cpb", "max-dpb"];

    public static byte[] Advertise1080p60(byte[] offerBody, byte[] answerBody)
    {
        string[] offer, answer;
        try { offer = Utf8.GetString(offerBody).Split('\n'); answer = Utf8.GetString(answerBody).Split('\n'); }
        catch (DecoderFallbackException) { return answerBody; }
        var offered = VideoSections(offer); var answered = VideoSections(answer);
        if (offered.Count != 1 || answered.Count != 1) return answerBody;
        var source = offered[0]; var target = answered[0];
        if (source.Mid != target.Mid || !Direction(offer, source, "sendonly", "sendrecv") || !Direction(answer, target, "recvonly")) return answerBody;
        var changed = false;
        foreach (var payload in target.Payloads)
        {
            if (!source.Payloads.Contains(payload)) continue;
            var local = H264(offer, source, payload); var remote = H264(answer, target, payload);
            if (local is null || remote is null) continue;
            var a = local.Parameters; var b = remote.Parameters;
            if (a.GetValueOrDefault("level-asymmetry-allowed") != "1" || b.GetValueOrDefault("level-asymmetry-allowed") != "1" ||
                a.GetValueOrDefault("packetization-mode") != "1" || b.GetValueOrDefault("packetization-mode") != "1" ||
                AdditionalLimits.Any(key => a.ContainsKey(key) || b.ContainsKey(key))) continue;
            var profile = b.GetValueOrDefault("profile-level-id");
            if (profile is null || !(profile.Equals("42e01f", StringComparison.OrdinalIgnoreCase) || profile.Equals("42001f", StringComparison.OrdinalIgnoreCase)) ||
                !profile.Equals(a.GetValueOrDefault("profile-level-id"), StringComparison.OrdinalIgnoreCase)) continue;
            var line = answer[remote.Line]; var parts = line[remote.ParametersStart..].Split(';');
            for (var i = 0; i < parts.Length; i++)
            {
                var equals = parts[i].IndexOf('=');
                if (equals < 0 || !parts[i][..equals].Trim().Equals("profile-level-id", StringComparison.OrdinalIgnoreCase)) continue;
                var value = parts[i][(equals + 1)..]; var leading = value.Length - value.TrimStart().Length;
                parts[i] = parts[i][..(equals + 1)] + value[..leading] + profile[..4] + "2a" + value[(leading + 6)..];
            }
            answer[remote.Line] = line[..remote.ParametersStart] + string.Join(';', parts); changed = true;
        }
        return changed ? Utf8.GetBytes(string.Join('\n', answer)) : answerBody;
    }

    private static List<Section> VideoSections(string[] lines)
    {
        List<Section> result = [];
        for (var start = 0; start < lines.Length; start++)
        {
            if (!lines[start].StartsWith("m=video ", StringComparison.Ordinal)) continue;
            var end = start + 1; while (end < lines.Length && !lines[end].StartsWith("m=", StringComparison.Ordinal)) end++;
            var fields = lines[start].TrimEnd('\r').Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 4 || !int.TryParse(fields[1], out var port) || port <= 0) return [];
            var mids = lines[start..end].Where(line => line.StartsWith("a=mid:", StringComparison.Ordinal)).ToArray();
            if (mids.Length > 1) return [];
            var payloads = fields[3..]; if (payloads.Distinct().Count() != payloads.Length) return [];
            result.Add(new(start, end, payloads, mids.Length == 1 ? mids[0][6..].TrimEnd('\r') : null));
        }
        return result;
    }

    private static bool Direction(string[] lines, Section section, params string[] allowed)
    {
        var directions = lines[section.Start..section.End].Select(line => line.TrimEnd('\r'))
            .Where(line => line is "a=sendonly" or "a=sendrecv" or "a=recvonly" or "a=inactive").ToArray();
        return directions.Length == 1 && allowed.Contains(directions[0][2..]);
    }

    private static Codec? H264(string[] lines, Section section, string payload)
    {
        var rtpPrefix = "a=rtpmap:" + payload + " "; var fmtpPrefix = "a=fmtp:" + payload + " ";
        var maps = Enumerable.Range(section.Start, section.End - section.Start).Where(i => lines[i].StartsWith(rtpPrefix, StringComparison.Ordinal)).ToArray();
        var formats = Enumerable.Range(section.Start, section.End - section.Start).Where(i => lines[i].StartsWith(fmtpPrefix, StringComparison.Ordinal)).ToArray();
        if (maps.Length != 1 || formats.Length != 1 || !lines[maps[0]][rtpPrefix.Length..].Trim().Equals("H264/90000", StringComparison.OrdinalIgnoreCase)) return null;
        Dictionary<string, string> parameters = new(StringComparer.OrdinalIgnoreCase);
        foreach (var field in lines[formats[0]][fmtpPrefix.Length..].Split(';'))
        {
            var pair = field.Split('=', 2);
            if (pair.Length != 2 || !parameters.TryAdd(pair[0].Trim(), pair[1].Trim())) return null;
        }
        return new(formats[0], fmtpPrefix.Length, parameters);
    }
}

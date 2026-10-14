using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;

namespace XivEureka.Zones;

/// <summary>What a chat message tells the plugin.</summary>
/// <param name="Tracker">Id of a ffxiv-eureka.com tracker quoted in the message (e.g. "bZFR-s").</param>
/// <param name="Position">Map coordinates (the 1-42 ones) given in the text, when there is no map link.</param>
/// <param name="Pop">The message announces a pop (word "pop", or the name of a running FATE).</param>
/// <param name="Fate">Name of the running FATE quoted in the message, if any.</param>
public sealed record ChatInfo(string? Tracker, Vector2? Position, bool Pop, string? Fate);

/// <summary>
/// Reads a Eureka chat message with nothing from the game: the text, and the names of the running
/// FATEs to recognize them. Map links (MapLinkPayload) are handled separately by the plugin: they
/// are already reliable positions.
/// </summary>
public static partial class ChatParser
{
    [GeneratedRegex(@"ffxiv-eureka\.com/(?:v/)?([A-Za-z0-9_-]{6,8})\b", RegexOptions.IgnoreCase)]
    private static partial Regex TrackerLink();

    // "X: 12.3 Y: 14.5" (game format), "(12.3, 14.5)", "12,3 / 14,5"; decimal point or comma.
    [GeneratedRegex(@"X\s*:?\s*(\d{1,2}(?:[.,]\d)?)\s*[,/ ]?\s*Y\s*:?\s*(\d{1,2}(?:[.,]\d)?)|\(\s*(\d{1,2}(?:\.\d)?)\s*[,/]\s*(\d{1,2}(?:\.\d)?)\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex Coordinates();

    [GeneratedRegex(@"\b(pop|pops|popped|spawn|spawned)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PopWord();

    public static ChatInfo Parse(string text, IEnumerable<string> runningFates)
    {
        var tracker = TrackerLink().Match(text) is { Success: true } t ? t.Groups[1].Value : null;
        // A tracker id holds digits and letters: keep it out of the coordinate search.
        var withoutLink = tracker == null ? text : TrackerLink().Replace(text, " ");

        Vector2? position = null;
        var c = Coordinates().Match(withoutLink);
        if (c.Success)
        {
            var x = c.Groups[1].Success ? c.Groups[1].Value : c.Groups[3].Value;
            var y = c.Groups[2].Success ? c.Groups[2].Value : c.Groups[4].Value;
            if (Number(x) is { } nx && Number(y) is { } ny && nx is >= 1 and <= 42 && ny is >= 1 and <= 42)
                position = new Vector2(nx, ny);
        }

        var fate = runningFates
            .Where(name => name.Length >= 4)
            .OrderByDescending(name => name.Length)
            .FirstOrDefault(name => withoutLink.Contains(name, StringComparison.OrdinalIgnoreCase));
        var pop = fate != null || PopWord().IsMatch(withoutLink);
        return new ChatInfo(tracker, position, pop, fate);
    }

    private static float? Number(string text) =>
        float.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
}

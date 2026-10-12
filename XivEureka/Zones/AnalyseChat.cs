using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;

namespace XivEureka.Zones;

/// <summary>Ce qu'un message du chat apprend au plugin.</summary>
/// <param name="Tracker">Identifiant d'un tracker ffxiv-eureka.com cité dans le message (ex. « bZFR-s »).</param>
/// <param name="Position">Coordonnées de carte (« jolies », 1-42) données dans le texte, si aucun lien de carte.</param>
/// <param name="Pop">Le message annonce un pop (mot « pop », ou le nom d'une FATE en cours).</param>
/// <param name="Fate">Nom de la FATE en cours citée dans le message, s'il y en a une.</param>
public sealed record MessageEureka(string? Tracker, Vector2? Position, bool Pop, string? Fate);

/// <summary>
/// Lit un message du chat d'Eurêka, sans rien du jeu : le texte, et les noms des FATE en cours pour
/// les reconnaître. Les liens de carte (MapLinkPayload) sont traités à part par le plugin : ils sont
/// déjà des positions sûres.
/// </summary>
public static partial class AnalyseChat
{
    [GeneratedRegex(@"ffxiv-eureka\.com/(?:v/)?([A-Za-z0-9_-]{6,8})\b", RegexOptions.IgnoreCase)]
    private static partial Regex LienTracker();

    // « X: 12.3 Y: 14.5 » (format du jeu), « (12.3, 14.5) », « 12,3 / 14,5 » ; décimale point ou virgule.
    [GeneratedRegex(@"X\s*:?\s*(\d{1,2}(?:[.,]\d)?)\s*[,/ ]?\s*Y\s*:?\s*(\d{1,2}(?:[.,]\d)?)|\(\s*(\d{1,2}(?:\.\d)?)\s*[,/]\s*(\d{1,2}(?:\.\d)?)\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex Coordonnees();

    [GeneratedRegex(@"\b(pop|pops|popped|spawn|spawned)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MotPop();

    public static MessageEureka Analyser(string texte, IEnumerable<string> fatesEnCours)
    {
        var tracker = LienTracker().Match(texte) is { Success: true } t ? t.Groups[1].Value : null;
        // L'identifiant d'un tracker contient des chiffres et lettres : on évite d'y lire des coordonnées.
        var sansLien = tracker == null ? texte : LienTracker().Replace(texte, " ");

        Vector2? position = null;
        var c = Coordonnees().Match(sansLien);
        if (c.Success)
        {
            var x = c.Groups[1].Success ? c.Groups[1].Value : c.Groups[3].Value;
            var y = c.Groups[2].Success ? c.Groups[2].Value : c.Groups[4].Value;
            if (Nombre(x) is { } nx && Nombre(y) is { } ny && nx is >= 1 and <= 42 && ny is >= 1 and <= 42)
                position = new Vector2(nx, ny);
        }

        var fate = fatesEnCours
            .Where(nom => nom.Length >= 4)
            .OrderByDescending(nom => nom.Length)
            .FirstOrDefault(nom => sansLien.Contains(nom, StringComparison.OrdinalIgnoreCase));
        var pop = fate != null || MotPop().IsMatch(sansLien);
        return new MessageEureka(tracker, position, pop, fate);
    }

    private static float? Nombre(string texte) =>
        float.TryParse(texte.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
}

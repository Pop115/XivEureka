using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace XivEureka.Zones;

/// <summary>Une FATE telle que le plugin la suit : son numéro, son nom, sa position monde (X, Y, Z).</summary>
public sealed record FateVue(ushort Id, string Nom, Vector3 Position);

/// <summary>
/// Compare les FATE en cours d'une trame à l'autre pour annoncer les nouvelles. À Eurêka, toutes
/// les FATE sont des NM sauf les FATE lapin, écartées avant (<see cref="EstFateLapin"/>) ; on n'a
/// donc pas besoin d'une table des NM pour alerter, le nom et la position viennent du jeu.
/// </summary>
public sealed class SuiviFates
{
    /// <summary>
    /// Règle « collecte » de la feuille Fate. Dans les quatre zones d'Eurêka, seules les cinq FATE
    /// lapin l'ont (Down the Rabbit Hole, Curiouser and Curiouser, We're All Mad Here, Uncommon
    /// Nonsense, Drink Me) ; les NM ont la règle 1 (relevé des lignes 1328-1427, 2026-10-14).
    /// </summary>
    private const byte RegleCollecte = 4;

    /// <summary>Vrai pour une FATE lapin d'Eurêka (à ne pas annoncer : on ne les fait pas).</summary>
    public static bool EstFateLapin(byte regle) => regle == RegleCollecte;

    private readonly Dictionary<ushort, FateVue> enCours = [];
    private bool premierPassage = true;

    public IReadOnlyCollection<FateVue> EnCours => enCours.Values;

    /// <summary>
    /// Les FATE apparues depuis le dernier appel. Au premier appel (arrivée dans la zone) on ne
    /// renvoie rien : celles déjà là ne sont pas des nouvelles.
    /// </summary>
    public IReadOnlyList<FateVue> MettreAJour(IEnumerable<FateVue> fates)
    {
        var vues = fates.GroupBy(f => f.Id).ToDictionary(g => g.Key, g => g.First());
        var nouvelles = premierPassage ? [] : vues.Values.Where(f => !enCours.ContainsKey(f.Id)).ToList();
        enCours.Clear();
        foreach (var (id, f) in vues) enCours[id] = f;
        premierPassage = false;
        return nouvelles;
    }

    /// <summary>Oublie tout (changement de zone) : le prochain passage ne signalera rien.</summary>
    public void Reinitialiser()
    {
        enCours.Clear();
        premierPassage = true;
    }

    /// <summary>La FATE en cours la plus proche d'une position monde (plan X/Z), à moins de <paramref name="distanceMax"/>.</summary>
    public FateVue? PlusProche(Vector3 position, float distanceMax) =>
        enCours.Values
            .Select(f => (Fate: f, D: Vector2.Distance(new Vector2(f.Position.X, f.Position.Z), new Vector2(position.X, position.Z))))
            .Where(p => p.D <= distanceMax)
            .OrderBy(p => p.D)
            .Select(p => p.Fate)
            .FirstOrDefault();
}

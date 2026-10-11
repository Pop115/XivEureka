using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace XivEureka.Logos;

/// <summary>Une mneme (éclat Logos identifié), désignée par son numéro d'objet du jeu.</summary>
public sealed record Mneme(uint Objet, string Type);

/// <summary>Un éclat Logos non identifié et les mnemes qu'il peut donner, dans l'ordre du site.</summary>
public sealed record Eclat(uint Objet, IReadOnlyList<uint> Mnemes);

/// <summary>
/// Une action Logos. <see cref="Numero"/> est son numéro dans le journal (1 à 56), <see cref="Action"/>
/// sa ligne dans la feuille Action du jeu. Chaque recette liste ses mnemes, répétées si besoin.
/// <see cref="Roles"/> : T tank, H soigneur, M mêlée, P distance physique, C distance magique.
/// </summary>
public sealed record ActionLogos(int Numero, uint Action, string Roles, int? Utilisations, IReadOnlyList<IReadOnlyList<uint>> Recettes);

/// <summary>
/// Les données fixes des Logos : quelles mnemes existent et quelles recettes donnent quelle action.
/// Le jeu ne les a pas en local (le mélange est résolu par le serveur) : elles viennent de
/// ffxiv-eureka.com, recoupées avec Logogram Helper, dans <c>Donnees/logos.json</c>.
/// Noms, icônes et descriptions sont lus dans les feuilles du jeu, dans sa langue.
/// </summary>
public sealed class CatalogueLogos
{
    public IReadOnlyList<Eclat> Eclats { get; }
    public IReadOnlyDictionary<uint, Mneme> Mnemes { get; }
    public IReadOnlyList<ActionLogos> Actions { get; }

    public CatalogueLogos(IReadOnlyList<Eclat> eclats, IReadOnlyList<Mneme> mnemes, IReadOnlyList<ActionLogos> actions)
    {
        Eclats = eclats;
        Mnemes = mnemes.ToDictionary(m => m.Objet);
        Actions = actions;
    }

    public static CatalogueLogos Lire(Stream json)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var brut = JsonSerializer.Deserialize<Fichier>(json, options) ?? throw new InvalidDataException("logos.json vide");
        return new CatalogueLogos(
            brut.Eclats.Select(e => new Eclat(e.Objet, e.Mnemes)).ToList(),
            brut.Mnemes.Select(m => new Mneme(m.Objet, m.Type)).ToList(),
            brut.Actions.Select(a => new ActionLogos(a.Numero, a.Action, a.Roles, a.Utilisations,
                a.Recettes.Select(r => (IReadOnlyList<uint>)r).ToList())).ToList());
    }

    public static CatalogueLogos Integre()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var flux = assembly.GetManifestResourceStream("XivEureka.Donnees.logos.json")
            ?? throw new FileNotFoundException("Ressource logos.json absente de la DLL");
        return Lire(flux);
    }

    private sealed record Fichier(List<FichierEclat> Eclats, List<FichierMneme> Mnemes, List<FichierAction> Actions);
    private sealed record FichierEclat(uint Objet, List<uint> Mnemes);
    private sealed record FichierMneme(uint Objet, string Type);
    private sealed record FichierAction(int Numero, uint Action, string Roles, int? Utilisations, List<List<uint>> Recettes);
}

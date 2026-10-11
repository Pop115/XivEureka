using System.Text.Json;
using XivEureka.Logos;
using Xunit;

namespace XivEureka.Tests;

public class LecteurEclatsTests
{
    private static int[] ReleveReel() =>
        JsonSerializer.Deserialize<int[]>(File.ReadAllText(Path.Combine("TestData", "eclats-2026-10-11.json")))!;

    [Fact]
    public void Decode_le_releve_fait_en_jeu()
    {
        var stock = LecteurEclats.Decoder(ReleveReel())!;

        Assert.Equal(12, stock.Count);
        Assert.Equal(5, stock[24015]);   // Mémoire d'un prêtre guerrier
        Assert.Equal(16, stock[24031]);  // Tranquillisant
        Assert.Equal(1, stock[24034]);   // Extra Soin, dernière case
    }

    [Fact]
    public void Refuse_un_tableau_incoherent()
    {
        Assert.Null(LecteurEclats.Decoder([]));
        Assert.Null(LecteurEclats.Decoder([5, 0, 0, 1, 2, 3]));
    }

    [Fact]
    public void Toutes_les_mnemes_du_releve_sont_au_catalogue()
    {
        var catalogue = CatalogueLogos.Integre();
        Assert.All(LecteurEclats.Decoder(ReleveReel())!.Keys, objet => Assert.Contains(objet, catalogue.Mnemes.Keys));
    }
}

public class CatalogueTests
{
    private readonly CatalogueLogos catalogue = CatalogueLogos.Integre();

    [Fact]
    public void Contient_les_56_actions_et_28_mnemes()
    {
        Assert.Equal(Enumerable.Range(1, 56), catalogue.Actions.Select(a => a.Numero));
        Assert.Equal(28, catalogue.Mnemes.Count);
    }

    [Fact]
    public void Chaque_mneme_vient_d_un_seul_eclat()
    {
        var parEclat = catalogue.Eclats.SelectMany(e => e.Mnemes).ToList();
        Assert.Equal(parEclat.Count, parEclat.Distinct().Count());
        Assert.Equal(catalogue.Mnemes.Keys.Order(), parEclat.Order());
    }

    [Fact]
    public void Les_recettes_n_utilisent_que_des_mnemes_connues()
    {
        Assert.All(catalogue.Actions, a =>
        {
            Assert.NotEmpty(a.Recettes);
            Assert.All(a.Recettes.SelectMany(r => r), m => Assert.Contains(m, catalogue.Mnemes.Keys));
        });
    }
}

public class FabricationTests
{
    private static readonly ActionLogos Exemple = new(1, 100, "THMPC", 3, [[1, 1, 1], [1, 2]]);

    [Fact]
    public void Une_mneme_repetee_compte_autant_de_fois()
    {
        var recette = Fabrication.Recette([1, 1, 1], new Dictionary<uint, int> { [1] = 7 });
        Assert.Equal(2, recette.Possibles);
        Assert.Equal(0, recette.Manquantes);

        var manque = Fabrication.Recette([1, 1, 1], new Dictionary<uint, int> { [1] = 1 });
        Assert.Equal(0, manque.Possibles);
        Assert.Equal(2, manque.Manquantes);
    }

    [Fact]
    public void Une_action_prend_la_meilleure_recette_sans_additionner()
    {
        var etat = Fabrication.Action(Exemple, new Dictionary<uint, int> { [1] = 3, [2] = 5 }, new HashSet<uint>());
        Assert.Equal(3, etat.Possibles);
        Assert.True(etat.AApprendre);
    }

    [Fact]
    public void Une_action_apprise_n_est_plus_a_apprendre()
    {
        var etat = Fabrication.Action(Exemple, new Dictionary<uint, int> { [1] = 3 }, new HashSet<uint> { 100 });
        Assert.False(etat.AApprendre);
        Assert.Equal(1, etat.Possibles);
    }

    [Fact]
    public void La_recette_la_plus_proche_est_celle_qui_manque_le_moins()
    {
        var etat = Fabrication.Action(Exemple, new Dictionary<uint, int> { [1] = 1 }, new HashSet<uint>());
        Assert.Equal(0, etat.Possibles);
        Assert.Equal(1, etat.PlusProche.Manquantes);
        Assert.Equal([1u, 2u], etat.PlusProche.Recette);
    }

    [Fact]
    public void L_utilite_ignore_les_actions_apprises()
    {
        Assert.Equal(1, Fabrication.Utilite([Exemple], new HashSet<uint>())[2]);
        Assert.Empty(Fabrication.Utilite([Exemple], new HashSet<uint> { 100 }));
    }

    [Theory]
    [InlineData("Évocation Logos : Bouclier", "Bouclier")]
    [InlineData("Mémoire d'un prêtre guerrier", "Mémoire d'un prêtre guerrier")]
    [InlineData("Protect L", "Protect L")]
    public void Raccourcit_les_noms_a_prefixe(string nom, string attendu) =>
        Assert.Equal(attendu, TextesJeu.Raccourcir(nom));
}

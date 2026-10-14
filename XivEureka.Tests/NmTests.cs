using System.Numerics;
using XivEureka.Logos;
using XivEureka.Zones;
using Xunit;

namespace XivEureka.Tests;

public class CatalogueNmTests
{
    private static readonly NmZone Pyros = CatalogueNm.Integre().Zone(795)!;

    [Fact]
    public void Les_17_NM_de_Pyros_avec_leur_FATE()
    {
        Assert.Equal(17, Pyros.Nm.Count);
        Assert.Equal(120, Pyros.RespawnMinutes);
        // FATE 1388 à 1404 de la feuille Fate, une par NM ; les FATE lapin (1407, 1408) n'y sont pas.
        Assert.Equal(Enumerable.Range(1388, 17).Select(i => (ushort)i), Pyros.Nm.Select(n => n.Fate).Order());
        Assert.All(Pyros.Nm, n => Assert.NotEmpty(n.Prepa.Lieux));
    }

    [Fact]
    public void Chaque_NM_lache_un_eclat_connu_du_catalogue_Logos()
    {
        var eclats = CatalogueLogos.Integre().Eclats.Select(e => e.Objet).ToHashSet();
        Assert.All(Pyros.Nm, n => Assert.Contains(n.Eclat, eclats));
    }

    [Fact]
    public void Conditions_relevees_sur_le_tracker()
    {
        var skoll = Pyros.Nm.Single(n => n.Pnj == 7739);
        Assert.Equal("Blizzards", skoll.Condition.Meteo);
        Assert.True(skoll.Prepa.Condition.Aucune);

        var leucosia = Pyros.Nm.Single(n => n.Pnj == 7759);
        Assert.True(leucosia.Condition.Aucune);
        Assert.True(leucosia.Prepa.Condition.Nuit);

        // Les météos citées existent dans les tranches de Pyros (sinon la fenêtre ne serait jamais trouvée).
        var meteos = Meteo.Zone(795)!.Tranches.Select(t => t.Meteo).ToHashSet();
        Assert.All(Pyros.Nm.SelectMany(n => new[] { n.Condition.Meteo, n.Prepa.Condition.Meteo }).OfType<string>(),
            m => Assert.Contains(m, meteos));
    }
}

public class SituationsTests
{
    private static readonly ZoneEureka Pyros = Meteo.Zone(795)!;
    private static readonly DateTimeOffset Maintenant = DateTimeOffset.FromUnixTimeSeconds(1_760_000_000L);

    private static NmEureka Nm(uint pnj, ushort fate, Condition? condition = null, Condition? prepa = null, int niveau = 40, uint eclat = 24009) =>
        new(pnj, 0, fate, niveau, "fire", new Vector2(20, 20), eclat, [], condition ?? new Condition(null, false),
            new Prepa(1, niveau + 5, prepa ?? new Condition(null, false), [new Vector2(21, 21)]));

    [Fact]
    public void Fenetre_meteo_couvre_les_periodes_consecutives()
    {
        foreach (var (_, meteo) in Pyros.Tranches)
        {
            var f = Situations.ProchaineFenetre(Pyros, new Condition(meteo, false), Maintenant)!;
            var periodes = Meteo.Periodes(Pyros, f.Debut).TakeWhile(p => p.Debut < f.Fin).ToList();
            Assert.All(periodes, p => Assert.Equal(meteo, p.Meteo));
            Assert.NotEqual(meteo, Meteo.Periodes(Pyros, f.Fin).First().Meteo);
            Assert.True(f.Fin > Maintenant);
        }
    }

    [Fact]
    public void Fenetre_de_nuit()
    {
        var minuit = DateTimeOffset.FromUnixTimeSeconds(0);
        var enCours = Situations.ProchaineFenetre(Pyros, new Condition(null, true), minuit)!;
        Assert.True(enCours.EnCours(minuit));
        Assert.Equal(6 * 175, enCours.Fin.ToUnixTimeSeconds());

        var midi = DateTimeOffset.FromUnixTimeSeconds(12 * 175);
        var aVenir = Situations.ProchaineFenetre(Pyros, new Condition(null, true), midi)!;
        Assert.Equal(19 * 175, aVenir.Debut.ToUnixTimeSeconds());
        Assert.Equal((24 + 6) * 175, aVenir.Fin.ToUnixTimeSeconds());
    }

    [Fact]
    public void Respawn_deux_heures_apres_la_fin_de_la_FATE()
    {
        var zone = new NmZone(795, 120, [Nm(10, 500)]);
        var fin = Maintenant.UtcDateTime.AddMinutes(-30);
        var s = Situations.Calculer(Pyros, zone, [], new Dictionary<ushort, DateTime> { [500] = fin }, new HashSet<ushort>(), Maintenant).Single();
        Assert.True(s.EnRespawn(Maintenant));
        Assert.Equal(Maintenant.AddMinutes(90), s.RespawnAuPlusTot);
        Assert.False(s.EnRespawn(Maintenant.AddMinutes(91)));
    }
}

public class ConseilsTests
{
    private static readonly ZoneEureka Pyros = Meteo.Zone(795)!;
    private static readonly NomsJeu Noms = new(p => $"NM{p}", m => m, o => $"Objet{o}");

    private static NmEureka Nm(uint pnj, int niveau, uint eclat = 24009, Condition? condition = null) =>
        new(pnj, 0, (ushort)(1000 + pnj), niveau, "fire", new Vector2(pnj, 10), eclat, [], condition ?? new Condition(null, false),
            new Prepa(pnj + 100, niveau + 5, new Condition(null, false), [new Vector2(pnj, 12)]));

    private static SituationNm Libre(NmEureka nm) => new(nm, null, null, null, null);

    [Fact]
    public void La_FATE_en_cours_passe_en_premier()
    {
        var maintenant = DateTimeOffset.UtcNow;
        var fate = new FateCarte(1001, "F", Vector3.Zero, 20, 35, TimeSpan.FromMinutes(24), 0, false);
        var conseils = Conseils.Calculer([new SituationNm(Nm(1, 40), fate, null, null, null), Libre(Nm(2, 40))],
            maintenant, 40, new Dictionary<uint, int>(), Noms);
        Assert.Equal("NM1 – FATE en cours", conseils[0].Titre);
        Assert.Contains("35 %", conseils[0].Texte);
    }

    [Fact]
    public void Une_meteo_dans_la_demi_heure_donne_la_prepa()
    {
        var maintenant = DateTimeOffset.UtcNow;
        var dans20 = new Fenetre(maintenant.AddMinutes(20), maintenant.AddMinutes(43));
        var dans50 = new Fenetre(maintenant.AddMinutes(50), maintenant.AddMinutes(73));
        var conseils = Conseils.Calculer(
            [new SituationNm(Nm(1, 50, condition: new Condition("Blizzards", false)), null, null, dans20, null),
             new SituationNm(Nm(2, 46, condition: new Condition("Thunder", false)), null, null, dans50, null)],
            maintenant, null, new Dictionary<uint, int>(), Noms);
        var c = Assert.Single(conseils);
        Assert.Equal("NM1 – Blizzards dans 20 min", c.Titre);
        Assert.Equal("Prépa : NM101 (Nv 55) vers (1.0, 12.0).", c.Texte);
        Assert.Equal(new Vector2(1, 12), c.Coordonnees);
    }

    [Fact]
    public void Niveau_et_eclats_ignorent_les_NM_en_respawn_ou_en_attente()
    {
        var maintenant = DateTimeOffset.UtcNow;
        var respawn = new SituationNm(Nm(1, 40), null, maintenant.AddMinutes(30), null, null);
        var attente = new SituationNm(Nm(2, 41, condition: new Condition("Thunder", false)), null, null,
            new Fenetre(maintenant.AddHours(2), maintenant.AddHours(3)), null);
        var libre = Libre(Nm(3, 42, eclat: 24011));
        var conseils = Conseils.Calculer([respawn, attente, libre], maintenant, 40,
            new Dictionary<uint, int> { [24009] = 5, [24011] = 2 }, Noms);

        var niveau = conseils.Single(c => c.Titre.StartsWith("À ton niveau"));
        Assert.Equal("Disponibles : NM3 (Nv 42).", niveau.Texte);
        var logos = Assert.Single(conseils, c => c.Titre.StartsWith("Logos"));
        Assert.Equal("Logos – Objet24011", logos.Titre); // 24009 n'a pas de source disponible
    }
}

public class EclatsUtilesTests
{
    [Fact]
    public void Compte_les_actions_que_chaque_eclat_ferait_avancer()
    {
        var catalogue = new CatalogueLogos(
            [new Eclat(1, [100, 101]), new Eclat(2, [200])],
            [new Mneme(100, "a"), new Mneme(101, "b"), new Mneme(200, "c")],
            [
                new ActionLogos(1, 9001, "T", null, [[100, 200]]), // manque 100 et 200 → éclats 1 et 2
                new ActionLogos(2, 9002, "T", null, [[101]]),      // fabricable → ignorée
                new ActionLogos(3, 9003, "T", null, [[200, 200]]), // apprise → ignorée
            ]);
        var stock = new Dictionary<uint, int> { [101] = 1 };
        var utiles = Fabrication.EclatsUtiles(catalogue, stock, new HashSet<uint> { 9003 });
        Assert.Equal(1, utiles[1]);
        Assert.Equal(1, utiles[2]);
    }
}

public class FatesTermineesTests
{
    private static FateVue Fate(ushort id) => new(id, $"F{id}", Vector3.Zero);

    [Fact]
    public void Retient_les_FATE_vues_finir_et_les_oublie_si_elles_reviennent()
    {
        var suivi = new SuiviFates();
        var t0 = new DateTime(2026, 10, 14, 20, 0, 0, DateTimeKind.Utc);
        suivi.MettreAJour([Fate(1), Fate(2)], t0);        // déjà là à l'arrivée
        suivi.MettreAJour([Fate(2)], t0.AddSeconds(5));     // la 1 finit
        Assert.Equal(t0.AddSeconds(5), suivi.Terminees[1]);
        Assert.False(suivi.Terminees.ContainsKey(2));

        suivi.MettreAJour([Fate(1), Fate(2)], t0.AddHours(2)); // la 1 revient
        Assert.False(suivi.Terminees.ContainsKey(1));

        suivi.Reinitialiser();
        Assert.Empty(suivi.Terminees);
    }
}

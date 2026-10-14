using System.Numerics;
using XivEureka.Zones;
using Xunit;

namespace XivEureka.Tests;

public class MeteoTests
{
    private static readonly ZoneEureka Pyros = Meteo.Zone(795)!;

    [Fact]
    public void Valeur_connue_a_l_epoque_unix()
    {
        // Calcul à la main : heure 0 → incrément 8, jour 0 → base 8 ; (8<<11)^8 = 16392 ; (16392>>8)^16392 = 16456.
        Assert.Equal(56, Meteo.Valeur(DateTimeOffset.FromUnixTimeSeconds(0)));
    }

    [Fact]
    public void La_meteo_ne_change_qu_aux_limites_de_periode()
    {
        var debut = DateTimeOffset.FromUnixTimeSeconds(1_760_000_000L / 1400 * 1400);
        var valeur = Meteo.Valeur(debut);
        Assert.Equal(valeur, Meteo.Valeur(debut.AddSeconds(1399)));
        var periodes = Meteo.Periodes(Pyros, debut.AddSeconds(700)).Take(3).ToList();
        Assert.Equal(debut, periodes[0].Debut);
        Assert.Equal(periodes[0].Fin, periodes[1].Debut);
        Assert.Equal(TimeSpan.FromSeconds(1400), periodes[2].Fin - periodes[2].Debut);
    }

    [Fact]
    public void Chaque_meteo_de_chaque_zone_revient_dans_la_semaine()
    {
        var maintenant = DateTimeOffset.FromUnixTimeSeconds(1_760_000_000L);
        foreach (var zone in Meteo.Zones)
            foreach (var (_, meteo) in zone.Tranches)
                Assert.Equal(meteo, Meteo.Prochaine(zone, meteo, maintenant)!.Meteo);
    }

    [Fact]
    public void Tranches_aux_bornes()
    {
        Assert.Equal("Fair Skies", Pyros.MeteoPour(0));
        Assert.Equal("Fair Skies", Pyros.MeteoPour(9));
        Assert.Equal("Heat Waves", Pyros.MeteoPour(10));
        Assert.Equal("Snow", Pyros.MeteoPour(99));
    }

    [Fact]
    public void Heure_et_nuit_d_Eorzea()
    {
        // 175 s réelles = une heure d'Eorzéa ; l'époque unix tombe à minuit.
        Assert.Equal((0, 0), Meteo.HeureEorzea(DateTimeOffset.FromUnixTimeSeconds(0)));
        Assert.Equal((6, 0), Meteo.HeureEorzea(DateTimeOffset.FromUnixTimeSeconds(6 * 175)));
        Assert.True(Meteo.Nuit(DateTimeOffset.FromUnixTimeSeconds(5 * 175)));
        Assert.False(Meteo.Nuit(DateTimeOffset.FromUnixTimeSeconds(6 * 175)));
        Assert.True(Meteo.Nuit(DateTimeOffset.FromUnixTimeSeconds(19 * 175)));
    }
}

public class AnalyseChatTests
{
    private static readonly string[] AucuneFate = [];

    [Theory]
    [InlineData("tracker https://ffxiv-eureka.com/bZFR-s merci", "bZFR-s")]
    [InlineData("ffxiv-eureka.com/v/aB3_x9 ", "aB3_x9")]
    [InlineData("pas de lien ici", null)]
    public void Repere_les_liens_de_tracker(string texte, string? attendu) =>
        Assert.Equal(attendu, AnalyseChat.Analyser(texte, AucuneFate).Tracker);

    [Theory]
    [InlineData("Skoll pop X: 12.3 Y: 14.5", 12.3f, 14.5f)]
    [InlineData("pop (21.4, 26.1)", 21.4f, 26.1f)]
    [InlineData("pop x 8,5 y 30", 8.5f, 30f)]
    public void Lit_les_coordonnees(string texte, float x, float y)
    {
        var lu = AnalyseChat.Analyser(texte, AucuneFate);
        Assert.True(lu.Pop);
        Assert.Equal(new Vector2(x, y), lu.Position);
    }

    [Fact]
    public void Ignore_les_coordonnees_hors_carte_et_le_lien_du_tracker()
    {
        Assert.Null(AnalyseChat.Analyser("pop (80, 12)", AucuneFate).Position);
        Assert.Null(AnalyseChat.Analyser("ffxiv-eureka.com/X12Y14", AucuneFate).Position);
    }

    [Fact]
    public void Reconnait_une_fate_en_cours_par_son_nom()
    {
        var lu = AnalyseChat.Analyser("go go Ice Scream !!", ["Ice Scream", "Eternity"]);
        Assert.True(lu.Pop);
        Assert.Equal("Ice Scream", lu.Fate);
    }

    [Theory]
    [InlineData("level up enfin")]
    [InlineData("population de lapins")]
    public void Pas_de_faux_pop(string texte) => Assert.False(AnalyseChat.Analyser(texte, AucuneFate).Pop);
}

public class SuiviFatesTests
{
    private static FateVue Fate(ushort id, float x = 0, float z = 0) => new(id, $"F{id}", new Vector3(x, 10, z));

    [Fact]
    public void Ne_signale_que_les_nouvelles_et_pas_celles_deja_la_a_l_arrivee()
    {
        var suivi = new SuiviFates();
        Assert.Empty(suivi.MettreAJour([Fate(1)]));
        Assert.Empty(suivi.MettreAJour([Fate(1)]));
        Assert.Equal([2], suivi.MettreAJour([Fate(1), Fate(2)]).Select(f => (int)f.Id));
        Assert.Empty(suivi.MettreAJour([Fate(2)]));

        suivi.Reinitialiser();
        Assert.Empty(suivi.MettreAJour([Fate(3)]));
    }

    [Fact]
    public void Trouve_la_fate_la_plus_proche_dans_le_plan()
    {
        var suivi = new SuiviFates();
        suivi.MettreAJour([Fate(1, 0, 0), Fate(2, 100, 0)]);
        Assert.Equal(2, suivi.PlusProche(new Vector3(90, -50, 5), 60)!.Id);
        Assert.Null(suivi.PlusProche(new Vector3(50, 0, 200), 60));
    }

    [Theory]
    [InlineData(4, true)]   // We're All Mad Here, Uncommon Nonsense… (feuille Fate, colonne Rule)
    [InlineData(1, false)]  // FATE de NM
    [InlineData(5, false)]  // Color My World (Anémos)
    public void Reconnait_les_fates_lapin_a_leur_regle(byte regle, bool lapin) =>
        Assert.Equal(lapin, SuiviFates.EstFateLapin(regle));
}

public class GeometrieCarteTests
{
    private static readonly GeometrieCarte Pyros = new(100, 0, 0);

    [Fact]
    public void Repere_du_jeu_aux_coordonnees_du_wiki()
    {
        // Éthérite de Firing Chamber : MapMarker 335 (pixels 1151, 827) ; le wiki dit X 23.8 Y 17.0.
        var coord = Pyros.Coordonnees(new Vector2(1151, 827));
        Assert.Equal(24.02f, coord.X, 2);
        Assert.Equal(17.54f, coord.Y, 2);
    }

    [Fact]
    public void Le_centre_du_monde_est_au_centre_de_la_texture()
    {
        Assert.Equal(new Vector2(1024, 1024), Pyros.Pixel(Vector3.Zero));
        Assert.Equal(new Vector2(1024, 1024), new GeometrieCarte(200, 0, 0).Pixel(Vector3.Zero));
        Assert.Equal(new Vector2(1024, 1024 + 475), new GeometrieCarte(100, 0, 475).Pixel(Vector3.Zero)); // Hydatos
    }

    [Theory]
    [InlineData(100, 0, 0)]
    [InlineData(200, 0, -754)]
    public void Aller_retour_monde_pixel_coordonnees(ushort echelle, short dx, short dy)
    {
        var g = new GeometrieCarte(echelle, dx, dy);
        var monde = new Vector3(-123.5f, 40, 287.25f);
        var pixel = g.Pixel(monde);
        var retour = g.Monde(pixel);
        Assert.Equal(monde.X, retour.X, 3);
        Assert.Equal(monde.Z, retour.Z, 3);
        Assert.True(Vector2.Distance(pixel, g.PixelDeCoordonnees(g.Coordonnees(pixel))) < 1e-3f);
    }

    [Fact]
    public void Meme_formule_que_le_lien_de_carte_du_jeu()
    {
        // MapUtil de Dalamud : 0.02 × décalage + 2048 / échelle + 0.02 × valeur + 1.
        var g = new GeometrieCarte(200, 0, -754);
        var monde = new Vector3(310f, 0, -95f);
        var attendu = (0.02f * -754) + (2048f / 200) + (0.02f * -95f) + 1f;
        Assert.Equal(attendu, g.Coordonnees(g.Pixel(monde)).Y, 3);
    }
}

public class BasculeJourNuitTests
{
    [Theory]
    [InlineData(0, false, 6)]        // minuit → le jour à 6 h
    [InlineData(6 * 175, true, 19)]  // 6 h pile → la nuit à 19 h
    [InlineData(19 * 175, false, 30)] // 19 h → le jour à 6 h le lendemain
    public void Prochaine_bascule(long secondes, bool nuit, int heureEorzea)
    {
        var (n, quand) = Meteo.ProchaineBascule(DateTimeOffset.FromUnixTimeSeconds(secondes));
        Assert.Equal(nuit, n);
        Assert.Equal(heureEorzea * 175L, quand.ToUnixTimeSeconds());
    }
}

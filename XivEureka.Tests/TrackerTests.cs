using System.Text.Json;
using XivEureka.Tracker;
using Xunit;

namespace XivEureka.Tests;

/// <summary>Messages réels du serveur de ffxiv-eureka.com, relevés le 2026-10-14 sur un tracker créé pour l'essai.</summary>
public class FormatTrackerTests
{
    private const string Initial =
        """[null,null,"instance:ZduYe9","initial_payload",{"data":{"attributes":{"data-center-id":null,"instance-id":null,"notorious-monsters":"{}","password":"vampires-system-fuchsia","prepped-notorious-monsters":"{}"},"id":"ZduYe9","relationships":{"zone":{"data":{"id":3,"type":"zone"}}},"type":"instance"}}]""";

    private const string Mort =
        """[null,null,"instance:ZduYe9","payload",{"data":{"attributes":{"notorious-monsters":"{\"38\":1792015799985}","updated-at":"2026-10-14T22:09:58.334690Z","prepped-notorious-monsters":"{\"38\":false}"},"id":"ZduYe9","type":"instance"}}]""";

    private const string Annulation =
        """[null,null,"instance:ZduYe9","payload",{"data":{"attributes":{"notorious-monsters":"{}","updated-at":"2026-10-14T22:09:58.438205Z"},"id":"ZduYe9","type":"instance"}}]""";

    [Fact]
    public void Lit_le_tracker_puis_ses_mises_a_jour()
    {
        var initial = FormatTracker.Lire(Initial)!;
        Assert.Equal("initial_payload", initial.Evenement);
        var t = FormatTracker.LireInstance(initial.Charge.GetProperty("data"));
        Assert.Equal("ZduYe9", t.Id);
        Assert.Equal(3, t.Zone);
        Assert.Equal("vampires-system-fuchsia", t.MotDePasse);
        Assert.Empty(t.Morts);

        t = FormatTracker.Fusionner(t, FormatTracker.Lire(Mort)!.Charge.GetProperty("data"));
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1792015799985), t.Morts[38]);
        Assert.Empty(t.Prepares);              // « false » n'est pas préparé
        Assert.Equal(3, t.Zone);               // absent de la mise à jour : gardé
        Assert.Equal("vampires-system-fuchsia", t.MotDePasse);

        t = FormatTracker.Fusionner(t, FormatTracker.Lire(Annulation)!.Charge.GetProperty("data"));
        Assert.Empty(t.Morts);
    }

    [Fact]
    public void Ecrit_les_messages_comme_le_site()
    {
        Assert.Equal("""["1","1","instance:ZduYe9","phx_join",{}]""", FormatTracker.Ecrire("1", "1", "instance:ZduYe9", "phx_join", new { }));
        Assert.Equal("""[null,"5","phoenix","heartbeat",{}]""", FormatTracker.Ecrire(null, "5", "phoenix", "heartbeat", new { }));
        using var corps = JsonDocument.Parse(FormatTracker.CorpsCreation(3));
        Assert.Equal("3", corps.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("zone-id").GetString());
    }

    [Fact]
    public void Message_illisible_ignore() => Assert.Null(FormatTracker.Lire("pas du json"));

    [Fact]
    public void Zones_et_data_centers_du_site()
    {
        Assert.Equal(3, FormatTracker.ZoneSite(795));
        Assert.Null(FormatTracker.ZoneSite(132));
        Assert.Equal(11, FormatTracker.DataCenters["Light"]);
        Assert.Equal(6, FormatTracker.DataCenters["chaos"]);
    }

    [Fact]
    public void Creation_seulement_a_Eureka_sans_tracker()
    {
        Assert.True(SuiviTracker.CreationAttendue(aEureka: true, trackerSuivi: false, creationEnCours: false, reglage: true));
        Assert.False(SuiviTracker.CreationAttendue(false, false, false, true));
        Assert.False(SuiviTracker.CreationAttendue(true, true, false, true));
        Assert.False(SuiviTracker.CreationAttendue(true, false, true, true));
        Assert.False(SuiviTracker.CreationAttendue(true, false, false, false));
    }
}

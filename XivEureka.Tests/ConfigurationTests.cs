using Xunit;

namespace XivEureka.Tests;

public class ConfigurationMigrationTests
{
    /// <summary>A version 1 file as Dalamud's Newtonsoft writes it ($type everywhere, sets as $values); made-up data.</summary>
    private const string Legacy = """
        {
          "$type": "XivEureka.Configuration, XivEureka",
          "Version": 1,
          "Personnages": {
            "$type": "System.Collections.Generic.Dictionary`2[[System.UInt64, System.Private.CoreLib],[XivEureka.EtatPersonnage, XivEureka]], System.Private.CoreLib",
            "123456789": {
              "$type": "XivEureka.EtatPersonnage, XivEureka",
              "Nom": "Test Character",
              "Stock": { "$type": "System.Collections.Generic.Dictionary`2[[System.UInt32],[System.Int32]]", "24015": 5, "24031": 18 },
              "StockLuLe": "2026-10-11T23:31:40.3963803+02:00",
              "Apprises": { "$type": "System.Collections.Generic.HashSet`1[[System.UInt32]]", "$values": [12958, 12959] },
              "ApprisesLuesLe": "2026-10-11T23:27:59.3292043+02:00"
            }
          },
          "OuvrirAvecLeManipulateur": false,
          "AlerterFates": true,
          "DrapeauSurFate": false,
          "EcouterChat": true,
          "DrapeauSurChat": true,
          "SonAlertes": false,
          "Calques": { "$type": "XivEureka.CalquesCarte, XivEureka", "Reperes": false, "NomsDeZone": true, "Groupe": true, "Fates": true, "Pops": true, "Nm": true, "Prepa": false },
          "DerniereZone": 763,
          "SuivreTrackerCrie": true,
          "CreerTrackerAuto": false,
          "SignalerMorts": true,
          "TrackerEnCours": { "$type": "XivEureka.TrackerSauve, XivEureka", "Id": "ZduYe9", "Territoire": 795, "Depuis": "2026-10-14T22:09:46Z" },
          "MotsDePasseTrackers": { "$type": "System.Collections.Generic.Dictionary`2[[System.String],[System.String]]", "ZduYe9": "some-pass-word" },
          "DernierTracker": "bZFR-s"
        }
        """;

    [Fact]
    public void Recognizes_a_version_1_file()
    {
        Assert.True(ConfigurationMigration.IsLegacy(Legacy));
        Assert.True(ConfigurationMigration.IsLegacy("""{ "Personnages": {} }"""));
        Assert.False(ConfigurationMigration.IsLegacy("""{ "Version": 2 }"""));
        Assert.False(ConfigurationMigration.IsLegacy("not json"));
    }

    [Fact]
    public void Carries_every_value_across()
    {
        var c = ConfigurationMigration.FromLegacy(Legacy);

        var character = c.Characters[123456789];
        Assert.Equal("Test Character", character.Name);
        Assert.Equal(new Dictionary<uint, int> { [24015] = 5, [24031] = 18 }, character.Stock);
        Assert.Equal([12958u, 12959u], character.Learned.Order());
        Assert.NotNull(character.StockReadAt);
        Assert.NotNull(character.LearnedReadAt);

        Assert.False(c.OpenWithManipulator);
        Assert.False(c.FlagOnFate);
        Assert.False(c.AlertSound);
        Assert.False(c.AutoCreateTracker);
        Assert.False(c.Layers.Points);
        Assert.False(c.Layers.Prep);
        Assert.True(c.Layers.Nms);
        Assert.Equal(763u, c.LastZone);
        Assert.Equal("bZFR-s", c.LastTracker);
        Assert.Equal("some-pass-word", c.TrackerPasswords["ZduYe9"]);
        Assert.Equal("ZduYe9", c.CurrentTracker!.Id);
        Assert.Equal(795u, c.CurrentTracker.Territory);
        Assert.Equal(2, c.Version);
    }
}

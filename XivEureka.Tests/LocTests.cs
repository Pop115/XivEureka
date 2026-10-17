using System.Numerics;
using Dalamud.Game;
using XivEureka.Logos;
using XivEureka.Zones;
using Xunit;

namespace XivEureka.Tests;

/// <summary>
/// The plugin language is global (<see cref="Loc"/>): tests that switch it run alone, after the
/// parallel ones (which all read French, the default).
/// </summary>
[CollectionDefinition("Language", DisableParallelization = true)]
public sealed class LanguageCollection;

[Collection("Language")]
public sealed class LocTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_760_000_000L);

    public void Dispose() => Loc.Set(false);

    [Theory]
    [InlineData(TextLanguage.Auto, ClientLanguage.French, false)]
    [InlineData(TextLanguage.Auto, ClientLanguage.English, true)]
    [InlineData(TextLanguage.Auto, ClientLanguage.German, true)]
    [InlineData(TextLanguage.Auto, ClientLanguage.Japanese, true)]
    [InlineData(TextLanguage.French, ClientLanguage.English, false)]
    [InlineData(TextLanguage.English, ClientLanguage.French, true)]
    public void French_for_a_French_client_English_otherwise_unless_forced(TextLanguage setting, ClientLanguage client, bool english)
    {
        Loc.Apply(setting, client);
        Assert.Equal(english, Loc.English);
        Assert.Equal(english ? "Map" : "Carte", Loc.T("Carte", "Map"));
    }

    [Fact]
    public void NM_states_in_English()
    {
        Loc.Set(true);
        var nm = new EurekaNm(1, 0, 1, 40, "fire", new Vector2(10, 10), 24009, [], new SpawnCondition(null, true),
            new PrepMob(2, 45, new SpawnCondition(null, false), [new Vector2(11, 11)]));
        var waiting = new NmStatus(nm, null, null, new TimeWindow(Now.AddMinutes(8), Now.AddMinutes(40)), null);
        Assert.Equal("night in 8 min", waiting.Describe(Now, w => w));
        Assert.Equal("prepped", (waiting with { Prepped = true }).Describe(Now, w => w));
        Assert.Equal("available", new NmStatus(nm, null, null, null, null).Describe(Now, w => w));
        var names = new GameNames(id => $"Mob{id}", w => w, id => $"Item{id}");
        Assert.Equal("Prep: Mob2 (lv 45) around (11.0, 11.0).", Suggestions.PrepText(nm, names));
    }

    [Fact]
    public void Hunt_notes_in_English()
    {
        Loc.Set(true);
        var bunny = new Farm(FarmKind.Bunny, 0, 1407, 35, false, [], [new Vector2(24, 26)],
            [new FarmDrop(24010, false, "gold", "5-11", 0.039)]);
        var shard = new HuntShard(24010, [], [], []);
        var option = HuntOptions.For(shard, new FarmCatalog([new ZoneFarms(795, [bunny])]),
            new HuntContext(795, [], new HashSet<ushort>(), 50, Now, 2)).Single();
        Assert.Contains("gold coffer", option.Notes);
        Assert.Contains(option.Notes, n => n.StartsWith("the bunny comes back", StringComparison.Ordinal));
        Assert.Equal("Anemos", EurekaWeather.Zone(732)!.Name);
    }

    [Fact]
    public void French_stays_the_default()
    {
        Loc.Set(false);
        Assert.Equal("Anémos", EurekaWeather.Zone(732)!.Name);
        Assert.Equal("disponible", new NmStatus(
            new EurekaNm(1, 0, 1, 40, "fire", Vector2.Zero, 0, [], new SpawnCondition(null, false),
                new PrepMob(2, 45, new SpawnCondition(null, false), [])), null, null, null, null).Describe(Now, w => w));
    }
}

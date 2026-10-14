using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace XivEureka.Logos;

/// <summary>A mneme (identified Logos shard), by its game item id.</summary>
public sealed record Mneme(uint Item, string Type);

/// <summary>An unidentified Logos shard and the mnemes it can yield, in the site's order.</summary>
public sealed record Shard(uint Item, IReadOnlyList<uint> Mnemes);

/// <summary>
/// A Logos action. <see cref="Number"/> is its position in the notebook (1 to 56), <see cref="Action"/>
/// its row in the game's Action sheet. Each recipe lists its mnemes, repeated when needed.
/// <see cref="Roles"/>: T tank, H healer, M melee, P physical ranged, C caster.
/// </summary>
public sealed record LogosAction(int Number, uint Action, string Roles, int? Uses, IReadOnlyList<IReadOnlyList<uint>> Recipes);

/// <summary>
/// The fixed Logos data: which mnemes exist and which recipes give which action. The game does not
/// have it locally (the mix is resolved server-side): it comes from ffxiv-eureka.com, cross-checked
/// with Logogram Helper, in <c>Data/logos.json</c>. Names, icons and descriptions are read from the
/// game sheets, in the client language.
/// </summary>
public sealed class LogosCatalog
{
    public IReadOnlyList<Shard> Shards { get; }
    public IReadOnlyDictionary<uint, Mneme> Mnemes { get; }
    public IReadOnlyList<LogosAction> Actions { get; }

    public LogosCatalog(IReadOnlyList<Shard> shards, IReadOnlyList<Mneme> mnemes, IReadOnlyList<LogosAction> actions)
    {
        Shards = shards;
        Mnemes = mnemes.ToDictionary(m => m.Item);
        Actions = actions;
    }

    public static LogosCatalog Read(Stream json)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var raw = JsonSerializer.Deserialize<RawFile>(json, options) ?? throw new InvalidDataException("logos.json is empty");
        return new LogosCatalog(
            raw.Shards.Select(s => new Shard(s.Item, s.Mnemes)).ToList(),
            raw.Mnemes.Select(m => new Mneme(m.Item, m.Type)).ToList(),
            raw.Actions.Select(a => new LogosAction(a.Number, a.Action, a.Roles, a.Uses,
                a.Recipes.Select(r => (IReadOnlyList<uint>)r).ToList())).ToList());
    }

    public static LogosCatalog Embedded()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("XivEureka.Data.logos.json")
            ?? throw new FileNotFoundException("logos.json resource missing from the DLL");
        return Read(stream);
    }

    private sealed record RawFile(List<RawShard> Shards, List<RawMneme> Mnemes, List<RawAction> Actions);
    private sealed record RawShard(uint Item, List<uint> Mnemes);
    private sealed record RawMneme(uint Item, string Type);
    private sealed record RawAction(int Number, uint Action, string Roles, int? Uses, List<List<uint>> Recipes);
}

using XivEureka.Progress;
using Xunit;

namespace XivEureka.Tests;

public class RelicCatalogTests
{
    private static readonly RelicCatalog Catalog = RelicCatalog.Embedded();

    [Fact]
    public void Sixteen_weapon_stages_and_eight_armor_stages_as_on_the_wiki()
    {
        Assert.Equal(16, Catalog.Weapon.Count);
        Assert.Equal(8, Catalog.Armor.Count);
        Assert.Equal("physeos", Catalog.Weapon[^1].Key);
        Assert.All(Catalog.Weapon, s => Assert.Equal(16, s.Count));
        Assert.Equal([75, 75, 75, 75, 75, 35, 35, 35], Catalog.Armor.Select(s => s.Count));
    }

    [Fact]
    public void Blocks_do_not_overlap_and_item_levels_never_go_down()
    {
        var all = Catalog.Weapon.Concat(Catalog.Armor).SelectMany(s => s.Items).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
        foreach (var line in new[] { Catalog.Weapon, Catalog.Armor })
            for (var i = 1; i < line.Count; i++)
                Assert.True(line[i].ItemLevel >= line[i - 1].ItemLevel, line[i].Key);
    }

    [Fact]
    public void Every_cost_is_a_known_material_and_the_totals_match_the_wiki()
    {
        var costs = Catalog.Weapon.Concat(Catalog.Armor).SelectMany(s => s.Cost.Concat(s.BodyLegsCost ?? []));
        Assert.All(costs, c => Assert.Contains(c.Item, Catalog.Materials));
        int Total(uint item, int from, int to) =>
            Catalog.Weapon.Skip(from).Take(to - from).SelectMany(s => s.Cost).Where(c => c.Item == item).Sum(c => c.Count);
        Assert.Equal(1300, Total(21801, 1, 4));   // Protean crystals, Anemos weapons
        Assert.Equal(31, Total(23309, 5, 8));     // Frosted protean crystals, Elemental weapons
        Assert.Equal(650, Total(24124, 8, 11));   // Pyros crystals, Pyros weapons
        Assert.Equal(350, Total(24807, 11, 15));  // Hydatos crystals, Eureka weapons
        Assert.Equal([10, 20, 30], Catalog.Weapon.Skip(8).Take(3).Select(s => s.Logos));
    }

    [Fact]
    public void Story_four_zones_opened_by_their_quests()
    {
        Assert.Equal([732u, 763u, 795u, 827u], Catalog.Story.Select(z => z.Territory));
        Assert.Equal([68614u, 68478u, 68148u, 68149u], Catalog.Story.Select(z => z.AccessQuest));
        Assert.Equal([35, 38, 40, 45, 50], Catalog.Story[2].Steps.Select(s => s.Level));
    }
}

public class RelicProgressTests
{
    private static readonly RelicCatalog Catalog = RelicCatalog.Embedded();

    /// <summary>The weapon blocks' job order, as in the game (the 16th item is the paladin shield).</summary>
    private static readonly string[] WeaponJobs =
        ["PLD", "MNK", "WAR", "DRG", "BRD", "NIN", "DRK", "MCH", "WHM", "BLM", "SMN", "SCH", "AST", "SAM", "RDM", "PLD"];

    private static readonly string[][] Roles =
    [
        ["PLD", "WAR", "DRK"], ["DRG"], ["MNK", "SAM"], ["BRD", "MCH"], ["NIN"], ["WHM", "SCH", "AST"], ["BLM", "SMN", "RDM"],
    ];

    private static readonly RelicSlot[] Pieces = [RelicSlot.Head, RelicSlot.Body, RelicSlot.Hands, RelicSlot.Legs, RelicSlot.Feet];

    /// <summary>A fake Item sheet laid out like the real one.</summary>
    private static RelicItemInfo Info(uint item)
    {
        foreach (var s in Catalog.Weapon.Where(s => s.Contains(item)))
        {
            var i = (int)(item - s.First);
            return new RelicItemInfo(new HashSet<string> { WeaponJobs[i] }, i == 15 ? RelicSlot.OffHand : RelicSlot.MainHand);
        }
        foreach (var s in Catalog.Armor.Where(s => s.Contains(item)))
        {
            var i = (int)(item - s.First);
            var jobs = s.Count == 75 ? new HashSet<string> { WeaponJobs[i / 5] } : Roles[i / 5].ToHashSet();
            return new RelicItemInfo(jobs, Pieces[i % 5]);
        }
        return new RelicItemInfo(new HashSet<string>(), null);
    }

    private static uint Weapon(string key, string job) => Catalog.Weapon.Single(s => s.Key == key).First + (uint)Array.IndexOf(WeaponJobs, job);

    [Fact]
    public void A_paladin_with_a_Pyros_sword_goes_to_Hydatos_next_and_its_shield_counts_apart()
    {
        var owned = new HashSet<uint> { Weapon("antiquated", "PLD"), Weapon("pyros", "PLD"), 24086 /* Pyros shield */ };
        var slots = RelicProgress.For(Catalog.Weapon, "PLD", owned, Info);
        Assert.Equal([RelicSlot.MainHand, RelicSlot.OffHand], slots.Select(s => s.Slot));
        var sword = slots[0];
        Assert.Equal(10, sword.Stage);
        Assert.Equal(24071u, sword.Owned);
        Assert.Equal(11, sword.NextStage);
        Assert.Equal(24643u, sword.NextItem);
        Assert.Equal([new RelicCost(24807, 50)], sword.NextCost);
        Assert.Equal(10, slots[1].Stage);
    }

    [Fact]
    public void Nothing_seen_starts_at_the_antiquated_weapon_and_Physeos_is_the_end()
    {
        var none = RelicProgress.For(Catalog.Weapon, "MNK", new HashSet<uint>(), Info).Single();
        Assert.Null(none.Stage);
        Assert.Equal(0, none.NextStage);
        Assert.Empty(none.NextCost);

        var done = RelicProgress.For(Catalog.Weapon, "BLM", new HashSet<uint> { Weapon("physeos", "BLM") }, Info).Single();
        Assert.True(done.Done);
        Assert.Empty(RelicProgress.RemainingToEnd(Catalog.Weapon, [done]));
    }

    [Fact]
    public void The_Elemental_weapon_steps_need_Logos_actions()
    {
        var slot = RelicProgress.For(Catalog.Weapon, "WHM", new HashSet<uint> { Weapon("elemental", "WHM") }, Info).Single();
        Assert.Equal(10, slot.NextLogos);
        Assert.Equal([new RelicCost(24124, 150)], slot.NextCost);
    }

    [Fact]
    public void Armor_goes_piece_by_piece_and_chest_and_legs_cost_more_at_the_end()
    {
        // A paladin: Anemos helm (stage 4) and Elemental +1 armor of Fending (stage 6).
        var anemosHelm = Catalog.Armor[4].First;            // PLD is the first job: its head is the first item
        var elementalBody = Catalog.Armor[6].First + 1;     // Fending (PLD, WAR, DRK) is the first role: body is the second
        var slots = RelicProgress.For(Catalog.Armor, "PLD", new HashSet<uint> { anemosHelm, elementalBody }, Info);
        Assert.Equal(Pieces, slots.Select(s => s.Slot));
        var head = slots.Single(s => s.Slot == RelicSlot.Head);
        Assert.Equal(4, head.Stage);
        Assert.Equal([new RelicCost(24124, 40)], head.NextCost);
        Assert.Equal(50, head.NextLogos);
        var body = slots.Single(s => s.Slot == RelicSlot.Body);
        Assert.Equal(6, body.Stage);
        Assert.Equal([new RelicCost(24808, 35)], body.NextCost);
        // A warrior shares the role sets: the paladin's Elemental body is theirs too.
        Assert.Equal(6, RelicProgress.For(Catalog.Armor, "WAR", new HashSet<uint> { elementalBody }, Info)
            .Single(s => s.Slot == RelicSlot.Body).Stage);
    }

    [Fact]
    public void What_is_left_to_the_end_adds_up_the_stages()
    {
        var slot = RelicProgress.For(Catalog.Weapon, "SAM", new HashSet<uint> { Weapon("pyros", "SAM") }, Info).Single();
        var left = RelicProgress.RemainingToEnd(Catalog.Weapon, [slot]);
        Assert.Equal(350, left[24807]);
        Assert.Equal(5, left[24806]);
        Assert.Equal(100, left[24808]);
        Assert.False(left.ContainsKey(24124));
    }

    [Fact]
    public void Jobs_of_a_line_come_from_its_first_stage()
    {
        Assert.Equal(15, RelicProgress.Jobs(Catalog.Weapon, Info).Count);
        Assert.Equal("PLD", RelicProgress.Jobs(Catalog.Weapon, Info)[0]);
    }
}

public class StoryProgressTests
{
    private static readonly IReadOnlyList<StoryZone> Story = RelicCatalog.Embedded().Story;

    [Fact]
    public void A_zone_is_done_once_the_next_one_is_open()
    {
        var quests = new HashSet<uint> { 68614, 68478, 68148 };
        var none = new HashSet<string>();
        Assert.Equal(StoryState.Done, StoryProgress.State(Story, 0, quests, none));
        Assert.Equal(StoryState.Done, StoryProgress.State(Story, 1, quests, none));
        Assert.Equal(StoryState.InProgress, StoryProgress.State(Story, 2, quests, none));
        Assert.Equal(StoryState.Locked, StoryProgress.State(Story, 3, quests, none));
    }

    [Fact]
    public void Ticking_a_step_marks_the_ones_before_and_the_last_one_ends_the_zone()
    {
        var quests = new HashSet<uint> { 68614, 68478, 68148 };
        var ticked = new HashSet<string> { "795:40" };
        var pyros = Story[2];
        Assert.Equal([true, true, true, false, false],
            pyros.Steps.Select(s => StoryProgress.StepDone(Story, 2, s, quests, ticked)));
        ticked.Add("795:50");
        Assert.Equal(StoryState.Done, StoryProgress.State(Story, 2, quests, ticked));
        // Pagos also has a level 35 step: the keys carry the zone.
        Assert.NotEqual(StoryProgress.Key(Story[1], Story[1].Steps[^1]), StoryProgress.Key(pyros, pyros.Steps[0]));
    }
}

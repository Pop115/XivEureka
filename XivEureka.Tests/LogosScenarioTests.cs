using System.Text.Json;
using XivEureka.Logos;
using XivEureka.Zones;
using Xunit;

namespace XivEureka.Tests;

/// <summary>
/// Whole sessions at the Logos manipulator, one persona each, played on <see cref="FakeLogosGame"/>
/// and read by the plugin's own readers and <see cref="LogosTracker"/>, tick by tick.
/// </summary>
public class LogosScenarioTests
{
    private static readonly LogosCatalog Catalog = LogosCatalog.Embedded();
    private static readonly LogosAction Recovery = Catalog.Actions.Single(a => a.Number == 36); // Récup: 24028 + 24034
    private static readonly LogosAction Feint = Catalog.Actions.Single(a => a.Number == 1);     // one mneme: 24015

    /// <summary>The plugin side: a character's saved state, fed by the game twice a second.</summary>
    private sealed class Plugin(FakeLogosGame game)
    {
        private readonly LogosTracker tracker = new(Catalog);
        private DateTime now = new(2026, 10, 16, 22, 0, 0);

        public CharacterState State { get; } = new() { Name = "Test" };
        public int Saves { get; private set; }

        public void Tick(bool shardListOpen = true)
        {
            now = now.AddMilliseconds(500);
            if (tracker.Update(State, game.Snapshot(shardListOpen), now)) Saves++;
        }

        public int Owned(uint item) => State.Stock.GetValueOrDefault(item);
        public bool Learned(LogosAction action) => State.Learned.Contains(action.Action);
        public ActionState Of(LogosAction action) => Crafting.Action(action, State.Stock, State.Learned);
    }

    /// <summary>
    /// Mia, newcomer: has never opened the notebook. She has exactly one Récup worth of mnemes,
    /// places them (the action must stay "to learn now": the bug of 2026-10-16), extracts it.
    /// Récup is learned as soon as the result shows; the mnemes count until the slots empty.
    /// </summary>
    [Fact]
    public void Mia_learns_Recovery_and_the_mnemes_are_spent_only_by_the_extraction()
    {
        var game = new FakeLogosGame(Catalog, new() { [24028] = 1, [24034] = 1, [24015] = 3 });
        var plugin = new Plugin(game);

        game.OpenManipulator();
        plugin.Tick();
        Assert.True(plugin.Of(Recovery).ToLearn);

        game.Place(24028);
        plugin.Tick();
        game.Place(24034);
        plugin.Tick();
        Assert.True(plugin.Of(Recovery).ToLearn);
        Assert.Equal(1, plugin.Owned(24028));
        Assert.Equal(1, plugin.Owned(24034));

        game.Extract();
        plugin.Tick();
        Assert.True(plugin.Learned(Recovery));
        Assert.False(plugin.Of(Recovery).ToLearn);
        Assert.Equal(1, plugin.Owned(24028));

        game.DismissResult();
        plugin.Tick();
        Assert.Equal(0, plugin.Owned(24028));
        Assert.Equal(0, plugin.Owned(24034));
        Assert.Equal(3, plugin.Owned(24015));
    }

    /// <summary>
    /// Kael, veteran with a full action reserve: crafts Récup again (already learned), goes
    /// through "Remplacer". Nothing new is learned, the mnemes are spent once.
    /// </summary>
    [Fact]
    public void Kael_crafts_a_known_action_with_a_full_reserve()
    {
        var game = new FakeLogosGame(Catalog, new() { [24028] = 15, [24034] = 2 }, learned: [36]);
        var plugin = new Plugin(game);
        game.NotebookOpen = true;
        plugin.Tick();
        game.NotebookOpen = false;
        Assert.True(plugin.Learned(Recovery));

        game.OpenManipulator();
        game.PlaceRecipe(Recovery, recipe: 1);
        plugin.Tick();
        game.Extract();
        plugin.Tick();
        game.ShowReserveFull();
        plugin.Tick();
        Assert.Equal(15, plugin.Owned(24028));

        game.DismissResult();
        plugin.Tick();
        Assert.Equal(14, plugin.Owned(24028));
        Assert.Equal(1, plugin.Owned(24034));
        Assert.Equal(game.TrueLearned, plugin.State.Learned);
    }

    /// <summary>
    /// Lou, hesitant: places three mnemes, takes one back, changes her mind and closes the
    /// manipulator. At no moment does the stock move, and nothing is learned.
    /// </summary>
    [Fact]
    public void Lou_places_and_takes_back_without_extracting()
    {
        var start = new Dictionary<uint, int> { [24015] = 2, [24028] = 4, [24034] = 1 };
        var game = new FakeLogosGame(Catalog, start);
        var plugin = new Plugin(game);

        game.OpenManipulator();
        foreach (var step in new Action[]
                 {
                     () => game.Place(24015), () => game.Place(24028), () => game.Place(24034),
                     () => game.TakeBack(24028), () => game.CloseManipulator(),
                 })
        {
            step();
            plugin.Tick();
            Assert.True(LogosTracker.SameStock(start, plugin.State.Stock));
        }
        Assert.Empty(plugin.State.Learned);
    }

    /// <summary>
    /// Pop, on 2026-10-16: reloads the plugin while two mnemes already sit in the astral
    /// reactor. The plugin knows nothing yet, but must count them from its very first read.
    /// </summary>
    [Fact]
    public void Pop_reloads_the_plugin_with_mnemes_already_placed()
    {
        var game = new FakeLogosGame(Catalog, new() { [24028] = 15, [24034] = 1 });
        game.OpenManipulator();
        game.Place(24028);
        game.Place(24034);

        var plugin = new Plugin(game);
        plugin.Tick();

        Assert.Equal(15, plugin.Owned(24028));
        Assert.Equal(1, plugin.Owned(24034));
        Assert.True(plugin.Of(Recovery).ToLearn);
    }

    /// <summary>
    /// Ana, methodical: ticks an action by hand by mistake, then opens the notebook, which
    /// corrects her. She learns Feint at the manipulator; the next notebook read keeps it.
    /// </summary>
    [Fact]
    public void Ana_the_notebook_corrects_hand_ticks_and_keeps_extracted_actions()
    {
        var game = new FakeLogosGame(Catalog, new() { [24015] = 1 }, learned: [2, 3]);
        var plugin = new Plugin(game);
        plugin.State.Learned.Add(Recovery.Action);

        game.NotebookOpen = true;
        plugin.Tick();
        game.NotebookOpen = false;
        Assert.False(plugin.Learned(Recovery));
        Assert.Equal(game.TrueLearned, plugin.State.Learned);

        game.OpenManipulator();
        game.PlaceRecipe(Feint);
        game.Extract();
        plugin.Tick();
        game.DismissResult();
        game.CloseManipulator();
        plugin.Tick();
        Assert.True(plugin.Learned(Feint));

        game.NotebookOpen = true;
        plugin.Tick();
        Assert.Equal(game.TrueLearned, plugin.State.Learned);
    }

    /// <summary>Nothing open: nothing read, nothing saved, whatever was known stays.</summary>
    [Fact]
    public void Away_from_the_windows_nothing_changes()
    {
        var game = new FakeLogosGame(Catalog, new() { [24015] = 1 });
        var plugin = new Plugin(game);
        plugin.Tick();
        var saves = plugin.Saves;
        plugin.State.Stock[24015] = 7;

        var tracker = new LogosTracker(Catalog);
        Assert.False(tracker.Update(plugin.State, new LogosSnapshot(), DateTime.Now));
        Assert.Equal(7, plugin.Owned(24015));
        Assert.Equal(saves, plugin.Saves);
    }

    /// <summary>
    /// Zoé, from A to Z: arrives in Pyros with a fresh plugin, reads her notebook at Drake, opens
    /// the shard list, is told which NM to hunt, gets the shard, appraises it (first into a mneme
    /// she does not need, then into the right one), crafts the action, and the board follows each
    /// step: counts in the header, "to learn now", the action's state, the useful shards.
    /// </summary>
    [Fact]
    public void Zoe_from_arrival_to_her_new_action()
    {
        var pyros = NmCatalog.Embedded().Zone(795)!;
        uint ShardOf(uint mneme) => Catalog.Shards.Single(s => s.Mnemes.Contains(mneme)).Item;
        // An action with a two-mneme recipe a + b, b from a shard a Pyros NM drops that gives other mnemes too.
        var (target, a, b) = Catalog.Actions
            .SelectMany(x => x.Recipes.Where(r => r.Count == 2 && r[0] != r[1]).Select(r => (Action: x, A: r[0], B: r[1])))
            .First(x => pyros.Nms.Any(n => n.Shard == ShardOf(x.B))
                        && Catalog.Shards.Single(s => s.Item == ShardOf(x.B)).Mnemes.Count > 1);
        var shard = ShardOf(b);
        var other = Catalog.Shards.Single(s => s.Item == shard).Mnemes.First(m => m != b);
        // She already knows everything a, b and other can make, except the target (one-mneme
        // actions are learned first), so the target is the only one that can become "to learn".
        var mnemes = new[] { a, b, other };
        var known = Catalog.Actions
            .Where(x => x != target && x.Recipes.Any(r => r.GroupBy(m => m).All(g => mnemes.Count(m => m == g.Key) >= g.Count())))
            .Select(x => x.Number).ToList();
        var game = new FakeLogosGame(Catalog, new() { [a] = 1 }, known);
        var plugin = new Plugin(game);
        LogosBoard Board() => LogosBoard.Of(Catalog, plugin.State);
        var names = new GameNames(n => $"NM{n}", w => w, i => $"Item{i}");
        IReadOnlyList<Suggestion> WhereToGo() => Suggestions.Compute(
            pyros.Nms.Select(n => new NmStatus(n, null, null, null, null)).ToList(), DateTimeOffset.UtcNow, null,
            Board().UsefulShards, names, max: 50);

        // A. Fresh plugin, no Logos window open: nothing read, nothing suggested.
        plugin.Tick(shardListOpen: false);
        Assert.Equal(0, Board().Learned);
        Assert.Empty(Board().UsefulShards);
        Assert.DoesNotContain(WhereToGo(), s => s.Title.StartsWith("Logos"));

        // B. The notebook at Drake.
        game.NotebookOpen = true;
        plugin.Tick(shardListOpen: false);
        game.NotebookOpen = false;
        Assert.Equal(known.Count, Board().Learned);
        Assert.Empty(Board().UsefulShards);
        Assert.Equal(56, Board().Total);

        // C. The shard list: one mneme short of the target; its shard is worth hunting.
        plugin.Tick();
        Assert.Equal(0, Board().ToLearn);
        var state = Board().Actions[target.Number];
        Assert.False(state.Learned);
        Assert.Equal(1, state.Closest.Missing);
        var withoutTarget = Crafting.UsefulShards(Catalog, plugin.State.Stock, new HashSet<uint>(plugin.State.Learned) { target.Action });
        Assert.Equal(withoutTarget.GetValueOrDefault(shard) + 1, Board().UsefulShards[shard]);
        Assert.Contains(WhereToGo(), s => s.Title == $"Logos – Item{shard}");

        // D. The NM drops the shard twice; the first appraisal is no luck.
        game.Loot(shard);
        game.Loot(shard);
        game.OpenManipulator();
        game.Appraise(shard, other);
        plugin.Tick();
        Assert.False(Board().Actions[target.Number].ToLearn);
        game.Appraise(shard, b);
        plugin.Tick();
        Assert.True(Board().Actions[target.Number].ToLearn);
        Assert.Equal(1, Board().Actions[target.Number].Craftable);
        Assert.Equal(1, Board().ToLearn);

        // E. Craft: placing keeps it "to learn", the result learns it, the slots emptying spends a and b.
        game.Place(a);
        game.Place(b);
        plugin.Tick();
        Assert.Equal(1, Board().ToLearn);
        Assert.Equal(target.Number, game.Extract().Number);
        plugin.Tick();
        Assert.True(Board().Actions[target.Number].Learned);
        Assert.Equal(known.Count + 1, Board().Learned);
        Assert.Equal(0, Board().ToLearn);
        game.DismissResult();
        game.CloseManipulator();
        plugin.Tick();
        Assert.Equal(0, plugin.Owned(a));
        Assert.Equal(0, plugin.Owned(b));
        Assert.Equal(1, plugin.Owned(other));

        // F. Back at Drake: the notebook agrees.
        game.NotebookOpen = true;
        plugin.Tick();
        Assert.Equal(game.TrueLearned, plugin.State.Learned);
        Assert.Equal(known.Count + 1, Board().Learned);
    }

    // ---------- the real session of 2026-10-16 ----------

    private static (Dictionary<uint, int> List, ManipulatorView View) RealCapture(string name)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine("TestData", $"manipulator-2026-10-16-{name}.json")));
        var values = json.RootElement.GetProperty("values").EnumerateArray()
            .Select(v => v.GetString()!)
            .Select(v => v.Length > 2 && v[0] is 'u' or 'i' && v[1] == ':' ? long.Parse(v[2..]) : -1L)
            .ToList();
        var array = json.RootElement.GetProperty("NumberArray 137").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        return (ShardReader.Decode(array)!, ManipulatorReader.Decode(values)!);
    }

    [Fact]
    public void Reads_the_real_manipulator_captures()
    {
        var (_, before) = RealCapture("1-before");
        var (_, extracted) = RealCapture("2-extracted");
        var (_, spent) = RealCapture("3-spent");

        Assert.Equal(ManipulatorView.Empty.Placed, before.Placed);
        Assert.Null(before.Extracted);
        Assert.Equal([24028u, 24034u], extracted.Placed);
        Assert.Equal(36, extracted.Extracted);
        Assert.Empty(spent.Placed);
        Assert.Null(spent.Extracted); // value 27 still says 36, but the result is gone
    }

    /// <summary>Pop's Récup of 2026-10-16, replayed from the captures: learned, then spent.</summary>
    [Fact]
    public void Replays_the_real_Recovery_extraction()
    {
        var tracker = new LogosTracker(Catalog);
        var state = new CharacterState();
        var now = new DateTime(2026, 10, 16, 22, 22, 18);

        var steps = new[] { "1-before", "2-extracted", "3-spent" }.Select(RealCapture).ToList();
        var totals = new List<(int, int)>();
        foreach (var (list, view) in steps)
        {
            tracker.Update(state, new LogosSnapshot(list, view), now = now.AddSeconds(10));
            totals.Add((state.Stock.GetValueOrDefault(24028u), state.Stock.GetValueOrDefault(24034u)));
        }

        Assert.Equal([(15, 1), (15, 1), (14, 0)], totals);
        Assert.Contains(Recovery.Action, state.Learned);
    }
}

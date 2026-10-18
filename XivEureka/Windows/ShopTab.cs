using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using XivEureka.Logos;

namespace XivEureka.Windows;

/// <summary>
/// "Quoi acheter" tab: the logograms to buy at the market board to learn the actions not learned
/// yet and craft again the ones chosen (Bloodbath L…), for the least gil. Prices from Universalis
/// on the data centers of the home world's region (one checkbox each); the plan from
/// <see cref="Shopping"/>. On top the expected total, then one line per logogram (mnemes missing,
/// appraisals expected, stacks to buy for a first round, where), then the recipes the plan uses.
/// The mnemes come out at random: buy a round, appraise at the manipulator, come back, the plan
/// starts again from the new stock.
/// </summary>
public sealed class ShopTab(
    Configuration configuration,
    Action save,
    LogosCatalog catalog,
    GameTexts texts,
    ITextureProvider icons,
    Func<CharacterState?> currentCharacter,
    Func<IReadOnlyList<string>> regionDataCenters)
{
    private static readonly Vector4 Yellow = new(1f, 0.85f, 0.25f, 1f);
    private static readonly Vector4 Green = new(0.35f, 0.83f, 0.6f, 1f);
    private static readonly Vector4 Grey = new(0.6f, 0.6f, 0.65f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.45f, 0.4f, 1f);
    private static readonly TimeSpan PriceAge = TimeSpan.FromMinutes(15);

    private readonly Universalis universalis = new();
    private string? planKey;
    private volatile ShopPlan? plan;
    private Task? planning;
    private int addChoice;

    public void Draw()
    {
        var state = currentCharacter();
        if (state == null)
        {
            ImGui.TextWrapped(Loc.T("Connecte-toi à un personnage pour voir quoi acheter.", "Log in to a character to see what to buy."));
            return;
        }
        if (state.StockReadAt == null)
        {
            ImGui.TextColored(Yellow, Loc.T("Mnemes jamais lues : ouvre la fenêtre des éclats au manipulateur Logos.", "Mnemes never read: open the logogram window at the Logos manipulator."));
            return;
        }

        var dataCenters = DrawPrices();
        if (state.LearnedReadAt == null)
            ImGui.TextColored(Yellow, Loc.T("Actions apprises jamais lues : ouvre le journal chez Drake (sinon tout compte comme à apprendre).", "Learned actions never read: open the notebook at Drake (until then, everything counts as to learn)."));
        DrawGoals(state);

        if (universalis.Markets.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(Grey, universalis.Busy ? Loc.T("Lecture des prix…", "Reading the prices…")
                : Loc.T("Pas encore de prix.", "No prices yet."));
            return;
        }

        var crafts = Crafts(state);
        var current = Plan(state, crafts);
        ImGui.Spacing();
        ImGui.Separator();
        using var list = ImRaii.Child("##shop", Vector2.Zero, false);
        if (!list) return;
        if (crafts.Count == 0)
        {
            ImGui.TextColored(Grey, Loc.T("Rien à fabriquer : le tableau est complet et aucune action en plus n'est demandée.", "Nothing to craft: the board is complete and no extra action is asked for."));
            return;
        }
        if (current == null)
        {
            ImGui.TextColored(Grey, Loc.T("Calcul du plan…", "Working out the plan…"));
            return;
        }
        if (current.NothingToBuy)
        {
            ImGui.TextColored(Green, Loc.T("Rien à acheter : tes mnemes suffisent, va au mélangeur Logos.", "Nothing to buy: your mnemes are enough, go to the Logos manipulator."));
            DrawRecipes(current, state);
            return;
        }
        DrawTotal(current, dataCenters);
        DrawLines(current);
        DrawRecipes(current, state);
    }

    /// <summary>"QUOI ACHETER  [x] Chaos [x] Light  (Actualiser)  prix de 14:02".</summary>
    private IReadOnlyList<string> DrawPrices()
    {
        ImGui.TextUnformatted(Loc.T("QUOI ACHETER", "WHAT TO BUY"));
        var region = regionDataCenters();
        foreach (var dc in region)
        {
            ImGui.SameLine();
            var on = !configuration.ShopSkippedDataCenters.Contains(dc);
            if (ImGui.Checkbox(dc + "##dc", ref on))
            {
                if (on) configuration.ShopSkippedDataCenters.Remove(dc);
                else configuration.ShopSkippedDataCenters.Add(dc);
                save();
            }
        }
        var chosen = region.Where(dc => !configuration.ShopSkippedDataCenters.Contains(dc)).ToList();
        var items = catalog.Shards.Select(s => s.Item).ToList();
        var stale = universalis.ReadAt == null || DateTime.Now - universalis.ReadAt > PriceAge
            || !universalis.ReadFor.SequenceEqual(chosen);
        if (stale && universalis.Error == null) universalis.Refresh(items, chosen);

        ImGui.SameLine();
        using (ImRaii.Disabled(universalis.Busy || chosen.Count == 0))
            if (ImGui.SmallButton(Loc.T("Actualiser", "Refresh") + "##prices")) universalis.Refresh(items, chosen);
        ImGui.SameLine();
        if (universalis.Busy)
            ImGui.TextColored(Grey, Loc.T("lecture…", "reading…"));
        else if (universalis.ReadAt is { } at)
        {
            ImGui.TextColored(Grey, Loc.T($"prix Universalis de {at:HH:mm}", $"Universalis prices as of {at:HH:mm}"));
            if (ImGui.IsItemHovered() && universalis.OldestUpload is { } upload)
                ImGui.SetTooltip(Loc.T($"Relevés par les joueurs ; le plus ancien date du {upload:dd/MM HH:mm}. Les prix sont hors taxe (5 % de plus en général).",
                    $"Uploaded by players; the oldest is from {upload:MM/dd HH:mm}. Prices before tax (usually 5% more)."));
        }
        if (universalis.Error is { } error)
            ImGui.TextColored(Red, Loc.T($"Universalis ne répond pas ({error}) : réessaie avec « Actualiser ».", $"Universalis did not answer ({error}): try again with \"Refresh\"."));
        if (chosen.Count == 0)
            ImGui.TextColored(Yellow, Loc.T("Coche au moins un centre de données.", "Tick at least one data center."));
        return chosen;
    }

    /// <summary>"[x] Compléter le tableau", then the extra crafts with their − / + and "Ajouter".</summary>
    private void DrawGoals(CharacterState state)
    {
        var notLearned = catalog.Actions.Count(a => !state.Learned.Contains(a.Action));
        var complete = configuration.ShopCompleteBoard;
        if (ImGui.Checkbox(Loc.T($"Compléter le tableau ({notLearned} action(s) pas apprise(s))", $"Complete the board ({notLearned} action(s) not learned)") + "##complete", ref complete))
        {
            configuration.ShopCompleteBoard = complete;
            save();
        }

        ImGui.TextUnformatted(Loc.T("Fabriquer en plus :", "Also craft:"));
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Loc.T("Les actions que tu veux fabriquer encore (une fabrication = ses utilisations), apprises ou pas.",
                "Actions you want to craft again (one craft = its uses), learned or not."));
        foreach (var action in catalog.Actions.Where(a => state.ExtraCrafts.GetValueOrDefault(a.Action) > 0).ToList())
        {
            var count = state.ExtraCrafts[action.Action];
            ImGui.SameLine();
            var text = texts.Action(action.Action);
            Icon(text.Icon, new Vector2(ImGui.GetFrameHeight()));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(Tip(action));
            ImGui.SameLine(0, 3);
            ImGui.TextUnformatted($"{text.ShortName} ×{count}");
            ImGui.SameLine(0, 3);
            if (ImGui.SmallButton($"-##less{action.Action}")) SetExtra(state, action.Action, count - 1);
            ImGui.SameLine(0, 2);
            if (ImGui.SmallButton($"+##more{action.Action}")) SetExtra(state, action.Action, count + 1);
        }

        ImGui.SameLine();
        var choices = catalog.Actions.OrderBy(a => texts.Action(a.Action).ShortName, StringComparer.CurrentCulture).ToList();
        addChoice = Math.Clamp(addChoice, 0, choices.Count - 1);
        ImGui.SetNextItemWidth(200);
        using (var combo = ImRaii.Combo("##add", texts.Action(choices[addChoice].Action).ShortName))
        {
            if (combo)
                for (var i = 0; i < choices.Count; i++)
                    if (ImGui.Selectable(texts.Action(choices[i].Action).ShortName + $"##pick{i}", i == addChoice))
                        addChoice = i;
        }
        ImGui.SameLine();
        if (ImGui.SmallButton(Loc.T("Ajouter", "Add") + "##addextra"))
        {
            var action = choices[addChoice].Action;
            SetExtra(state, action, state.ExtraCrafts.GetValueOrDefault(action) + 1);
        }
    }

    private void SetExtra(CharacterState state, uint action, int count)
    {
        if (count <= 0) state.ExtraCrafts.Remove(action);
        else state.ExtraCrafts[action] = Math.Min(count, 99);
        save();
    }

    /// <summary>Action → crafts: once for each action not learned (if asked), plus the extras.</summary>
    private Dictionary<uint, int> Crafts(CharacterState state)
    {
        var crafts = new Dictionary<uint, int>();
        foreach (var action in catalog.Actions)
        {
            var count = state.ExtraCrafts.GetValueOrDefault(action.Action);
            if (configuration.ShopCompleteBoard && !state.Learned.Contains(action.Action)) count++;
            if (count > 0) crafts[action.Action] = count;
        }
        return crafts;
    }

    /// <summary>
    /// The plan, computed again in the background only when the stock, the goals or the prices
    /// change (a few hundred milliseconds); null until the first one is ready.
    /// </summary>
    private ShopPlan? Plan(CharacterState state, Dictionary<uint, int> crafts)
    {
        var key = string.Join(',', state.Stock.OrderBy(e => e.Key).Select(e => $"{e.Key}:{e.Value}")) + "|"
            + string.Join(',', state.Learned.Order()) + "|"
            + string.Join(',', crafts.OrderBy(e => e.Key).Select(e => $"{e.Key}:{e.Value}")) + "|"
            + universalis.ReadAt?.Ticks.ToString(CultureInfo.InvariantCulture);
        if (key != planKey && planning is not { IsCompleted: false })
        {
            planKey = key;
            var stock = new Dictionary<uint, int>(state.Stock);
            var learned = new HashSet<uint>(state.Learned);
            var markets = universalis.Markets;
            planning = Task.Run(() => plan = Shopping.Plan(catalog, stock, crafts, learned, markets));
        }
        return plan;
    }

    private void DrawTotal(ShopPlan current, IReadOnlyList<string> dataCenters)
    {
        var appraisals = current.Lines.Sum(l => l.Expected);
        ImGui.TextColored(Yellow, Loc.T($"≈ {Gil(current.Cost)} gils en moyenne, pour ≈ {appraisals:0} éclats",
            $"≈ {Gil(current.Cost)} gil on average, for ≈ {appraisals:0} logograms"));
        ImGui.TextColored(Grey, Loc.T(
            "Chaque éclat donne une de ses mnemes au hasard : achète le 1er tour, fais-les identifier au manipulateur, reviens ici. Le plan repart de tes nouvelles mnemes, sans rien acheter de trop.",
            "Each logogram gives one of its mnemes at random: buy the first round, appraise them at the manipulator, come back here. The plan starts again from your new mnemes, buying nothing too many."));
        if (current.Lines.Any(l => l.NotEnoughForSale))
            ImGui.TextColored(Red, Loc.T($"Pas assez en vente pour certains éclats sur {string.Join(" + ", dataCenters)} : il faudra en farmer ou attendre.",
                $"Not enough for sale for some logograms on {string.Join(" + ", dataCenters)}: farm them or wait."));
        ImGui.Spacing();
    }

    private void DrawLines(ShopPlan current)
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp;
        using var table = ImRaii.Table("##lines", 4, flags);
        if (!table) return;
        ImGui.TableSetupColumn(Loc.T("Éclat", "Logogram"), ImGuiTableColumnFlags.WidthStretch, 1.1f);
        ImGui.TableSetupColumn(Loc.T("Mnemes qui manquent", "Missing mnemes"), ImGuiTableColumnFlags.WidthStretch, 1.4f);
        ImGui.TableSetupColumn(Loc.T("Combien", "How many"), ImGuiTableColumnFlags.WidthStretch, 0.9f);
        ImGui.TableSetupColumn(Loc.T("1er tour : où acheter", "First round: where to buy"), ImGuiTableColumnFlags.WidthStretch, 1.6f);
        ImGui.TableHeadersRow();
        foreach (var line in current.Lines)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var shard = texts.Item(line.Shard);
            Icon(shard.Icon, new Vector2(24));
            ImGui.SameLine();
            ImGui.TextUnformatted(GameTexts.WithoutUnidentified(shard.Name));

            ImGui.TableNextColumn();
            foreach (var (mneme, count) in line.Missing)
            {
                var text = texts.Item(mneme);
                Icon(text.Icon, new Vector2(20));
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(text.Name);
                ImGui.SameLine(0, 3);
                ImGui.TextUnformatted($"{text.ShortName} ×{count}");
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(Loc.T($"≈ {line.Expected:0.#} (au moins {line.AtLeast})", $"≈ {line.Expected:0.#} (at least {line.AtLeast})"));
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(Loc.T($"Éclats à identifier en moyenne pour avoir toutes ces mnemes ({Pool(line.Shard)} possibles, au hasard).",
                    $"Logograms to appraise on average to get all these mnemes ({Pool(line.Shard)} possible, at random)."));
            ImGui.TextColored(line.NotEnoughForSale ? Red : Grey, $"≈ {Gil(line.Cost)} {Loc.T("gils", "gil")}");

            ImGui.TableNextColumn();
            DrawStacks(line);
        }
    }

    /// <summary>
    /// The stacks to buy for the first round: as many as the mnemes missing (none wasted at best),
    /// stacks bought whole, cheapest total.
    /// </summary>
    private void DrawStacks(ShopLine line)
    {
        if (!universalis.Markets.TryGetValue(line.Shard, out var market) || market.Listings.Count == 0)
        {
            ImGui.TextColored(Red, Loc.T("rien en vente", "none for sale"));
            return;
        }
        var stacks = Shopping.Stacks(market, line.AtLeast);
        var total = stacks.Sum(s => (long)s.Quantity * s.PricePerUnit);
        foreach (var group in stacks.GroupBy(s => (s.World, s.DataCenter)))
        {
            var units = group.Sum(s => s.Quantity);
            var prices = string.Join(", ", group.Select(s => $"{s.Quantity}×{Gil(s.PricePerUnit)}"));
            ImGui.TextUnformatted($"{group.Key.World} ({group.Key.DataCenter}) : {prices}");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(Loc.T($"{units} éclat(s) sur {group.Key.World}", $"{units} logogram(s) on {group.Key.World}"));
        }
        ImGui.TextColored(Grey, Loc.T($"{stacks.Sum(s => s.Quantity)} pour {Gil(total)} gils", $"{stacks.Sum(s => s.Quantity)} for {Gil(total)} gil"));
    }

    /// <summary>The recipes the plan uses, action by action: what to place at the manipulator.</summary>
    private void DrawRecipes(ShopPlan current, CharacterState state)
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted(Loc.T("Recettes choisies", "Chosen recipes"));
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Loc.T("Pour chaque fabrication, la recette qui coûte le moins à compléter avec tes mnemes.",
                "For each craft, the recipe cheapest to complete with your mnemes."));
        foreach (var group in current.Crafts.GroupBy(c => (c.Action.Number, Recipe: string.Join(',', c.Recipe))))
        {
            var craft = group.First();
            var text = texts.Action(craft.Action.Action);
            Icon(text.Icon, new Vector2(24));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(Tip(craft.Action));
            ImGui.SameLine();
            var learns = group.Any(c => c.Learn);
            var count = group.Count();
            ImGui.TextColored(learns ? Green : Yellow, count > 1 ? $"{text.ShortName} ×{count}" : text.ShortName);
            ImGui.SameLine();
            ImGui.TextColored(Grey, learns ? Loc.T("(à apprendre)", "(to learn)") : Loc.T("(en plus)", "(extra)"));
            foreach (var (mneme, need) in craft.Recipe.GroupBy(m => m).Select(g => (g.Key, g.Count())))
            {
                ImGui.SameLine();
                var mnemeText = texts.Item(mneme);
                Icon(mnemeText.Icon, new Vector2(20));
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(mnemeText.Name);
                ImGui.SameLine(0, 2);
                var owned = state.Stock.GetValueOrDefault(mneme);
                ImGui.TextColored(owned >= need * count ? Green : Grey, need > 1 ? $"{mnemeText.ShortName} ×{need}" : mnemeText.ShortName);
            }
        }
    }

    private string Tip(LogosAction action)
    {
        var text = texts.Action(action.Action);
        var uses = action.Uses is { } n ? Loc.T($"{n} utilisation(s) par fabrication", $"{n} use(s) per craft") : Loc.T("sans limite d'utilisations", "unlimited uses");
        return $"{text.Name}\n{uses}\n\n{text.Description}";
    }

    private int Pool(uint shard) => catalog.Shards.First(s => s.Item == shard).Mnemes.Count;

    private static string Gil(double amount) =>
        amount >= Shopping.Unavailable ? "?" : Math.Round(amount).ToString("N0", CultureInfo.CurrentCulture);

    private void Icon(uint icon, Vector2 size)
    {
        if (icon != 0 && icons.TryGetFromGameIcon(new GameIconLookup(icon), out var shared) && shared.TryGetWrap(out var image, out _))
            ImGui.Image(image.Handle, size);
        else
            ImGui.Dummy(size);
    }
}

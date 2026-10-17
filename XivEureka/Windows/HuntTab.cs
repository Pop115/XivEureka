using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using XivEureka.Logos;
using XivEureka.Zones;

namespace XivEureka.Windows;

/// <summary>
/// "Quoi taper" tab: the chain from the Logos actions not learned yet to what to hit. On top, the
/// actions craftable right now (go to the manipulator), then "Le plus rapide": for each useful
/// shard, its fastest source now (sprites under their weather first). Then one block per useful
/// shard, most useful first: the actions it moves forward, all its sources fastest first (sprites,
/// NMs with their live state, bunny chests, lockboxes, random mobs) and the mnemes it can turn into,
/// with what each one is missing for. Computed by <see cref="Hunt"/> and <see cref="HuntOptions"/>.
/// User-facing texts are French for now.
/// </summary>
public sealed class HuntTab(
    LogosCatalog catalog,
    NmCatalog nmCatalog,
    FarmCatalog farms,
    GameTexts texts,
    ITextureProvider icons,
    Func<CharacterState?> currentCharacter,
    Func<HuntContext> contextNow,
    Func<string, string> weatherName,
    Action<EurekaNm> showNm,
    Action<string, IReadOnlyList<Vector2>> showFarm,
    Action<uint, IReadOnlyList<Vector2>> setFlag)
{
    /// <summary>Sources shown per shard before "N autre(s)".</summary>
    private const int ShownSources = 4;

    private static readonly Vector4 Yellow = new(1f, 0.85f, 0.25f, 1f);
    private static readonly Vector4 Green = new(0.35f, 0.83f, 0.6f, 1f);
    private static readonly Vector4 Amber = new(0.95f, 0.65f, 0.2f, 1f);
    private static readonly Vector4 Grey = new(0.6f, 0.6f, 0.65f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.45f, 0.4f, 1f);

    public void Draw()
    {
        var state = currentCharacter();
        if (state == null)
        {
            ImGui.TextWrapped(Loc.T("Connecte-toi à un personnage pour voir quoi taper.", "Log in to a character to see what to hit."));
            return;
        }

        ImGui.TextUnformatted(Loc.T("QUOI TAPER", "WHAT TO HIT"));
        if (state.StockReadAt == null)
        {
            ImGui.TextColored(Yellow, Loc.T("Mnemes jamais lues : ouvre la fenêtre des éclats au manipulateur Logos.", "Mnemes never read: open the logogram window at the Logos manipulator."));
            return;
        }
        if (state.LearnedReadAt == null)
            ImGui.TextColored(Yellow, Loc.T("Actions apprises jamais lues : ouvre le journal chez Drake (sinon tout compte comme à apprendre).", "Learned actions never read: open the notebook at Drake (until then, everything counts as to learn)."));

        var plan = Hunt.Compute(catalog, nmCatalog.Zones, state.Stock, state.Learned);
        var context = contextNow();
        ImGui.SameLine();
        ImGui.TextColored(Grey, Loc.T($"{plan.NotLearned} action(s) pas encore apprise(s) · mnemes du {state.StockReadAt:dd/MM HH:mm}", $"{plan.NotLearned} action(s) not learned yet · mnemes as of {state.StockReadAt:MM/dd HH:mm}"));

        if (plan.CraftableNow.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(Green, Loc.T($"Fabricables maintenant ({plan.CraftableNow.Count}) : va au mélangeur Logos", $"Craftable now ({plan.CraftableNow.Count}): go to the Logos manipulator"));
            ActionIcons(plan.CraftableNow);
        }
        if (plan.Shards.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(Grey, plan.NotLearned == 0 ? Loc.T("Toutes les actions Logos sont apprises.", "All Logos actions are learned.") : Loc.T("Plus rien à chasser : tout le reste est fabricable.", "Nothing left to hunt: all the rest is craftable."));
            return;
        }

        ImGui.Spacing();
        ImGui.Separator();
        using var list = ImRaii.Child("##hunt", Vector2.Zero, false);
        if (!list) return;
        DrawFastest(plan, context);
        foreach (var shard in plan.Shards) DrawShard(shard, context);
    }

    /// <summary>One line per useful shard, by its fastest source: what to do first.</summary>
    private void DrawFastest(HuntPlan plan, HuntContext context)
    {
        var zone = EurekaWeather.Zone(context.Territory)?.Name ?? "?";
        ImGui.TextColored(Yellow, Loc.T("LE PLUS RAPIDE", "FASTEST"));
        ImGui.SameLine();
        ImGui.TextColored(Grey, context.Level is { } level ? Loc.T($"à {zone}, niveau {level}", $"in {zone}, level {level}") : Loc.T($"à {zone} (niveau inconnu)", $"in {zone} (level unknown)"));
        var fastest = HuntOptions.Fastest(plan, farms, context);
        if (fastest.Count == 0)
        {
            ImGui.TextColored(Grey, Loc.T("Aucune source connue dans cette zone pour les éclats qui te manquent.", "No known source in this zone for the logograms you are missing."));
        }
        using (var table = ImRaii.Table("##fastest", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
        {
            if (table)
            {
                ImGui.TableSetupColumn(Loc.T("éclat", "logogram"));
                ImGui.TableSetupColumn("source", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn(Loc.T("quand", "when"));
                ImGui.TableSetupColumn(Loc.T("combien", "how long"));
                ImGui.TableSetupColumn(Loc.T("boutons", "buttons"));
                foreach (var (option, i) in fastest.Select((o, i) => (o, i)))
                {
                    using var id = ImRaii.PushId(i);
                    var shard = plan.Shards.First(s => s.Shard == option.Shard);
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    var text = texts.Item(option.Shard);
                    Icon(text.Icon, new Vector2(20));
                    ImGui.SameLine();
                    ImGui.TextUnformatted(GameTexts.WithoutUnidentified(text.Name));
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip(Loc.T($"Sert à {shard.Actions.Count} action(s) · ≈ {shard.UsefulChance:P0} de chances d'un mneme utile", $"Used by {shard.Actions.Count} action(s) · ≈ {shard.UsefulChance:P0} chance of a useful mneme"));
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(Label(option));
                    if (option.Notes.Count > 0 && ImGui.IsItemHovered()) ImGui.SetTooltip(string.Join("\n", option.Notes));
                    ImGui.TableNextColumn();
                    When(option, context);
                    ImGui.TableNextColumn();
                    Effort(option, context.XpWeight);
                    ImGui.TableNextColumn();
                    MapButton(option);
                }
            }
        }
        ImGui.TextColored(Grey, Loc.T("Temps estimé jusqu'au premier mneme utile : attente (météo, jour/nuit, respawn, FATE) + essais × temps par essai.", "Estimated time to the first useful mneme: wait (weather, day/night, respawn, FATE) + tries × time per try."));
        if (context.XpWeight > 0)
            ImGui.TextColored(Grey, Loc.T($"Classé aussi par l'XP (niveau max de la zone : {HuntXp.MaxLevel(context.Territory)}) : une source qui fait monter de niveau compte pour moins de temps.",
                $"Also ranked by experience (zone max level: {HuntXp.MaxLevel(context.Territory)}): a source that levels you up counts for less time."));
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Loc.T("Chance par essai = chance que l'éclat tombe (tier du coffre, adaptation…) × chance qu'un des éclats donne un mneme utile.\n" +
                "Mesuré pour les coffres lapin (relevé communautaire de plus de 13 000 coffres par zone) et les mnemes (700 000 analyses).\n" +
                "Estimé ailleurs (élémentaires, adaptations, coffres de mutation) : des ordres de grandeur pour classer.",
                "Chance per try = chance the logogram drops (coffer tier, adaptation…) × chance one of them gives a useful mneme.\n" +
                "Measured for bunny coffers (community record of over 13,000 coffers per zone) and mnemes (700,000 appraisals).\n" +
                "Estimated elsewhere (elementals, adaptations, mutation lockboxes): orders of magnitude, to rank."));
        ImGui.Spacing();
        ImGui.Separator();
    }

    private void DrawShard(HuntShard shard, HuntContext context)
    {
        using var id = ImRaii.PushId((int)shard.Shard);
        var text = texts.Item(shard.Shard);
        Icon(text.Icon, new Vector2(32));
        ImGui.SameLine();
        ImGui.BeginGroup();
        ImGui.TextColored(Yellow, GameTexts.WithoutUnidentified(text.Name));
        ImGui.SameLine();
        var useful = shard.Mnemes.Count(m => m.NeededBy.Count > 0);
        ImGui.TextColored(Grey, Loc.T($"sert à {shard.Actions.Count} action(s) · à l'analyse, 1 mneme parmi {shard.Mnemes.Count} : {useful} utile(s), {shard.UsefulChance:P0} de chances", $"used by {shard.Actions.Count} action(s) · appraisal gives 1 mneme of {shard.Mnemes.Count}: {useful} useful, {shard.UsefulChance:P0} chance"));
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Loc.T("Les mnemes sortent à chances égales (relevé communautaire de 700 000 analyses).\n" +
                $"Il faut en moyenne {1 / shard.UsefulChance:0.#} éclat(s) pour un mneme utile : les temps estimés en tiennent compte.",
                "Mnemes come out evenly (community record of 700,000 appraisals).\n" +
                $"On average {1 / shard.UsefulChance:0.#} logogram(s) for a useful mneme: the estimated times include it."));
        ActionIcons(shard.Actions);
        ImGui.EndGroup();

        ImGui.Indent(40);
        var options = HuntOptions.For(shard, farms, context);
        if (options.Count == 0)
            ImGui.TextColored(Grey, Loc.T("Aucune source connue (zone pas encore dans les données du plugin, ou pas à ton niveau).", "No known source (zone not in the plugin data yet, or not at your level)."));
        for (var i = 0; i < Math.Min(ShownSources, options.Count); i++) DrawOption(options[i], i, context);
        if (options.Count > ShownSources)
        {
            using var more = ImRaii.TreeNode(Loc.T($"{options.Count - ShownSources} autre(s) source(s)", $"{options.Count - ShownSources} more source(s)") + "##more");
            if (more)
                for (var i = ShownSources; i < options.Count; i++) DrawOption(options[i], i, context);
        }

        foreach (var mneme in shard.Mnemes)
        {
            var m = texts.Item(mneme.Mneme);
            Icon(m.Icon, new Vector2(20));
            ImGui.SameLine();
            var line = $"{m.ShortName} ({mneme.Owned.ToString(CultureInfo.InvariantCulture)})";
            if (mneme.NeededBy.Count == 0)
            {
                ImGui.TextColored(Grey, Loc.T($"{line} : pas utile pour l'instant", $"{line}: not useful for now"));
                continue;
            }
            ImGui.TextUnformatted(line);
            ImGui.SameLine();
            ImGui.TextColored(Grey, Loc.T("manque pour : ", "missing for: ") + string.Join(", ",
                mneme.NeededBy.Select(n => n.Missing > 1 ? $"{texts.Action(n.Action.Action).ShortName} (×{n.Missing})" : texts.Action(n.Action.Action).ShortName)));
        }
        ImGui.Unindent(40);
        ImGui.Spacing();
        ImGui.Separator();
    }

    private void DrawOption(HuntOption option, int index, HuntContext context)
    {
        using var id = ImRaii.PushId(index);
        ImGui.TextUnformatted(Label(option));
        ImGui.SameLine();
        When(option, context);
        ImGui.SameLine();
        Effort(option, context.XpWeight);
        if (option.Notes.Count > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Grey, "· " + string.Join(" · ", option.Notes));
        }
        ImGui.SameLine();
        MapButton(option);
    }

    /// <summary>
    /// "≈ 35 min · ~7 kills (14 %)", "≈ 1 h 40 · ~4 FATE lapin (28 %)": total estimated time, then
    /// tries and chance per try; the count dropped at once in the tooltip.
    /// </summary>
    private static void Effort(HuntOption option, double xpWeight)
    {
        if (option.Eta is not { } eta) return;
        var what = option.Farm?.Kind switch
        {
            FarmKind.Bunny => Loc.T("FATE lapin", "bunny FATEs"),
            null => "NM",
            FarmKind.Chain => Loc.T("chaînes", "chains"),
            _ => "kills",
        };
        var tries = option.Tries < 1.5 ? Loc.T("1 essai", "1 try") : $"~{option.Tries:0} {what}";
        ImGui.TextColored(Grey, $"≈ {Formatting.Duration(eta)} · {tries} ({option.TryChance:P0})");
        var hovered = ImGui.IsItemHovered();
        ImGui.SameLine();
        var (difficulty, color) = option.Difficulty switch
        {
            Difficulty.Easy => (Loc.T("facile", "easy"), Green),
            Difficulty.Medium => (Loc.T("moyen", "medium"), Amber),
            _ => (Loc.T("dur", "hard"), Red),
        };
        ImGui.TextColored(color, difficulty);
        hovered |= ImGui.IsItemHovered();
        if (xpWeight > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(option.XpShare >= 0.75 ? Green : option.XpShare >= 0.3 ? Amber : Grey,
                option.XpShare >= 0.75 ? "XP ++" : option.XpShare >= 0.3 ? "XP +" : "XP –");
            hovered |= ImGui.IsItemHovered();
        }
        if (!hovered) return;
        var count = option.Drop?.Count is { } c ? Loc.T($"{c} éclat(s) à la fois", $"{c} logogram(s) at once") : Loc.T("1 éclat à la fois", "1 logogram at once");
        var lines = new List<string>
        {
            Loc.T($"{count} · {option.TryChance:P0} de chances par essai d'avoir un mneme utile", $"{count} · {option.TryChance:P0} chance per try of a useful mneme"),
            Loc.T($"= {option.Tries:0.#} essai(s) en moyenne", $"= {option.Tries:0.#} tries on average"),
        };
        if (option.Wait > TimeSpan.Zero) lines.Add(Loc.T($"attente {Formatting.Duration(option.Wait)} (météo, jour/nuit, respawn)", $"wait {Formatting.Duration(option.Wait)} (weather, day/night, respawn)"));
        lines.Add(Loc.T($"1er essai {Formatting.Duration(option.FirstTry)}", $"1st try {Formatting.Duration(option.FirstTry)}"));
        if (option.Tries > 1) lines.Add(Loc.T($"+ {option.Tries - 1:0.#} × {Formatting.Duration(option.TryTime)} pour les suivants", $"+ {option.Tries - 1:0.#} × {Formatting.Duration(option.TryTime)} for the next ones"));
        lines.Add(Loc.T($"≈ {Formatting.Duration(eta)} en tout", $"≈ {Formatting.Duration(eta)} in all"));
        lines.Add(Loc.T($"difficulté : {difficulty} (écart de niveau, mob qui mute ou s'adapte, NM)",
            $"difficulty: {difficulty} (level gap, mob that mutates or adapts, NM)"));
        if (xpWeight > 0 && option.Counted is { } counted)
        {
            lines.Add(Loc.T($"XP : {option.XpShare:P0} du rythme d'un farm de mobs à +3 niveaux",
                $"XP: {option.XpShare:P0} of the rhythm of grinding mobs 3 levels above"));
            lines.Add(Loc.T($"compté dans le classement : ≈ {Formatting.Duration(counted)} (l'XP compte pour {xpWeight:P0})",
                $"counted in the ranking: ≈ {Formatting.Duration(counted)} (experience weighs {xpWeight:P0})"));
        }
        ImGui.SetTooltip(string.Join("\n", lines));
    }

    /// <summary>"Sprite d'orage Nv 41", "FATE lapin « … »", "Coffre de mutation : …", "NM (Nv 45, Pyros)".</summary>
    private string Label(HuntOption option)
    {
        if (option.Nm is { } nm)
        {
            var zone = EurekaWeather.Zone(nm.Territory)?.Name ?? $"zone {nm.Territory}";
            return $"NM {texts.Npc(nm.Nm.Npc)} ({Loc.T("Nv", "lv")} {nm.Nm.Level}, {zone})";
        }
        var farm = option.Farm!;
        return farm.Kind switch
        {
            FarmKind.Bunny => Loc.T($"FATE lapin « {texts.Fate(farm.Fate)} »", $"Bunny FATE \"{texts.Fate(farm.Fate)}\""),
            FarmKind.Chain => Loc.T($"Chaîne de 30 (mobs Nv {farm.Level}+)", $"Chain of 30 (lv {farm.Level}+ mobs)"),
            FarmKind.Lockbox => Loc.T($"Coffre de mutation : {texts.Npc(farm.Npc)} Nv {farm.Level}", $"Mutation lockbox: {texts.Npc(farm.Npc)} lv {farm.Level}")
                + (farm.Mutated ? Loc.T(" muté", " mutated") : farm.Adapted ? Loc.T(" adapté", " adapted") : ""),
            _ => $"{texts.Npc(farm.Npc)} {Loc.T("Nv", "lv")} {farm.Level}" + (farm.Adapted ? Loc.T(" adapté", " adapted") : ""),
        };
    }

    /// <summary>When the source is there: green now, amber to come (with the weather), grey unknown.</summary>
    private void When(HuntOption option, HuntContext context)
    {
        var now = context.Now;
        if (option.Farm is { Kind: FarmKind.Bunny } bunny && option.Eta != null)
        {
            if (context.BunnyFates.Contains(bunny.Fate)) ImGui.TextColored(Green, Loc.T("FATE en cours", "FATE running"));
            else ImGui.TextColored(Amber, Loc.T("attendre la FATE", "wait for the FATE"));
            return;
        }
        if (option.Status is { } status)
        {
            ImGui.TextColored(status.IsDoable(now) ? Green : Amber, status.Describe(now, weatherName));
            return;
        }
        if (option.Eta == null)
        {
            ImGui.TextColored(Grey, option.Nm != null ? Loc.T("état inconnu (autre zone)", "state unknown (other zone)") : Loc.T("autre zone", "other zone"));
            return;
        }
        var weather = option.Farm is { Weathers.Count: > 0 } f
            ? string.Join(Loc.T(" ou ", " or "), f.Weathers.Select(weatherName))
            : option.Farm?.Time switch { "night" => Loc.T("Nuit", "Night"), "day" => Loc.T("Jour", "Day"), _ => null };
        if (option.Window is { } w && !w.IsOpen(now))
            ImGui.TextColored(Amber, Loc.T($"{weather} dans", $"{weather} in") + $" {Formatting.Duration(w.Start - now)}");
        else if (option.Window is { } open)
            ImGui.TextColored(Green, Loc.T($"{weather} en cours (encore {Formatting.Duration(open.End - now)})", $"{weather} now ({Formatting.Duration(open.End - now)} left)"));
        else
            ImGui.TextColored(Green, Loc.T("maintenant", "now"));
    }

    /// <summary>"Carte": shows it on the plugin map; "Drapeau": game flag on the nearest place.</summary>
    private void MapButton(HuntOption option)
    {
        if (option.Places.Count == 0) return;
        if (ImGui.SmallButton(Loc.T("Carte", "Map") + "##map"))
        {
            if (option.Nm is { } nm) showNm(nm.Nm);
            else showFarm(Label(option), option.Places);
        }
        ImGui.SameLine();
        if (ImGui.SmallButton(Loc.T("Drapeau", "Flag") + "##flag")) setFlag(option.Territory, option.Places);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Loc.T("Pose le drapeau de la carte du jeu (sur l'endroit le plus proche de toi)", "Sets the game map flag (on the place nearest to you)"));
    }

    private void ActionIcons(IReadOnlyList<LogosAction> actions)
    {
        var first = true;
        foreach (var action in actions)
        {
            if (!first) ImGui.SameLine(0, 3);
            first = false;
            var text = texts.Action(action.Action);
            Icon(text.Icon, new Vector2(24));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(text.Name);
        }
    }

    private void Icon(uint icon, Vector2 size)
    {
        if (icon != 0 && icons.TryGetFromGameIcon(new GameIconLookup(icon), out var shared) && shared.TryGetWrap(out var image, out _))
            ImGui.Image(image.Handle, size);
        else
            ImGui.Dummy(size);
    }
}

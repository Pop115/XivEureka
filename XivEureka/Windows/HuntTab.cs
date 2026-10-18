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
/// Three modes on top: Logos (that ranking alone), Mix (the experience counted too, three presets),
/// XP (what levels you up fastest in the zone, <see cref="XpOptions"/>), only below the zone's max level.
/// </summary>
public sealed class HuntTab(
    Configuration configuration,
    Action save,
    Func<IReadOnlySet<uint>> playerStatuses,
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

        var context = contextNow();
        var maxLevel = context.Level is { } lv && lv >= HuntXp.MaxLevel(context.Territory);
        var mode = DrawMode(context, maxLevel);
        if (mode == HuntMode.Xp)
        {
            DrawXp(state, context);
            return;
        }
        if (state.StockReadAt == null)
        {
            ImGui.TextColored(Yellow, Loc.T("Mnemes jamais lues : ouvre la fenêtre des éclats au manipulateur Logos.", "Mnemes never read: open the logogram window at the Logos manipulator."));
            return;
        }
        if (state.LearnedReadAt == null)
            ImGui.TextColored(Yellow, Loc.T("Actions apprises jamais lues : ouvre le journal chez Drake (sinon tout compte comme à apprendre).", "Learned actions never read: open the notebook at Drake (until then, everything counts as to learn)."));

        var plan = Hunt.Compute(catalog, nmCatalog.Zones, state.Stock, state.Learned);
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
        // Only the shards that can be had in this zone: the others wait for their own zone.
        var here = plan.Shards.Select(s => (Shard: s, Options: HuntOptions.For(s, farms, context))).Where(x => x.Options.Count > 0).ToList();
        foreach (var (shard, options) in here) DrawShard(shard, options, context);
        if (here.Count < plan.Shards.Count)
            ImGui.TextColored(Grey, Loc.T($"{plan.Shards.Count - here.Count} autre(s) éclat(s) utile(s) sans source dans cette zone (ou pas à ton niveau).",
                $"{plan.Shards.Count - here.Count} other useful logogram(s) with no source in this zone (or not at your level)."));
    }

    private static readonly (int Percent, string Fr, string En)[] Presets =
    [
        (25, "surtout Logos", "mostly Logos"),
        (50, "équilibré", "balanced"),
        (75, "surtout XP", "mostly XP"),
    ];

    /// <summary>
    /// "QUOI TAPER  (o) Logos ( ) Mix ( ) XP", and the Mix presets. At the zone's max level the
    /// experience counts for nothing: XP is greyed, and a saved XP mode shows the Logos ranking.
    /// </summary>
    private HuntMode DrawMode(HuntContext context, bool maxLevel)
    {
        ImGui.TextUnformatted(Loc.T("QUOI TAPER", "WHAT TO HIT"));
        foreach (var (mode, label, tip) in new[]
        {
            (HuntMode.Logos, "Logos", Loc.T("Le plus rapide vers les actions Logos qui te manquent.", "Fastest to the Logos actions you are missing.")),
            (HuntMode.Mix, "Mix", Loc.T("Les actions Logos, en comptant aussi l'XP : une source qui fait monter de niveau compte pour moins de temps.",
                "The Logos actions, experience counted too: a source that levels you up counts for less time.")),
            (HuntMode.Xp, "XP", Loc.T("Ce qui fait monter de niveau le plus vite dans la zone.", "What levels you up fastest in the zone.")),
        })
        {
            ImGui.SameLine();
            using (ImRaii.Disabled(mode != HuntMode.Logos && maxLevel))
            {
                if (ImGui.RadioButton(label + "##mode", configuration.HuntMode == mode))
                {
                    configuration.HuntMode = mode;
                    save();
                }
            }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(maxLevel && mode != HuntMode.Logos
                    ? Loc.T($"Niveau max de la zone ({HuntXp.MaxLevel(context.Territory)}) : l'XP ne compte plus ici.", $"Zone max level ({HuntXp.MaxLevel(context.Territory)}): experience no longer counts here.")
                    : tip);
        }
        var gap = Math.Clamp(configuration.LevelGap, 0, HuntOptions.MaxGap);
        ImGui.SetNextItemWidth(140);
        if (ImGui.SliderInt(Loc.T("Écart de niveau", "Level gap") + "##gap", ref gap, 0, HuntOptions.MaxGap, "+%d"))
        {
            configuration.LevelGap = gap;
            save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Loc.T("Jusqu'à combien de niveaux au-dessus de toi tu tapes : les mobs plus forts ne sont pas proposés.\n" +
                "L'XP monte jusqu'à +6, pas au-delà. Les éclats ne tombent que des mobs de ton niveau ou plus.",
                "How many levels above you you hit: stronger mobs are not proposed.\n" +
                "Experience grows up to +6, not beyond. Logograms only drop from mobs at or above your level."));
        ImGui.SameLine();
        var countParty = configuration.CountParty;
        if (ImGui.Checkbox(Loc.T("Compter le groupe", "Count the party") + "##party", ref countParty))
        {
            configuration.CountParty = countParty;
            save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Loc.T($"Les kills sont partagés entre les joueurs (jusqu'à {HuntOptions.MaxSharing}) : temps plus courts.\nLe groupe réel compte s'il est plus grand. Les coffres lapin ne se partagent pas.",
                $"Kills are shared by the players (up to {HuntOptions.MaxSharing}): shorter times.\nThe actual party counts when it is bigger. Bunny coffers are not shared."));
        if (configuration.CountParty)
        {
            ImGui.SameLine();
            var party = Math.Clamp(configuration.FarmPartySize, 1, 8);
            ImGui.SetNextItemWidth(110);
            if (ImGui.SliderInt("##partysize", ref party, 1, 8, Loc.T("%d joueur(s)", "%d player(s)")))
            {
                configuration.FarmPartySize = party;
                save();
            }
            if (context.PartySize > party)
            {
                ImGui.SameLine();
                ImGui.TextColored(Grey, Loc.T($"(groupe : {context.PartySize})", $"(party: {context.PartySize})"));
            }
        }
        if (maxLevel) return HuntMode.Logos;
        if (configuration.HuntMode == HuntMode.Mix)
        {
            ImGui.TextColored(Grey, Loc.T("Part de l'XP :", "Experience share:"));
            foreach (var (percent, fr, en) in Presets)
            {
                ImGui.SameLine();
                if (ImGui.RadioButton($"{Loc.T(fr, en)} ({percent} %)##mix{percent}", configuration.XpImportance == percent))
                {
                    configuration.XpImportance = percent;
                    save();
                }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(Loc.T("À 50 % : une heure qui rapporte autant d'XP qu'un farm de mobs à +3 niveaux compte pour une demi-heure.",
                    "At 50%: an hour giving as much experience as grinding mobs 3 levels above counts as half an hour."));
        }
        return configuration.HuntMode;
    }

    /// <summary>XP mode: the experience buffs, a few tips, then the sources of the zone, best first.</summary>
    private void DrawXp(CharacterState state, HuntContext context)
    {
        var zone = EurekaWeather.Zone(context.Territory)?.Name ?? "?";
        ImGui.TextColored(Grey, context.Level is { } level
            ? Loc.T($"à {zone}, niveau {level} (max {HuntXp.MaxLevel(context.Territory)})",
                $"in {zone}, level {level} (max {HuntXp.MaxLevel(context.Territory)})")
            : Loc.T($"à {zone} (niveau inconnu)", $"in {zone} (level unknown)"));

        var statuses = playerStatuses();
        Buff(statuses.Contains(XpOptions.HarmonyStatus), Loc.T("Potion d'harmonie", "Potion of Harmony"),
            Loc.T("+10 % d'XP pendant 1 h (1 h 30 HQ) ; au marché, ou fabriquée", "+10% experience for 1 h (1 h 30 HQ); market board, or crafted"));
        ImGui.SameLine();
        Buff(statuses.Contains(XpOptions.FairyStatus), Loc.T("bénédiction des fées", "elementals' blessing"),
            Loc.T("+XP et dégâts pendant 1 h : reste près d'une fée élémentaire (3 par zone, souvent annoncées en /shout)",
                "+experience and damage for 1 h: stand by an Eureka elemental (3 per zone, often shouted)"));
        ImGui.SameLine();
        ImGui.TextColored(Grey, Loc.T("· conseils", "· tips"));
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Loc.T("Carnet d'objectifs Eurêka (onglet Autres, chaque semaine) : beaucoup d'XP, il se fait aussi à Anemos (niveau ramené à 20).\n" +
                "En groupe, un membre à 8 niveaux d'écart ou plus : plus personne ne gagne d'XP sur les mobs.\n" +
                "NM plus d'un niveau au-dessus de toi : XP réduite et aucune récompense.\n" +
                "Mort puis retour au camp : perte d'XP, voire d'un niveau ; demande une résurrection en /shout.",
                "Eureka challenge log (Other tab, weekly): lots of experience, also doable in Anemos (level synced to 20).\n" +
                "In a party, a member 8 or more levels apart: nobody gets experience from mobs.\n" +
                "NM more than one level above you: reduced experience and no reward.\n" +
                "Dying then returning to camp costs experience, even a level; ask for a raise in /shout."));

        var useful = state.StockReadAt == null
            ? new HashSet<uint>()
            : Hunt.Compute(catalog, nmCatalog.Zones, state.Stock, state.Learned).Shards.Where(s => s.UsefulChance > 0).Select(s => s.Shard).ToHashSet();
        var options = XpOptions.For(farms, context, state.Learned, useful);
        ImGui.Spacing();
        ImGui.Separator();
        if (options.Count == 0)
        {
            ImGui.TextColored(Grey, Loc.T("Aucune source connue à ton niveau dans cette zone.", "No known source at your level in this zone."));
            return;
        }
        using var list = ImRaii.Child("##xp", Vector2.Zero, false);
        if (!list) return;
        using var table = ImRaii.Table("##xptable", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit);
        if (!table) return;
        ImGui.TableSetupColumn(Loc.T("cible", "target"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(Loc.T("quand", "when"));
        ImGui.TableSetupColumn("XP");
        ImGui.TableSetupColumn(Loc.T("boutons", "buttons"));
        ImGui.TableHeadersRow();
        foreach (var (option, i) in options.Select((o, i) => (o, i)))
        {
            using var id = ImRaii.PushId(i);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var label = XpLabel(option);
            ImGui.TextUnformatted(label);
            if (option.Notes.Count > 0 && ImGui.IsItemHovered()) ImGui.SetTooltip(string.Join("\n", option.Notes));
            foreach (var shard in option.UsefulShards)
            {
                ImGui.SameLine(0, 4);
                var text = texts.Item(shard);
                Icon(text.Icon, new Vector2(18));
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(Loc.T($"Lâche aussi {GameTexts.WithoutUnidentified(text.Name)}, utile pour tes actions Logos",
                        $"Also drops {GameTexts.WithoutUnidentified(text.Name)}, useful for your Logos actions"));
            }
            ImGui.TableNextColumn();
            if (option.Status is { } status) ImGui.TextColored(status.IsDoable(context.Now) ? Green : Amber, status.Describe(context.Now, weatherName));
            else WindowText(option.Window, option.Farm != null ? Condition(option.Farm) : PrepCondition(option.PrepFor), context.Now);
            ImGui.TableNextColumn();
            XpRate(option);
            ImGui.TableNextColumn();
            if (option.Nm is { } nm)
            {
                if (ImGui.SmallButton(Loc.T("Carte", "Map") + "##map")) showNm(nm);
                ImGui.SameLine();
            }
            else if (option.Places.Count > 0)
            {
                if (ImGui.SmallButton(Loc.T("Carte", "Map") + "##map")) showFarm(label, option.Places);
                ImGui.SameLine();
            }
            if (option.Places.Count > 0 && ImGui.SmallButton(Loc.T("Drapeau", "Flag") + "##flag")) setFlag(context.Territory, option.Places);
        }
    }

    /// <summary>Green "Potion d'harmonie ✓" when active, amber "pas de …" otherwise, with what it gives.</summary>
    private static void Buff(bool active, string name, string tip)
    {
        if (active) ImGui.TextColored(Green, name + " ✓");
        else ImGui.TextColored(Amber, Loc.T($"pas de {name}", $"no {name}"));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(tip);
    }

    /// <summary>"×1.4 · moyen": experience per minute against the reference rhythm, then the difficulty.</summary>
    private static void XpRate(XpOption option)
    {
        var relative = option.Relative;
        ImGui.TextColored(relative >= 1 ? Green : relative >= 0.5 ? Amber : Grey, $"×{relative:0.0}");
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
        if (!hovered) return;
        var hour = option.Score / HuntXp.ReferenceRate;
        ImGui.SetTooltip(Loc.T($"XP par minute : ×{relative:0.00} le rythme d'un duo sur des mobs à +3 niveaux\n" +
                $"sur l'heure qui vient, attente comprise : ×{hour:0.00}\ndifficulté : {difficulty} (écart de niveau, mob qui mute ou s'adapte, NM)",
            $"Experience per minute: ×{relative:0.00} the rhythm of a duo on mobs 3 levels above\n" +
                $"over the next hour, wait included: ×{hour:0.00}\ndifficulty: {difficulty} (level gap, mob that mutates or adapts, NM)"));
    }

    /// <summary>"Chaîne de 30 (mobs Nv 52)", "NM Skoll Nv 50", "Bombe Nv 47 (prépa de …)", "Sprite d'orage Nv 46 adapté".</summary>
    private string XpLabel(XpOption option) => option.Kind switch
    {
        XpKind.Chain => Loc.T($"Chaîne de 30 (mobs Nv {option.Level})", $"Chain of 30 (lv {option.Level} mobs)"),
        XpKind.Nm => $"NM {texts.Npc(option.Npc)} {Loc.T("Nv", "lv")} {option.Level}",
        _ => $"{texts.Npc(option.Npc)} {Loc.T("Nv", "lv")} {option.Level}"
            + (option.Farm is { Adapted: true } ? Loc.T(" adapté", " adapted") : option.Farm is { Mutated: true } ? Loc.T(" muté", " mutated") : "")
            + (option.PrepFor is { } nm ? Loc.T($" (prépa de {texts.Npc(nm.Npc)})", $" (prep of {texts.Npc(nm.Npc)})") : ""),
    };

    /// <summary>The weather or night a prep mob waits for; null when none.</summary>
    private string? PrepCondition(EurekaNm? nm) => nm?.Prep.Condition switch
    {
        { Weather: { } weather } => weatherName(weather),
        { Night: true } => Loc.T("Nuit", "Night"),
        _ => null,
    };

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

    private void DrawShard(HuntShard shard, IReadOnlyList<HuntOption> options, HuntContext context)
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
            ImGui.TextColored(Grey, Loc.T("hors de portée", "out of reach"));
            return;
        }
        WindowText(option.Window, Condition(option.Farm), now);
    }

    /// <summary>"Orages ou Neige", "Nuit", "Jour": what the farm waits for; null when nothing.</summary>
    private string? Condition(Farm? farm) => farm is { Weathers.Count: > 0 } f
        ? string.Join(Loc.T(" ou ", " or "), f.Weathers.Select(weatherName))
        : farm?.Time switch { "night" => Loc.T("Nuit", "Night"), "day" => Loc.T("Jour", "Day"), _ => null };

    /// <summary>Amber "Orages dans 12 min", green "Orages en cours (encore 8 min)", green "maintenant".</summary>
    private static void WindowText(TimeWindow? window, string? condition, DateTimeOffset now)
    {
        if (window is { } w && !w.IsOpen(now))
            ImGui.TextColored(Amber, Loc.T($"{condition} dans", $"{condition} in") + $" {Formatting.Duration(w.Start - now)}");
        else if (window is { } open)
            ImGui.TextColored(Green, Loc.T($"{condition} en cours (encore {Formatting.Duration(open.End - now)})", $"{condition} now ({Formatting.Duration(open.End - now)} left)"));
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

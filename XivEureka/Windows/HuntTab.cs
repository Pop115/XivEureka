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

    public void Draw()
    {
        var state = currentCharacter();
        if (state == null)
        {
            ImGui.TextWrapped("Connecte-toi à un personnage pour voir quoi taper.");
            return;
        }

        ImGui.TextUnformatted("QUOI TAPER");
        if (state.StockReadAt == null)
        {
            ImGui.TextColored(Yellow, "Mnemes jamais lues : ouvre la fenêtre des éclats au manipulateur Logos.");
            return;
        }
        if (state.LearnedReadAt == null)
            ImGui.TextColored(Yellow, "Actions apprises jamais lues : ouvre le journal chez Drake (sinon tout compte comme à apprendre).");

        var plan = Hunt.Compute(catalog, nmCatalog.Zones, state.Stock, state.Learned);
        var context = contextNow();
        ImGui.SameLine();
        ImGui.TextColored(Grey, $"{plan.NotLearned} action(s) pas encore apprise(s) · mnemes du {state.StockReadAt:dd/MM HH:mm}");

        if (plan.CraftableNow.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(Green, $"Fabricables maintenant ({plan.CraftableNow.Count}) : va au mélangeur Logos");
            ActionIcons(plan.CraftableNow);
        }
        if (plan.Shards.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(Grey, plan.NotLearned == 0 ? "Toutes les actions Logos sont apprises." : "Plus rien à chasser : tout le reste est fabricable.");
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
        ImGui.TextColored(Yellow, "LE PLUS RAPIDE");
        ImGui.SameLine();
        ImGui.TextColored(Grey, context.Level is { } level ? $"à {zone}, niveau {level}" : $"à {zone} (niveau inconnu)");
        var fastest = HuntOptions.Fastest(plan, farms, context);
        if (fastest.Count == 0)
        {
            ImGui.TextColored(Grey, "Aucune source connue dans cette zone pour les éclats qui te manquent.");
        }
        using (var table = ImRaii.Table("##fastest", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
        {
            if (table)
            {
                ImGui.TableSetupColumn("éclat");
                ImGui.TableSetupColumn("source", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("quand");
                ImGui.TableSetupColumn("boutons");
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
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Sert à {shard.Actions.Count} action(s) · ≈ {shard.UsefulChance:P0} de chances d'un mneme utile");
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(Label(option));
                    if (option.Notes.Count > 0 && ImGui.IsItemHovered()) ImGui.SetTooltip(string.Join("\n", option.Notes));
                    ImGui.TableNextColumn();
                    When(option, context);
                    ImGui.TableNextColumn();
                    MapButton(option);
                }
            }
        }
        ImGui.TextColored(Grey, "Temps estimés à la louche, pour classer : attente (météo, respawn, FATE) + effort selon la source, divisé par la chance d'un mneme utile.");
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
        ImGui.TextColored(Grey, $"sert à {shard.Actions.Count} action(s) · à l'analyse, 1 mneme parmi {shard.Mnemes.Count} : {useful} utile(s), ≈ {shard.UsefulChance:P0} de chances");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("En supposant que les mnemes sortent à chances égales (le jeu ne le dit pas).\n" +
                "Les temps estimés en tiennent compte : il faut en moyenne " +
                $"{1 / shard.UsefulChance:0.#} éclat(s) pour un mneme utile.");
        ActionIcons(shard.Actions);
        ImGui.EndGroup();

        ImGui.Indent(40);
        var options = HuntOptions.For(shard, farms, context);
        if (options.Count == 0)
            ImGui.TextColored(Grey, "Aucune source connue (zone pas encore dans les données du plugin, ou pas à ton niveau).");
        for (var i = 0; i < Math.Min(ShownSources, options.Count); i++) DrawOption(options[i], i, context);
        if (options.Count > ShownSources)
        {
            using var more = ImRaii.TreeNode($"{options.Count - ShownSources} autre(s) source(s)##more");
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
                ImGui.TextColored(Grey, $"{line} : pas utile pour l'instant");
                continue;
            }
            ImGui.TextUnformatted(line);
            ImGui.SameLine();
            ImGui.TextColored(Grey, "manque pour : " + string.Join(", ",
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
        if (option.Notes.Count > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Grey, "· " + string.Join(" · ", option.Notes));
        }
        ImGui.SameLine();
        MapButton(option);
    }

    /// <summary>"Sprite d'orage Nv 41", "FATE lapin « … »", "Coffre de mutation : …", "NM (Nv 45, Pyros)".</summary>
    private string Label(HuntOption option)
    {
        if (option.Nm is { } nm)
        {
            var zone = EurekaWeather.Zone(nm.Territory)?.Name ?? $"zone {nm.Territory}";
            return $"NM {texts.Npc(nm.Nm.Npc)} (Nv {nm.Nm.Level}, {zone})";
        }
        var farm = option.Farm!;
        return farm.Kind switch
        {
            FarmKind.Bunny => $"FATE lapin « {texts.Fate(farm.Fate)} »",
            FarmKind.Lockbox => $"Coffre de mutation : {texts.Npc(farm.Npc)} Nv {farm.Level}",
            _ => $"{texts.Npc(farm.Npc)} Nv {farm.Level}" + (farm.Adapted ? " adapté" : ""),
        };
    }

    /// <summary>When the source is there: green now, amber to come (with the weather), grey unknown.</summary>
    private void When(HuntOption option, HuntContext context)
    {
        var now = context.Now;
        if (option.Farm is { Kind: FarmKind.Bunny } bunny && option.Eta != null)
        {
            if (context.BunnyFates.Contains(bunny.Fate)) ImGui.TextColored(Green, "FATE en cours");
            else ImGui.TextColored(Amber, "attendre la FATE");
            return;
        }
        if (option.Status is { } status)
        {
            ImGui.TextColored(status.IsDoable(now) ? Green : Amber, status.Describe(now, weatherName));
            return;
        }
        if (option.Eta == null)
        {
            ImGui.TextColored(Grey, option.Nm != null ? "état inconnu (autre zone)" : "autre zone");
            return;
        }
        var weather = option.Farm is { Weathers.Count: > 0 } f
            ? string.Join(" ou ", f.Weathers.Select(weatherName))
            : option.Farm?.Time switch { "night" => "Nuit", "day" => "Jour", _ => null };
        if (option.Window is { } w && !w.IsOpen(now))
            ImGui.TextColored(Amber, $"{weather} dans {Formatting.Duration(w.Start - now)}");
        else if (option.Window is { } open)
            ImGui.TextColored(Green, $"{weather} en cours (encore {Formatting.Duration(open.End - now)})");
        else
            ImGui.TextColored(Green, "maintenant");
    }

    /// <summary>"Carte": shows it on the plugin map; "Drapeau": game flag on the nearest place.</summary>
    private void MapButton(HuntOption option)
    {
        if (option.Places.Count == 0) return;
        if (ImGui.SmallButton("Carte"))
        {
            if (option.Nm is { } nm) showNm(nm.Nm);
            else showFarm(Label(option), option.Places);
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Drapeau")) setFlag(option.Territory, option.Places);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Pose le drapeau de la carte du jeu (sur l'endroit le plus proche de toi)");
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

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
/// "Quoi taper" tab: the chain from the Logos actions not learned yet to the NMs to hit. On top, the
/// actions craftable right now (go to the manipulator). Then one block per useful shard, most
/// useful first: the actions it moves forward, the NMs that drop it (with their state when in the
/// zone shown on the map, doable ones first) and the mnemes it can turn into, with what each one
/// is missing for. Computed by <see cref="Hunt"/>. User-facing texts are French for now.
/// </summary>
public sealed class HuntTab(
    LogosCatalog catalog,
    NmCatalog nmCatalog,
    GameTexts texts,
    ITextureProvider icons,
    Func<CharacterState?> currentCharacter,
    Func<(uint Territory, IReadOnlyList<NmStatus> Statuses)> statusesNow,
    Func<string, string> weatherName,
    Action<EurekaNm> showOnMap)
{
    private static readonly Vector4 Yellow = new(1f, 0.85f, 0.25f, 1f);
    private static readonly Vector4 Green = new(0.35f, 0.83f, 0.6f, 1f);
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
        var (territory, statuses) = statusesNow();
        var now = DateTimeOffset.UtcNow;
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
        foreach (var shard in plan.Shards) DrawShard(shard, territory, statuses, now);
    }

    private void DrawShard(HuntShard shard, uint territory, IReadOnlyList<NmStatus> statuses, DateTimeOffset now)
    {
        using var id = ImRaii.PushId((int)shard.Shard);
        var text = texts.Item(shard.Shard);
        Icon(text.Icon, new Vector2(32));
        ImGui.SameLine();
        ImGui.BeginGroup();
        ImGui.TextColored(Yellow, GameTexts.WithoutUnidentified(text.Name));
        ImGui.SameLine();
        ImGui.TextColored(Grey, $"sert à {shard.Actions.Count} action(s) · donne 1 mneme parmi {shard.Mnemes.Count} à l'analyse");
        ActionIcons(shard.Actions);
        ImGui.EndGroup();

        ImGui.Indent(40);
        if (shard.Sources.Count == 0)
            ImGui.TextColored(Grey, "Aucun NM connu ne le lâche (zone pas encore dans les données du plugin).");
        foreach (var source in Ordered(shard.Sources, territory, statuses, now))
        {
            using var nmId = ImRaii.PushId((int)source.Nm.Npc);
            var status = source.Territory == territory ? statuses.FirstOrDefault(s => s.Nm.Npc == source.Nm.Npc) : null;
            var zone = EurekaWeather.Zone(source.Territory)?.Name ?? $"zone {source.Territory}";
            ImGui.TextUnformatted($"{texts.Npc(source.Nm.Npc)} (Nv {source.Nm.Level}, {zone})");
            ImGui.SameLine();
            if (status != null)
                ImGui.TextColored(status.IsDoable(now) ? Green : Grey, status.Describe(now, weatherName));
            else
                ImGui.TextColored(Grey, "état inconnu (autre zone)");
            if (source.Territory == territory)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Carte")) showOnMap(source.Nm);
            }
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

    /// <summary>Doable now first, then NMs of the zone shown, then by level.</summary>
    private static IEnumerable<HuntSource> Ordered(IReadOnlyList<HuntSource> sources, uint territory,
        IReadOnlyList<NmStatus> statuses, DateTimeOffset now) =>
        sources.OrderByDescending(s => s.Territory == territory && statuses.FirstOrDefault(x => x.Nm.Npc == s.Nm.Npc) is { } st && st.IsDoable(now))
            .ThenByDescending(s => s.Territory == territory)
            .ThenBy(s => s.Nm.Level);

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

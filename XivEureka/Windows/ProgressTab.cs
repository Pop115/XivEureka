using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using XivEureka.Logos;
using XivEureka.Progress;
using XivEureka.Zones;

namespace XivEureka.Windows;

/// <summary>
/// "Progression" tab: the Eureka story zone by zone, the relic weapon and armor of a job (stage
/// reached, next stage and what it costs against what you own), and the relic materials in the
/// bags. Relic items are those seen in the containers the game has loaded, remembered per
/// character. Computed by <see cref="RelicProgress"/> and <see cref="StoryProgress"/>.
/// </summary>
public sealed class ProgressTab(
    RelicCatalog catalog,
    ProgressReader reader,
    GameTexts texts,
    ITextureProvider icons,
    Func<CharacterState?> currentCharacter,
    Func<int?> elementalLevel,
    Action save)
{
    private static readonly TimeSpan ReadInterval = TimeSpan.FromSeconds(1);
    private static readonly Vector4 Yellow = new(1f, 0.85f, 0.25f, 1f);
    private static readonly Vector4 Green = new(0.35f, 0.83f, 0.6f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.45f, 0.4f, 1f);
    private static readonly Vector4 Grey = new(0.6f, 0.6f, 0.65f, 1f);

    private ProgressSnapshot snapshot = ProgressSnapshot.Empty;
    private DateTime nextRead = DateTime.MinValue;
    private string? chosenJob;

    public void Draw()
    {
        var state = currentCharacter();
        if (state == null)
        {
            ImGui.TextWrapped(Loc.T("Connecte-toi à un personnage pour voir sa progression.", "Log in to a character to see its progress."));
            return;
        }
        Refresh(state);

        var owned = state.RelicItems;
        var jobs = RelicProgress.Jobs(catalog.Weapon, reader.Info);
        var job = chosenJob ?? (snapshot.Job is { } j && jobs.Contains(j) ? j : jobs.FirstOrDefault());

        ImGui.TextUnformatted(Loc.T("PROGRESSION", "PROGRESS"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(110);
        using (var combo = ImRaii.Combo("##job", job != null ? reader.JobName(job) : "?"))
        {
            if (combo)
                foreach (var candidate in jobs)
                    if (ImGui.Selectable(reader.JobName(candidate) + (candidate == snapshot.Job ? Loc.T(" (actuel)", " (current)") : ""), candidate == job))
                        chosenJob = candidate == snapshot.Job ? null : candidate;
        }
        ImGui.SameLine();
        ImGui.TextColored(Grey, Loc.T("Servants et coffre à tenues pas lus : le plugin retient les reliques déjà vues.",
            "Retainers and the glamour dresser are not read: the plugin remembers the relics it has seen."));

        using var child = ImRaii.Child("##progress", Vector2.Zero, false);
        if (!child) return;

        if (ImGui.CollapsingHeader(Loc.T("Histoire", "Story") + "##story", ImGuiTreeNodeFlags.DefaultOpen)) DrawStory(state);
        if (job == null) return;
        if (ImGui.CollapsingHeader(Loc.T("Arme relique", "Relic weapon") + "##weapon", ImGuiTreeNodeFlags.DefaultOpen))
            DrawLine(catalog.Weapon, job, owned, state, allJobs: jobs);
        if (ImGui.CollapsingHeader(Loc.T("Armure relique", "Relic armor") + "##armor", ImGuiTreeNodeFlags.DefaultOpen))
            DrawLine(catalog.Armor, job, owned, state, allJobs: null);
        if (ImGui.CollapsingHeader(Loc.T("Matériaux dans les sacs", "Materials in the bags") + "##materials")) DrawMaterials();
    }

    /// <summary>Reads the game once a second, and remembers the relic items seen.</summary>
    private void Refresh(CharacterState state)
    {
        if (DateTime.UtcNow < nextRead) return;
        nextRead = DateTime.UtcNow + ReadInterval;
        snapshot = reader.Read();
        var before = state.RelicItems.Count;
        state.RelicItems.UnionWith(snapshot.Relics);
        if (state.RelicItems.Count != before) save();
    }

    // ---------- story ----------

    private void DrawStory(CharacterState state)
    {
        var level = elementalLevel();
        using var table = ImRaii.Table("##story-table", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit);
        if (!table) return;
        ImGui.TableSetupColumn("zone");
        ImGui.TableSetupColumn("state");
        ImGui.TableSetupColumn("steps", ImGuiTableColumnFlags.WidthStretch);
        for (var i = 0; i < catalog.Story.Count; i++)
        {
            var zone = catalog.Story[i];
            var status = StoryProgress.State(catalog.Story, i, snapshot.QuestsDone, state.StorySteps);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(EurekaWeather.Zone(zone.Territory)?.Name ?? $"{zone.Territory}");
            ImGui.TableNextColumn();
            switch (status)
            {
                case StoryState.Done: ImGui.TextColored(Green, Loc.T("terminée", "done")); break;
                case StoryState.InProgress: ImGui.TextColored(Yellow, Loc.T("en cours", "in progress")); break;
                default: ImGui.TextColored(Grey, Loc.T("pas ouverte", "not open")); break;
            }
            ImGui.TableNextColumn();
            if (status == StoryState.Locked)
            {
                ImGui.TextColored(Grey, Loc.T("quête d'accès : ", "access quest: ") + texts.Quest(zone.AccessQuest));
                continue;
            }
            if (status == StoryState.Done)
            {
                ImGui.TextColored(Grey, Loc.T($"{zone.Steps.Count} étapes faites", $"{zone.Steps.Count} steps done"));
                continue;
            }
            foreach (var step in zone.Steps)
            {
                var key = StoryProgress.Key(zone, step);
                var done = StoryProgress.StepDone(catalog.Story, i, step, snapshot.QuestsDone, state.StorySteps);
                var label = Loc.T($"Nv {step.Level}", $"lv {step.Level}") + (step.Note is { } note ? $" · {note}" : "");
                if (ImGui.Checkbox($"{label}##{key}", ref done))
                {
                    if (done) state.StorySteps.Add(key);
                    else state.StorySteps.Remove(key);
                    save();
                }
                if (!done && level is { } l && l < step.Level)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(Grey, Loc.T($"(à {step.Level - l} niveau(x))", $"({step.Level - l} level(s) to go)"));
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(Loc.T("Étape de Krile : le jeu ne l'expose pas, coche-la quand elle est faite.",
                    "Krile's step: the game does not expose it, tick it once done."));
            }
        }
    }

    // ---------- relic lines ----------

    private void DrawLine(IReadOnlyList<RelicStage> stages, string job, IReadOnlySet<uint> owned, CharacterState state,
        IReadOnlyList<string>? allJobs)
    {
        var slots = RelicProgress.For(stages, job, owned, reader.Info);
        if (slots.Count == 0)
        {
            ImGui.TextColored(Grey, Loc.T("Pas de relique d'Eurêka pour ce job.", "No Eureka relic for this job."));
            return;
        }
        var learned = state.LearnedReadAt != null ? state.Learned.Count : (int?)null;

        foreach (var slot in slots)
        {
            using var id = ImRaii.PushId((int)slot.Slot);
            ItemLine(slot.Owned, Loc.T("aucune vue", "none seen"));
            ImGui.SameLine();
            var reached = slot.Stage is { } s ? s + 1 : 0;
            ImGui.TextColored(Grey, Loc.T($"étape {reached}/{stages.Count}", $"stage {reached}/{stages.Count}"));

            ImGui.Indent(32);
            if (slot.Done)
                ImGui.TextColored(Green, Loc.T("Terminée.", "Done."));
            else if (slot.NextStage is { } next)
            {
                ImGui.TextColored(Grey, "→");
                ImGui.SameLine();
                ItemLine(slot.NextItem, "?");
                ImGui.SameLine();
                ImGui.TextColored(Grey, Where(stages[next]));
                if (slot.NextCost.Count == 0 && stages[next].Zone == 0)
                    ImGui.TextColored(Grey, Loc.T("Quête de job niveau 70 (ou le Récupérateur de la Calamité).", "Level 70 job quest (or the Calamity Salvager)."));
                foreach (var cost in slot.NextCost) CostLine(cost);
                if (slot.NextLogos > 0)
                {
                    var ok = learned >= slot.NextLogos;
                    ImGui.TextColored(ok ? Green : Red, Loc.T($"Actions Logos apprises : {learned?.ToString() ?? "?"}/{slot.NextLogos}",
                        $"Logos actions learned: {learned?.ToString() ?? "?"}/{slot.NextLogos}"));
                }
            }
            ImGui.Unindent(32);
        }

        var remaining = RelicProgress.RemainingToEnd(stages, slots);
        if (remaining.Count > 0)
        {
            ImGui.TextColored(Grey, Loc.T("Reste jusqu'à la dernière étape :", "Left to the last stage:"));
            ImGui.SameLine();
            ImGui.TextColored(Grey, string.Join(" · ", remaining.Select(kv =>
                $"{kv.Value - Math.Min(kv.Value, snapshot.Materials.GetValueOrDefault(kv.Key))} {texts.Item(kv.Key).Name}")));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(Loc.T("Déjà déduit : ce que tu as dans tes sacs.", "Already deducted: what you have in your bags."));
        }

        if (allJobs == null) return;
        ImGui.Spacing();
        using var table = ImRaii.Table("##all-jobs", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit);
        if (!table) return;
        ImGui.TableSetupColumn(Loc.T("job", "job"));
        ImGui.TableSetupColumn(Loc.T("arme", "weapon"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(Loc.T("niv. d'objet", "item level"));
        ImGui.TableHeadersRow();
        foreach (var other in allJobs)
        {
            var main = RelicProgress.For(stages, other, owned, reader.Info).FirstOrDefault(p => p.Slot == RelicSlot.MainHand);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextColored(other == job ? Yellow : Grey, reader.JobName(other));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(main?.Owned is { } item ? texts.Item(item).Name : "—");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(main?.Stage is { } st ? stages[st].ItemLevel.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
        }
    }

    private static string Where(RelicStage stage) =>
        stage.Zone == 0 ? "" : $"({EurekaWeather.Zone(stage.Zone)?.Name}, {Loc.T("niv. d'objet", "item level")} {stage.ItemLevel})";

    private void CostLine(RelicCost cost)
    {
        var have = snapshot.Materials.GetValueOrDefault(cost.Item);
        var text = texts.Item(cost.Item);
        Icon(text.Icon, new Vector2(18));
        ImGui.SameLine();
        ImGui.TextColored(have >= cost.Count ? Green : Red, $"{text.Name} {have}/{cost.Count}");
    }

    private void ItemLine(uint? item, string none)
    {
        if (item is not { } it)
        {
            ImGui.Dummy(new Vector2(24));
            ImGui.SameLine();
            ImGui.TextColored(Grey, none);
            return;
        }
        var text = texts.Item(it);
        Icon(text.Icon, new Vector2(24));
        ImGui.SameLine();
        ImGui.TextUnformatted(text.Name);
    }

    private void DrawMaterials()
    {
        foreach (var item in catalog.Materials)
        {
            var text = texts.Item(item);
            Icon(text.Icon, new Vector2(20));
            ImGui.SameLine();
            ImGui.TextUnformatted(Loc.T($"{text.Name} : {snapshot.Materials.GetValueOrDefault(item)}", $"{text.Name}: {snapshot.Materials.GetValueOrDefault(item)}"));
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

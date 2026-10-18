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

namespace XivEureka.Windows;

/// <summary>
/// The Logos shard page, modelled on ffxiv-eureka.com/logograms. On the left, the notebook grid (10
/// per row, in game order): colored icon if the action is learned, dark cell otherwise, light cell
/// if it is not learned but craftable; in yellow, how many can be crafted. Below, the mnemes by
/// shard with the actions each one is used in. On the right, the chosen action's sheet.
/// "Actions Logos" tab of the main window.
/// </summary>
public sealed class LogosTab
{
    private const int Columns = 10;
    private const float CellSize = 44;
    private const float Gap = 6;
    public const float SheetWidth = 330;

    private static readonly Vector4 Yellow = new(1f, 0.85f, 0.25f, 1f);
    private static readonly Vector4 Green = new(0.35f, 0.83f, 0.6f, 1f);
    private static readonly Vector4 Red = new(1f, 0.45f, 0.4f, 1f);
    private static readonly Vector4 Grey = new(0.6f, 0.6f, 0.65f, 1f);
    private static readonly Vector4 EmptyCell = new(0.13f, 0.13f, 0.15f, 1f);
    private static readonly Vector4 EmptyBorder = new(0.3f, 0.3f, 0.34f, 1f);
    private static readonly Vector4 LightCell = new(0.92f, 0.92f, 0.95f, 1f);

    private static (string Letter, string Name)[] Roles =>
    [
        ("T", "tank"), ("H", Loc.T("soigneur", "healer")), ("M", Loc.T("mêlée", "melee")),
        ("P", Loc.T("distance physique", "physical ranged")), ("C", Loc.T("distance magique", "caster")),
    ];

    private readonly LogosCatalog catalog;
    private readonly GameTexts texts;
    private readonly ITextureProvider icons;
    private readonly Func<CharacterState?> currentCharacter;
    private readonly Func<bool> liveStock;
    private readonly Func<char?> currentRole;
    private readonly Action save;

    private int? chosen;

    public LogosTab(LogosCatalog catalog, GameTexts texts, ITextureProvider icons,
        Func<CharacterState?> currentCharacter, Func<bool> liveStock, Func<char?> currentRole, Action save)
    {
        this.catalog = catalog;
        this.texts = texts;
        this.icons = icons;
        this.currentCharacter = currentCharacter;
        this.liveStock = liveStock;
        this.currentRole = currentRole;
        this.save = save;
    }

    public void Draw()
    {
        var state = currentCharacter();
        if (state == null)
        {
            ImGui.TextWrapped(Loc.T("Connecte-toi à un personnage pour voir ses mnemes.", "Log in to a character to see its mnemes."));
            return;
        }

        var board = LogosBoard.Of(catalog, state);
        var states = board.Actions.ToDictionary(e => e.Key, e => e.Value);
        var height = ImGui.GetContentRegionAvail().Y;
        using (var left = ImRaii.Child("##notebook", new Vector2(ImGui.GetContentRegionAvail().X - SheetWidth - Gap, height), false))
        {
            if (left)
            {
                DrawHeader(state, board);
                DrawGrid(state, states);
                ImGui.Spacing();
                ImGui.Separator();
                DrawMnemes(state, states);
            }
        }
        ImGui.SameLine(0, Gap);
        using (var right = ImRaii.Child("##sheet", new Vector2(0, height), true))
            if (right) DrawSheet(state, states);
    }

    // ---------- header ----------

    private void DrawHeader(CharacterState state, LogosBoard board)
    {
        ImGui.TextUnformatted(Loc.T("ACTIONS LOGOS", "LOGOS ACTIONS"));
        ImGui.SameLine();
        ImGui.TextColored(Yellow, $"{board.Learned}");
        ImGui.SameLine(0, 0);
        ImGui.TextUnformatted($"/{board.Total}");
        if (board.ToLearn > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Yellow, Loc.T($"· {board.ToLearn} à apprendre maintenant (cases qui clignotent)",
                $"· {board.ToLearn} to learn now (blinking cells)"));
        }

        ImGui.TextColored(Grey, Provenance(state));
        ImGui.TextColored(Grey, Loc.T("Clic : recettes de l'action. Clic droit : apprise / pas apprise.",
            "Click: the action's recipes. Right click: learned / not learned."));
        ImGui.Spacing();
    }

    private string Provenance(CharacterState state)
    {
        var stock = liveStock() ? Loc.T("Mnemes lues en direct", "Mnemes read live")
            : state.StockReadAt is { } s ? Loc.T($"Mnemes du {s:dd/MM HH:mm}", $"Mnemes as of {s:MM/dd HH:mm}")
            : Loc.T("Mnemes jamais lues : ouvre la fenêtre des éclats au manipulateur",
                "Mnemes never read: open the logogram window at the manipulator");
        var learned = state.LearnedReadAt is { } l ? Loc.T($"apprises du {l:dd/MM HH:mm}", $"learned as of {l:MM/dd HH:mm}")
            : Loc.T("apprises jamais lues : ouvre le journal chez Drake", "learned never read: open the notebook at Drake");
        return $"{stock} · {learned}";
    }

    // ---------- notebook grid ----------

    private void DrawGrid(CharacterState state, Dictionary<int, ActionState> states)
    {
        var actions = catalog.Actions.OrderBy(a => a.Number).ToList();
        for (var i = 0; i < actions.Count; i++)
        {
            if (i % Columns != 0) ImGui.SameLine(0, Gap);
            ActionCell(state, states[actions[i].Number], CellSize);
        }
    }

    /// <summary>An action cell, reused small in the mneme list.</summary>
    private void ActionCell(CharacterState state, ActionState e, float size, string suffix = "")
    {
        var text = texts.Action(e.Action.Action);
        var corner = ImGui.GetCursorScreenPos();
        var end = corner + new Vector2(size);
        if (ImGui.InvisibleButton($"##a{e.Action.Number}{suffix}", new Vector2(size))) chosen = e.Action.Number;
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) ToggleLearned(state, e.Action.Action);
        var hovered = ImGui.IsItemHovered();

        var draw = ImGui.GetWindowDrawList();
        var rounding = size / 8;
        if (e.Learned)
        {
            if (IconTexture(text.Icon) is { } image) draw.AddImageRounded(image, corner, end, Vector2.Zero, Vector2.One, 0xFFFFFFFF, rounding);
        }
        else if (e.Craftable > 0)
        {
            // To learn: colored icon in a pulsing yellow halo, so it stands out.
            var pulse = 0.55f + (0.45f * MathF.Sin((float)ImGui.GetTime() * 4f));
            for (var halo = 1; halo <= 4; halo++)
                draw.AddRect(corner - new Vector2(halo * 1.5f), end + new Vector2(halo * 1.5f),
                    ImGui.GetColorU32(Yellow with { W = pulse * (0.9f - (halo * 0.18f)) }), rounding + halo, ImDrawFlags.None, 2);
            draw.AddRectFilled(corner, end, ImGui.GetColorU32(LightCell), rounding);
            if (IconTexture(text.Icon) is { } image) draw.AddImageRounded(image, corner, end, Vector2.Zero, Vector2.One, 0xFFFFFFFF, rounding);
            if (size >= 32)
            {
                var badge = corner + new Vector2(7, 7);
                draw.AddCircleFilled(badge, 7, ImGui.GetColorU32(Yellow));
                draw.AddText(badge - (ImGui.CalcTextSize("!") / 2), 0xFF000000, "!");
            }
        }
        else
        {
            draw.AddRectFilled(corner, end, ImGui.GetColorU32(EmptyCell), rounding);
            draw.AddRect(corner, end, ImGui.GetColorU32(EmptyBorder), rounding);
        }
        if (chosen == e.Action.Number) draw.AddRect(corner - new Vector2(2), end + new Vector2(2), ImGui.GetColorU32(Yellow), rounding, ImDrawFlags.None, 2);

        if (e.Craftable > 0 && size >= 32)
        {
            var count = e.Craftable.ToString(CultureInfo.InvariantCulture);
            var position = end - ImGui.CalcTextSize(count) - new Vector2(2, 0);
            draw.AddText(position + new Vector2(1, 1), 0xFF000000, count);
            draw.AddText(position + new Vector2(-1, 1), 0xFF000000, count);
            draw.AddText(position, ImGui.GetColorU32(Yellow), count);
        }

        if (hovered)
        {
            var learnedText = e.Learned ? Loc.T("apprise", "learned") : Loc.T("pas apprise", "not learned");
            var detail = e.Craftable > 0 ? Loc.T($"fabricable ×{e.Craftable}", $"craftable ×{e.Craftable}")
                : Loc.T($"il manque {e.Closest.Missing} mneme(s)", $"{e.Closest.Missing} mneme(s) missing");
            ImGui.SetTooltip($"{text.Name}\n{learnedText} · {detail}");
        }
    }

    // ---------- mnemes ----------

    private void DrawMnemes(CharacterState state, Dictionary<int, ActionState> states)
    {
        ImGui.TextUnformatted("MNEMES");
        ImGui.SameLine();
        ImGui.TextColored(Grey, Loc.T("par éclat Logos · actions qui s'en servent", "by logogram · actions that use them"));
        foreach (var shard in catalog.Shards)
        {
            ImGui.Spacing();
            var shardText = texts.Item(shard.Item);
            Icon(shardText.Icon, new Vector2(22));
            ImGui.SameLine();
            AlignText(22);
            ImGui.TextColored(Yellow, GameTexts.WithoutUnidentified(shardText.Name));
            foreach (var item in shard.Mnemes) MnemeRow(state, states, item);
        }
    }

    private void MnemeRow(CharacterState state, Dictionary<int, ActionState> states, uint item)
    {
        const float height = 28;
        var text = texts.Item(item);
        var quantity = state.Stock.GetValueOrDefault(item);

        ImGui.Dummy(new Vector2(12, height));
        ImGui.SameLine(0, 0);
        Icon(text.Icon, new Vector2(height));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(text.Name);
        ImGui.SameLine();
        var nameStart = ImGui.GetCursorPosX();
        AlignText(height);
        if (quantity > 0) ImGui.TextColored(Yellow, quantity.ToString(CultureInfo.InvariantCulture));
        else ImGui.TextColored(Grey, "0");
        ImGui.SameLine(nameStart + 34);
        AlignText(height);
        if (quantity > 0) ImGui.TextUnformatted(text.ShortName);
        else ImGui.TextColored(Grey, text.ShortName);

        var usedIn = catalog.Actions.Where(a => a.Recipes.Any(r => r.Contains(item))).OrderBy(a => a.Number);
        ImGui.SameLine(nameStart + 230);
        var first = true;
        foreach (var action in usedIn)
        {
            if (!first) ImGui.SameLine(0, 3);
            first = false;
            ActionCell(state, states[action.Number], height, $"m{item}");
        }
    }

    private static void AlignText(float lineHeight) =>
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + ((lineHeight - ImGui.GetTextLineHeight()) / 2));

    // ---------- action sheet ----------

    private void DrawSheet(CharacterState state, Dictionary<int, ActionState> states)
    {
        if (chosen is not { } number || !states.TryGetValue(number, out var e))
        {
            ImGui.TextWrapped(Loc.T("Clique une action pour voir sa description et ses recettes, avec ce que tu as de chaque mneme.",
                "Click an action to see its description and recipes, with what you own of each mneme."));
            return;
        }
        var text = texts.Action(e.Action.Action);
        Icon(text.Icon, new Vector2(48));
        ImGui.SameLine();
        ImGui.BeginGroup();
        ImGui.TextUnformatted(text.ShortName);
        // Wrapped: "mêlée, distance physique, distance magique" does not fit the sheet's width.
        ImGui.PushTextWrapPos(0);
        using (ImRaii.PushColor(ImGuiCol.Text, Grey))
            ImGui.TextUnformatted($"{Loc.T("n°", "#")} {e.Action.Number} · {RoleNames(e.Action.Roles)}");
        ImGui.PopTextWrapPos();
        if (e.Action.Uses is { } uses) ImGui.TextColored(Grey, Loc.T($"{uses} utilisation(s)", $"{uses} use(s)"));
        if (currentRole() is { } role && Roles.FirstOrDefault(r => r.Letter == role.ToString()) is { Name: { } roleName })
        {
            if (e.Action.Roles.Contains(role, StringComparison.Ordinal))
                ImGui.TextColored(Green, Loc.T($"✓ utilisable en {roleName} (ta classe)", $"✓ usable as {roleName} (your job)"));
            else
                ImGui.TextColored(Red, Loc.T($"pas utilisable en {roleName} (ta classe)", $"not usable as {roleName} (your job)"));
        }
        ImGui.EndGroup();

        var learned = e.Learned;
        if (ImGui.Checkbox(Loc.T("Apprise", "Learned") + "##learned", ref learned)) ToggleLearned(state, e.Action.Action);

        ImGui.Spacing();
        ImGui.PushTextWrapPos(0);
        ImGui.TextUnformatted(text.Description);
        ImGui.PopTextWrapPos();
        ImGui.Separator();
        ImGui.TextUnformatted(Loc.T("Recettes", "Recipes"));
        foreach (var recipe in e.Recipes)
        {
            ImGui.Spacing();
            if (recipe.Craftable > 0) ImGui.TextColored(Green, Loc.T($"×{recipe.Craftable} possible(s)", $"×{recipe.Craftable} possible"));
            else ImGui.TextColored(Grey, Loc.T($"il manque {recipe.Missing}", $"{recipe.Missing} missing"));
            foreach (var (item, need) in recipe.Needs)
            {
                var mneme = texts.Item(item);
                var owned = state.Stock.GetValueOrDefault(item);
                Icon(mneme.Icon, new Vector2(22));
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(mneme.Name);
                ImGui.SameLine();
                var line = $"{(need > 1 ? $"{need}× " : "")}{mneme.ShortName}  {owned}/{need}";
                if (owned >= need) ImGui.TextUnformatted(line);
                else ImGui.TextColored(Red, line);
            }
        }
    }

    private static string RoleNames(string letters) =>
        letters.Length == Roles.Length ? Loc.T("tous les rôles", "all roles")
            : string.Join(", ", Roles.Where(r => letters.Contains(r.Letter, StringComparison.Ordinal)).Select(r => r.Name));

    private void ToggleLearned(CharacterState state, uint action)
    {
        if (!state.Learned.Remove(action)) state.Learned.Add(action);
        save();
    }

    // ---------- icons ----------

    private ImTextureID? IconTexture(uint icon)
    {
        if (icon != 0 && icons.TryGetFromGameIcon(new GameIconLookup(icon), out var shared) && shared.TryGetWrap(out var image, out _))
            return image.Handle;
        return null;
    }

    private void Icon(uint icon, Vector2 size)
    {
        if (IconTexture(icon) is { } texture) ImGui.Image(texture, size);
        else ImGui.Dummy(size);
    }
}

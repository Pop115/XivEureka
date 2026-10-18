using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using XivEureka.Logos;
using XivEureka.Tracker;
using XivEureka.Zones;

namespace XivEureka.Windows;

/// <summary>What the Map tab gets from the rest of the plugin, besides the game data.</summary>
public sealed record MapSources(
    Func<IReadOnlyDictionary<ushort, DateTime>> EndedFates,
    Func<IReadOnlySet<ushort>> Prepped,
    Func<int?> ElementalLevel,
    Func<IReadOnlyDictionary<uint, int>> UsefulShards,
    TrackerSession Tracker,
    Action<uint, uint, Vector3> SetFlag);

/// <summary>
/// "Carte" tab: the game map of the Eureka zone, redrawn by the plugin so that what comes from it is
/// obvious at a glance. It shows what the game map shows (fixed points, area names, the player, the
/// party, the flag, running FATEs) and what the plugin knows: NMs with their status, the chosen NM's
/// prep area, chat pops, and on the right "Où aller maintenant". Wheel: zoom around the mouse; drag:
/// pan; right click: flag. User-facing texts are French for now.
/// </summary>
public sealed class MapTab
{
    private const float PanelWidth = 360;
    private const float MaxZoom = 4f;
    private const uint FlagIcon = 60561;
    private const float PrepRadius = 1.3f; // in map units, around each prep place
    private static readonly TimeSpan ComputeInterval = TimeSpan.FromSeconds(1);

    private static readonly Vector4 Yellow = new(1f, 0.85f, 0.25f, 1f);
    private static readonly Vector4 Grey = new(0.6f, 0.6f, 0.65f, 1f);
    private static readonly uint White = Color(1, 1, 1);
    private static readonly uint Black = Color(0, 0, 0);
    private static readonly uint PartyBlue = Color(0.29f, 0.64f, 1f);
    private static readonly uint FateRed = Color(0.77f, 0.23f, 0.29f);
    private static readonly uint PopRed = Color(1f, 0.3f, 0.3f);
    private static readonly uint MeCream = Color(1f, 0.96f, 0.76f);
    private static readonly uint AreaInk = Color(0.33f, 0.25f, 0.13f);
    private static readonly uint AreaInkFaint = Color(0.33f, 0.25f, 0.13f, 0.55f);
    private static readonly uint OpenGreen = Color(0.18f, 0.55f, 0.32f);
    private static readonly uint ComingAmber = Color(0.72f, 0.47f, 0.08f);
    private static readonly uint Parchment = Color(0.94f, 0.89f, 0.73f);
    private static readonly uint NmFill = Color(0.11f, 0.11f, 0.13f);
    private static readonly uint RespawnFill = Color(0.36f, 0.35f, 0.39f);
    private static readonly uint PrepPurple = Color(0.42f, 0.17f, 0.55f);
    private static readonly uint FarmTeal = Color(0.05f, 0.45f, 0.5f);

    private static readonly Dictionary<string, (uint Color, string French, string English)> Elements = new()
    {
        ["fire"] = (Color(0.91f, 0.36f, 0.24f), "Feu", "Fire"),
        ["ice"] = (Color(0.44f, 0.82f, 0.93f), "Glace", "Ice"),
        ["wind"] = (Color(0.37f, 0.81f, 0.48f), "Vent", "Wind"),
        ["earth"] = (Color(0.85f, 0.71f, 0.29f), "Terre", "Earth"),
        ["lightning"] = (Color(0.72f, 0.48f, 0.9f), "Foudre", "Lightning"),
        ["water"] = (Color(0.29f, 0.56f, 0.91f), "Eau", "Water"),
    };

    private static string ElementName(string element) =>
        Elements.TryGetValue(element, out var e) ? Loc.T(e.French, e.English) : element;

    private readonly Configuration configuration;
    private readonly MapReader maps;
    private readonly MapStateReader stateReader;
    private readonly NmCatalog nmCatalog;
    private readonly GameNames names;
    private readonly ITextureProvider textures;
    private readonly MapSources sources;
    private readonly Action save;

    private uint shownMap;
    private float zoom;
    private Vector2 center;
    private bool followMe = true;
    private uint? chosenNm;
    private Vector2? pendingCenter;
    private (string Label, IReadOnlyList<Vector2> Places)? shownFarm;

    private DateTime nextCompute = DateTime.MinValue;
    private IReadOnlyList<NmStatus> statuses = [];
    private IReadOnlyList<Suggestion> suggestions = [];

    public MapTab(Configuration configuration, MapReader maps, MapStateReader stateReader, NmCatalog nmCatalog,
        GameNames names, ITextureProvider textures, MapSources sources, Action save)
    {
        this.configuration = configuration;
        this.maps = maps;
        this.stateReader = stateReader;
        this.nmCatalog = nmCatalog;
        this.names = names;
        this.textures = textures;
        this.sources = sources;
        this.save = save;
    }

    /// <summary>
    /// For the hunt tab: the zone shown (current Eureka zone, else the last one), its NM statuses, the
    /// running bunny FATEs and the elemental level.
    /// </summary>
    public HuntContext HuntContextNow()
    {
        var state = stateReader.Read();
        var inEureka = EurekaWeather.Zone(state.Territory) != null;
        var zone = EurekaWeather.Zone(inEureka ? state.Territory : configuration.LastZone) ?? EurekaWeather.Zones[2];
        if (!inEureka) state = MapState.Empty;
        Compute(zone, state);
        var level = sources.ElementalLevel();
        var party = configuration.CountParty ? Math.Max(configuration.FarmPartySize, state.Party.Count + 1) : 1;
        return new HuntContext(zone.Territory, statuses, state.BunnyFates, level, DateTimeOffset.UtcNow, Math.Clamp(configuration.LevelGap, 0, HuntOptions.MaxGap),
            HuntXp.Weight(configuration.HuntMode == HuntMode.Logos ? 0 : configuration.XpImportance / 100.0, zone.Territory, level), party);
    }

    /// <summary>Chooses the NM and centers the map on it at the next draw.</summary>
    public void ShowNm(EurekaNm nm)
    {
        chosenNm = nm.Npc;
        pendingCenter = nm.Position;
    }

    /// <summary>
    /// Sets the game flag on the place nearest to the player (the first one when not in that zone),
    /// on the zone's main map.
    /// </summary>
    public void FlagAt(uint territory, IReadOnlyList<Vector2> places)
    {
        if (places.Count == 0 || maps.MainMap(territory) is not { } map) return;
        var g = map.Geometry;
        var state = stateReader.Read();
        var place = state.Territory == territory && state.Me is { } me
            ? places.MinBy(p => Vector2.DistanceSquared(p, g.Coordinates(g.Pixel(me))))
            : places[0];
        sources.SetFlag(map.Territory, map.Map, g.World(g.PixelFromCoordinates(place)));
    }

    /// <summary>Highlights a farm's places (sprites, mobs, bunny FATE) and centers the map on the first one.</summary>
    public void ShowFarm(string label, IReadOnlyList<Vector2> places)
    {
        if (places.Count == 0) return;
        shownFarm = (label, places);
        pendingCenter = places[0];
    }

    public void Draw()
    {
        var state = stateReader.Read();
        var inEureka = EurekaWeather.Zone(state.Territory) != null;
        if (inEureka && configuration.LastZone != state.Territory)
        {
            configuration.LastZone = state.Territory;
            save();
        }

        var zone = EurekaWeather.Zone(inEureka ? state.Territory : configuration.LastZone) ?? EurekaWeather.Zones[2];
        var map = inEureka ? maps.Map(state.Map) : maps.MainMap(zone.Territory);
        if (!inEureka) state = MapState.Empty;
        Compute(zone, state);

        DrawHeader(zone, inEureka);
        if (map == null)
        {
            ImGui.TextColored(Grey, Loc.T("Carte introuvable dans les données du jeu.", "Map not found in the game data."));
            return;
        }

        if (pendingCenter is { } c)
        {
            CenterOnCoordinates(map, c);
            pendingCenter = null;
        }

        var height = ImGui.GetContentRegionAvail().Y;
        using (var left = ImRaii.Child("##map", new Vector2(ImGui.GetContentRegionAvail().X - PanelWidth - 8, height), true,
                   ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
            if (left) DrawMap(map, state);
        ImGui.SameLine(0, 8);
        using (var right = ImRaii.Child("##panel", new Vector2(0, height), true))
            if (right) DrawPanel(map, state);
    }

    /// <summary>NM statuses and suggestions, recomputed once a second (not every frame).</summary>
    private void Compute(EurekaZone zone, MapState state)
    {
        if (DateTime.UtcNow < nextCompute) return;
        nextCompute = DateTime.UtcNow + ComputeInterval;
        var now = DateTimeOffset.UtcNow;
        if (nmCatalog.Zone(zone.Territory) is not { } nms)
        {
            statuses = [];
            suggestions = [];
            return;
        }
        statuses = NmStatuses.Compute(zone, nms, state.Fates, sources.EndedFates(), sources.Prepped(), now);
        suggestions = Suggestions.Compute(statuses, now, sources.ElementalLevel(), sources.UsefulShards(), names);
    }

    // ---------- header: zone, time, weather, tracker ----------

    private void DrawHeader(EurekaZone zone, bool inEureka)
    {
        var now = DateTimeOffset.UtcNow;
        if (!inEureka)
        {
            ImGui.SetNextItemWidth(160);
            using (var combo = ImRaii.Combo("##zone", zone.Name))
            {
                if (combo)
                    foreach (var z in EurekaWeather.Zones)
                        if (ImGui.Selectable(z.Name, z == zone))
                        {
                            configuration.LastZone = z.Territory;
                            shownMap = 0;
                            nextCompute = DateTime.MinValue;
                            save();
                        }
            }
            ImGui.SameLine();
            ImGui.TextColored(Grey, Loc.T("(tu n'es pas à Eurêka : carte seule)", "(you are not in Eureka: map only)"));
        }
        else
        {
            ImGui.TextColored(Yellow, zone.Name);
        }

        var (h, m) = EurekaWeather.EorzeaTime(now);
        var (night, at) = EurekaWeather.NextDayNightSwitch(now);
        ImGui.SameLine();
        ImGui.TextUnformatted(Loc.T($"· ET {h:00}:{m:00} · {(night ? "nuit" : "jour")} dans {Formatting.Duration(at - now)}", $"· ET {h:00}:{m:00} · {(night ? "night" : "day")} in {Formatting.Duration(at - now)}"));
        if (sources.ElementalLevel() is { } level)
        {
            ImGui.SameLine();
            ImGui.TextUnformatted(Loc.T($"· niveau élémentaire {level}", $"· elemental level {level}"));
        }

        // Weather: the current one, then the next six, with how long until each.
        var first = true;
        foreach (var p in EurekaWeather.Periods(zone, now).Take(7))
        {
            if (!first) ImGui.SameLine(0, 14);
            var name = names.Weather(p.Weather);
            if (first) ImGui.TextColored(Yellow, Loc.T($"{name} (encore {Formatting.Duration(p.End - now)})", $"{name} ({Formatting.Duration(p.End - now)} left)"));
            else ImGui.TextUnformatted($"{name} {Formatting.Duration(p.Start - now)}");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{p.Start.ToLocalTime():HH:mm} → {p.End.ToLocalTime():HH:mm} " + Loc.T("(heure locale)", "(local time)"));
            first = false;
        }
        if (inEureka) DrawTracker();
        ImGui.Spacing();
    }

    /// <summary>Tracker line: which one, its status, and the useful actions (copy, open, create, switch).</summary>
    private void DrawTracker()
    {
        var t = sources.Tracker;
        ImGui.TextColored(t.Current != null ? Yellow : Grey, Loc.T("Tracker :", "Tracker:"));
        ImGui.SameLine();
        ImGui.TextUnformatted(t.Status);
        if (t.FollowedId is { } id)
        {
            var link = $"https://ffxiv-eureka.com/{id}";
            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.T("Copier le lien", "Copy link") + "##copy")) ImGui.SetClipboardText(link);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(Loc.T("À crier dans le chat pour que les autres le suivent.", "Shout it in chat so others follow it."));
            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.T("Ouvrir", "Open") + "##open")) Dalamud.Utility.Util.OpenLink(link);
        }
        else
        {
            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.T("Créer un tracker", "Create a tracker") + "##create")) t.CreateNow();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(Loc.T("Crée un tracker ffxiv-eureka.com pour cette instance ; le lien s'affiche dans le chat, à crier.", "Creates a ffxiv-eureka.com tracker for this instance; the link shows in chat, to shout."));
        }

        var others = t.OtherTrackers;
        if (others.Count == 0) return;
        ImGui.SameLine();
        ImGui.SetNextItemWidth(220);
        using var combo = ImRaii.Combo("##other-trackers", $"{others.Count} autre(s) tracker(s) de la zone");
        if (!combo) return;
        foreach (var other in others)
        {
            var updated = other.UpdatedAt is { } u
                ? Loc.T($"mis à jour il y a {Formatting.Duration(DateTimeOffset.UtcNow - u)}", $"updated {Formatting.Duration(DateTimeOffset.UtcNow - u)} ago")
                : Loc.T("jamais mis à jour", "never updated");
            if (ImGui.Selectable($"{other.Id} · " + Loc.T($"{other.Kills.Count} NM tué(s)", $"{other.Kills.Count} NM(s) killed") + $" · {updated}")) t.Pick(other.Id);
        }
    }

    // ---------- the map ----------

    private void DrawMap(GameMap map, MapState state)
    {
        var origin = ImGui.GetCursorScreenPos();
        var size = ImGui.GetContentRegionAvail();
        var fit = Math.Min(size.X, size.Y) / MapGeometry.TextureSize;
        if (shownMap != map.Map)
        {
            shownMap = map.Map;
            zoom = fit;
            center = new Vector2(MapGeometry.TextureSize / 2);
        }

        ImGui.InvisibleButton("##map-area", size);
        var hovered = ImGui.IsItemHovered();
        var mouse = ImGui.GetIO().MousePos;
        Vector2 Screen(Vector2 pixel) => origin + (size / 2) + ((pixel - center) * zoom);
        Vector2 PixelUnder(Vector2 screen) => center + ((screen - origin - (size / 2)) / zoom);

        if (hovered && ImGui.GetIO().MouseWheel != 0)
        {
            var before = PixelUnder(mouse);
            zoom = Math.Clamp(zoom * (ImGui.GetIO().MouseWheel > 0 ? 1.2f : 1 / 1.2f), fit * 0.8f, MaxZoom);
            center += before - PixelUnder(mouse);
            followMe = false;
        }
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            center -= ImGui.GetIO().MouseDelta / zoom;
            followMe = false;
        }
        if (followMe && state.Me is { } here) center = map.Geometry.Pixel(here);

        var draw = ImGui.GetWindowDrawList();
        var background = textures.GetFromGame(map.Texture).GetWrapOrEmpty();
        draw.AddImage(background.Handle, Screen(Vector2.Zero), Screen(new Vector2(MapGeometry.TextureSize)));

        var layers = configuration.Layers;
        var g = map.Geometry;
        var scale = Math.Clamp(zoom / fit, 1f, 1.6f);
        var now = DateTimeOffset.UtcNow;
        var nmRadius = 13 * scale;
        var labels = new LabelPlacer();
        // NMs that cannot be done now are hidden (unless asked for, or chosen): no point in the time left.
        var shown = statuses.Where(s => layers.UnavailableNms || s.IsDoable(now) || s.Nm.Npc == chosenNm).ToList();

        // Obstacles first, so that labels go around the markers rather than over them.
        if (layers.Nms)
            foreach (var s in shown)
                labels.AddObstacle(ScreenRect.Centered(Screen(g.PixelFromCoordinates(s.Nm.Position)), new Vector2((nmRadius + 3) * 2)));
        if (state.Me is { } meObstacle) labels.AddObstacle(ScreenRect.Centered(Screen(g.Pixel(meObstacle)), new Vector2(28)));

        if (layers.Points)
            foreach (var p in map.Points.Where(p => !p.IsAreaName))
            {
                var s = Screen(p.Pixel);
                Icon(draw, p.Icon, s, 22 * scale);
                labels.AddObstacle(ScreenRect.Centered(s, new Vector2(22 * scale)));
                if (p.Name != null && Near(mouse, s, 12 * scale)) ImGui.SetTooltip(p.Name);
            }

        var chosen = statuses.FirstOrDefault(s => s.Nm.Npc == chosenNm);
        if (layers.Prep && chosen != null)
            foreach (var place in chosen.Nm.Prep.Places)
            {
                var s = Screen(g.PixelFromCoordinates(place));
                var radius = PrepRadius * 50 * g.Scale / 100f * zoom;
                draw.AddCircleFilled(s, radius, (PrepPurple & 0x00FFFFFF) | 0x38000000);
                draw.AddCircle(s, radius, PrepPurple, 0, 2);
                var text = Loc.T("prépa : ", "prep: ") + names.Npc(chosen.Nm.Prep.Npc);
                var rect = labels.PlaceAround(s, radius, ImGui.CalcTextSize(text));
                OutlinedText(draw, rect.Min, text, PrepPurple, Parchment);
            }

        if (shownFarm is { } farm)
            foreach (var place in farm.Places)
            {
                var s = Screen(g.PixelFromCoordinates(place));
                var radius = PrepRadius * 50 * g.Scale / 100f * zoom;
                draw.AddCircleFilled(s, radius, (FarmTeal & 0x00FFFFFF) | 0x38000000);
                draw.AddCircle(s, radius, FarmTeal, 0, 2);
                var rect = labels.PlaceAround(s, radius, ImGui.CalcTextSize(farm.Label));
                OutlinedText(draw, rect.Min, farm.Label, FarmTeal, Parchment);
            }

        if (layers.Fates)
            foreach (var f in state.Fates.Where(f => statuses.All(s => s.Nm.Fate != f.Id)))
            {
                // FATE without an NM entry (zone without data): drawn as on the game map.
                var s = Screen(g.Pixel(f.Position));
                Icon(draw, f.Icon, s, 26 * scale);
                var text = FateLabel(f);
                var rect = labels.PlaceAround(s, 13 * scale, ImGui.CalcTextSize(text));
                OutlinedText(draw, rect.Min, text, White, Black);
            }

        if (layers.Nms)
        {
            foreach (var s in shown) DrawNmMarker(draw, s, Screen(g.PixelFromCoordinates(s.Nm.Position)), now, nmRadius);
            // The chosen NM's label is placed first: it gets the best spot.
            foreach (var s in shown.OrderByDescending(s => s.Nm.Npc == chosenNm))
                DrawNmLabel(draw, labels, s, Screen(g.PixelFromCoordinates(s.Nm.Position)), now, nmRadius);
        }

        // Area names last and only where there is room: NM labels matter more.
        if (layers.AreaNames)
            foreach (var p in map.Points.Where(p => p.IsAreaName))
            {
                var rect = ScreenRect.Centered(Screen(p.Pixel), ImGui.CalcTextSize(p.Name!));
                if (labels.TryPlace(rect)) draw.AddText(rect.Min, AreaInkFaint, p.Name!);
            }

        if (layers.Pops)
            foreach (var pop in state.Pops)
            {
                var s = Screen(g.Pixel(pop.Position));
                var pulse = 10 + (4 * MathF.Sin((float)ImGui.GetTime() * 4));
                draw.AddCircle(s, pulse, PopRed, 0, 3);
                var text = $"{pop.Title} · " + Loc.T($"il y a {Formatting.Duration(DateTime.UtcNow - pop.When)}", $"{Formatting.Duration(DateTime.UtcNow - pop.When)} ago");
                var rect = labels.PlaceAround(s, 14, ImGui.CalcTextSize(text));
                OutlinedText(draw, rect.Min, text, White, Black);
            }

        if (state.Flag is { } flag) FlagPin(draw, Screen(g.Pixel(flag)), 22 * scale);

        if (layers.Party)
            foreach (var member in state.Party)
            {
                var s = Screen(g.Pixel(member));
                draw.AddCircleFilled(s, 5, PartyBlue);
                draw.AddCircle(s, 5, Black, 0, 1.5f);
            }

        if (state.Me is { } me) MeArrow(draw, Screen(g.Pixel(me)), state.Rotation);

        if (hovered)
        {
            var coordinates = g.Coordinates(PixelUnder(mouse));
            var text = $"X {coordinates.X:0.0} · Y {coordinates.Y:0.0}";
            draw.AddRectFilled(origin + new Vector2(6, size.Y - 26), origin + new Vector2(16 + ImGui.CalcTextSize(text).X, size.Y - 6), 0xD0282020, 4);
            draw.AddText(origin + new Vector2(11, size.Y - 24), White, text);
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right)) ImGui.OpenPopup("##map-menu");
            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) && ImGui.GetIO().MouseDragMaxDistanceSqr[0] < 16)
                chosenNm = shown.FirstOrDefault(s => Near(mouse, Screen(g.PixelFromCoordinates(s.Nm.Position)), nmRadius + 3))?.Nm.Npc ?? chosenNm;
            if (layers.Nms && shown.FirstOrDefault(s => Near(mouse, Screen(g.PixelFromCoordinates(s.Nm.Position)), nmRadius + 2)) is { } under)
            {
                using var tooltip = ImRaii.Tooltip();
                DrawNmSheet(under, now, buttons: false);
            }
        }

        using (var menu = ImRaii.Popup("##map-menu"))
        {
            if (menu)
            {
                if (ImGui.MenuItem(Loc.T("Poser le drapeau du jeu ici", "Set the game flag here")))
                    sources.SetFlag(map.Territory, map.Map, g.World(PixelUnder(ImGui.GetMousePosOnOpeningCurrentPopup())));
                if (ImGui.MenuItem(Loc.T("Recentrer sur moi", "Center on me"))) followMe = true;
            }
        }

        ImGui.SetCursorScreenPos(origin + new Vector2(size.X - 120, 6));
        if (ImGui.SmallButton((followMe ? Loc.T("Suit ma position", "Following me") : Loc.T("Centrer sur moi", "Center on me")) + "##follow")) followMe = true;
    }

    /// <summary>The NM's disc: fill = state (FATE, respawning, idle), ring = element, level inside; cream halo when chosen.</summary>
    private void DrawNmMarker(ImDrawListPtr draw, NmStatus s, Vector2 p, DateTimeOffset now, float radius)
    {
        var element = Elements.GetValueOrDefault(s.Nm.Element, (Color: White, French: s.Nm.Element, English: s.Nm.Element));
        var fill = s.Fate != null ? FateRed : s.IsRespawning(now) ? RespawnFill : NmFill;
        if (s.Fate != null) draw.AddCircleFilled(p, radius + 6, (FateRed & 0x00FFFFFF) | 0x60000000);
        if (s.Nm.Npc == chosenNm) draw.AddCircle(p, radius + 4, MeCream, 0, 3);
        draw.AddCircleFilled(p, radius, fill);
        draw.AddCircle(p, radius, element.Color, 0, 3);
        var level = s.Nm.Level.ToString();
        draw.AddText(p - (ImGui.CalcTextSize(level) / 2), White, level);
    }

    /// <summary>Name, and below it the state as a colored pill, placed where it overlaps nothing.</summary>
    private void DrawNmLabel(ImDrawListPtr draw, LabelPlacer labels, NmStatus s, Vector2 p, DateTimeOffset now, float radius)
    {
        var name = names.Npc(s.Nm.Npc);
        var nameSize = ImGui.CalcTextSize(name);
        var badge = Badge(s, now);
        var badgeSize = badge is { } b ? ImGui.CalcTextSize(b.Text) + new Vector2(8, 2) : Vector2.Zero;
        var block = new Vector2(Math.Max(nameSize.X, badgeSize.X), nameSize.Y + (badge != null ? badgeSize.Y + 2 : 0));
        var rect = labels.PlaceAround(p, radius + 2, block);

        var middle = (rect.Min.X + rect.Max.X) / 2;
        OutlinedText(draw, new Vector2(middle - (nameSize.X / 2), rect.Min.Y), name, s.Nm.Npc == chosenNm ? Black : AreaInk, Parchment);
        if (badge is not { } pill) return;
        var pillMin = new Vector2(middle - (badgeSize.X / 2), rect.Min.Y + nameSize.Y + 2);
        draw.AddRectFilled(pillMin, pillMin + badgeSize, pill.Color, 4);
        draw.AddText(pillMin + new Vector2(4, 1), White, pill.Text);
    }

    /// <summary>The state under the NM's name, with its pill color: FATE, respawn, prepped, or condition open / to come.</summary>
    private (string Text, uint Color)? Badge(NmStatus s, DateTimeOffset now)
    {
        if (s.Fate is { } f) return (FateLabel(f), FateRed);
        if (s.IsRespawning(now)) return ($"respawn ≥ {Formatting.Duration(s.RespawnNotBefore!.Value - now)}", RespawnFill);
        if (s.Prepped) return (Loc.T("préparé", "prepped"), PrepPurple);
        if (s.Wait is not { } window) return null;
        var condition = s.NmWindow != null ? s.Nm.Condition : s.Nm.Prep.Condition;
        var name = condition.Weather is { } w ? names.Weather(w) : Loc.T("nuit", "night");
        return window.IsOpen(now)
            ? (Loc.T($"{name} en cours", $"{name} now"), OpenGreen)
            : (Loc.T($"{name} dans", $"{name} in") + $" {Formatting.Duration(window.Start - now)}", ComingAmber);
    }

    private static string FateLabel(MapFate f) =>
        f.Preparing ? Loc.T("FATE en préparation", "FATE preparing") : $"FATE {f.Progress} % · {Formatting.Duration(f.Remaining)}";

    private static void MeArrow(ImDrawListPtr draw, Vector2 p, float rotation)
    {
        // Rotation 0 = facing world south (+Z), so towards the bottom of the map.
        var forward = new Vector2(MathF.Sin(rotation), MathF.Cos(rotation));
        var side = new Vector2(-forward.Y, forward.X);
        var tip = p + (forward * 13);
        var left = p - (forward * 8) + (side * 8);
        var right = p - (forward * 8) - (side * 8);
        draw.AddTriangleFilled(tip, left, p - (forward * 3), MeCream);
        draw.AddTriangleFilled(tip, p - (forward * 3), right, MeCream);
        draw.AddTriangle(tip, left, right, Black, 2);
    }

    // ---------- right panel ----------

    private void DrawPanel(GameMap map, MapState state)
    {
        var now = DateTimeOffset.UtcNow;
        ImGui.TextColored(Yellow, Loc.T("Où aller maintenant", "Where to go now"));
        if (statuses.Count == 0) ImGui.TextColored(Grey, Loc.T("Pas encore de données de NM pour cette zone.", "No NM data for this zone yet."));
        else if (suggestions.Count == 0) ImGui.TextColored(Grey, Loc.T("Rien de pressé : aucune FATE, aucune condition dans la demi-heure.", "Nothing urgent: no FATE, no condition within half an hour."));
        foreach (var (s, i) in suggestions.Select((s, i) => (s, i)))
        {
            using var id = ImRaii.PushId(i);
            ImGui.TextColored(Yellow, $"{i + 1}.");
            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.PushTextWrapPos(0);
            ImGui.TextUnformatted(s.Title);
            ImGui.TextColored(Grey, s.Text);
            ImGui.PopTextWrapPos();
            if (ImGui.SmallButton(Loc.T("Voir", "Show") + "##show"))
            {
                CenterOnCoordinates(map, s.Coordinates);
                chosenNm = s.Npc ?? chosenNm;
            }
            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.T("Drapeau", "Flag") + "##flag"))
                sources.SetFlag(map.Territory, map.Map, map.Geometry.World(map.Geometry.PixelFromCoordinates(s.Coordinates)));
            ImGui.EndGroup();
            ImGui.Spacing();
        }

        if (shownFarm is { } farm)
        {
            ImGui.Separator();
            ImGui.TextColored(Yellow, Loc.T("Farm montré", "Farm shown"));
            ImGui.SameLine();
            ImGui.TextUnformatted(farm.Label);
            if (ImGui.SmallButton(Loc.T("Centrer", "Center") + "##farm")) CenterOnCoordinates(map, farm.Places[0]);
            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.T("Drapeau", "Flag") + "##farmflag"))
                sources.SetFlag(map.Territory, map.Map, map.Geometry.World(map.Geometry.PixelFromCoordinates(farm.Places[0])));
            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.T("Effacer", "Clear") + "##farmclear")) shownFarm = null;
        }

        if (statuses.FirstOrDefault(s => s.Nm.Npc == chosenNm) is { } chosen)
        {
            ImGui.Separator();
            DrawNmSheet(chosen, now, buttons: true, map);
        }

        if (state.Pops.Count > 0)
        {
            ImGui.Separator();
            ImGui.TextColored(Yellow, Loc.T("Pops du chat", "Chat pops"));
            foreach (var (pop, i) in state.Pops.Select((p, i) => (p, i)).Reverse())
            {
                if (ImGui.Selectable($"{pop.Title}##pop{i}")) CenterOn(map, pop.Position);
                ImGui.SameLine();
                ImGui.TextColored(Grey, Loc.T($"il y a {Formatting.Duration(DateTime.UtcNow - pop.When)}", $"{Formatting.Duration(DateTime.UtcNow - pop.When)} ago"));
            }
        }

        ImGui.Separator();
        ImGui.TextColored(Grey, Loc.T("Calques", "Layers"));
        var k = configuration.Layers;
        using (var table = ImRaii.Table("##layers", 2))
        {
            if (table)
            {
                ImGui.TableNextColumn(); Checkbox("NM##nms", () => k.Nms, v => k.Nms = v);
                ImGui.TableNextColumn(); Checkbox(Loc.T("Zone de prépa", "Prep area") + "##prep", () => k.Prep, v => k.Prep = v);
                ImGui.TableNextColumn(); Checkbox(Loc.T("Pops du chat", "Chat pops") + "##pops", () => k.Pops, v => k.Pops = v);
                ImGui.TableNextColumn(); Checkbox(Loc.T("Groupe", "Party") + "##party", () => k.Party, v => k.Party = v);
                ImGui.TableNextColumn(); Checkbox(Loc.T("Repères du jeu", "Game markers") + "##points", () => k.Points, v => k.Points = v);
                ImGui.TableNextColumn(); Checkbox(Loc.T("Noms de zone", "Area names") + "##areas", () => k.AreaNames, v => k.AreaNames = v);
                ImGui.TableNextColumn(); Checkbox(Loc.T("Autres FATE", "Other FATEs") + "##fates", () => k.Fates, v => k.Fates = v);
                ImGui.TableNextColumn(); Checkbox(Loc.T("NM pas faisables", "Unavailable NMs") + "##unavailable", () => k.UnavailableNms, v => k.UnavailableNms = v);
            }
        }
        ImGui.TextColored(Grey, Loc.T("Molette : zoom · glisser : déplacer · clic : fiche du NM · clic droit : drapeau.", "Wheel: zoom · drag: pan · click: NM sheet · right click: flag."));
    }

    private void DrawNmSheet(NmStatus s, DateTimeOffset now, bool buttons, GameMap? map = null)
    {
        var nm = s.Nm;
        var element = ElementName(nm.Element);
        ImGui.TextColored(Yellow, names.Npc(nm.Npc));
        ImGui.SameLine();
        ImGui.TextColored(Grey, $"{Loc.T("Nv", "lv")} {nm.Level} · {element} · {Formatting.Coordinates(nm.Position)}");
        ImGui.PushTextWrapPos(buttons ? 0 : 360);
        ImGui.TextUnformatted(Suggestions.PrepText(nm, names));
        ImGui.TextUnformatted(Loc.T("Condition : ", "Condition: ") + ConditionText(s, now));
        var drops = new[] { nm.Shard }.Where(s => s != 0).Concat(nm.Items).Select(i => GameTexts.WithoutUnidentified(names.Item(i)));
        ImGui.TextUnformatted(Loc.T("Lâche : ", "Drops: ") + (drops.Any() ? string.Join(", ", drops) : Loc.T("rien de notable", "nothing notable")));
        ImGui.TextUnformatted(Loc.T("État : ", "State: ") + (Badge(s, now) is { } badge ? badge.Text
            : s.RespawnNotBefore != null ? Loc.T("réapparu (2 h passées)", "back (2 h passed)")
            : Loc.T("inconnu (pas vu tué dans cette instance)", "unknown (not seen killed in this instance)")));
        ImGui.PopTextWrapPos();

        if (!buttons || map == null) return;
        if (ImGui.SmallButton(Loc.T("Centrer", "Center") + "##nmcenter")) CenterOnCoordinates(map, nm.Position);
        ImGui.SameLine();
        if (ImGui.SmallButton(Loc.T("Drapeau sur la FATE", "Flag on the FATE") + "##nmflag"))
            sources.SetFlag(map.Territory, map.Map, map.Geometry.World(map.Geometry.PixelFromCoordinates(nm.Position)));
        if (nm.Prep.Places.Count > 0)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.T("Drapeau sur la prépa", "Flag on the prep") + "##prepflag"))
                sources.SetFlag(map.Territory, map.Map, map.Geometry.World(map.Geometry.PixelFromCoordinates(nm.Prep.Places[0])));
        }
    }

    private string ConditionText(NmStatus s, DateTimeOffset now)
    {
        string Window(SpawnCondition c, TimeWindow? w, string who)
        {
            var name = c.Weather is { } weather ? names.Weather(weather) : Loc.T("la nuit", "at night");
            return w == null ? Loc.T($"{who} : {name}", $"{who} {name}")
                : w.IsOpen(now) ? Loc.T($"{who} : {name}, en cours (encore {Formatting.Duration(w.End - now)})", $"{who} {name}, now ({Formatting.Duration(w.End - now)} left)")
                : Loc.T($"{who} : {name}, dans", $"{who} {name}, in") + $" {Formatting.Duration(w.Start - now)}";
        }
        if (!s.Nm.Condition.None) return Window(s.Nm.Condition, s.NmWindow, Loc.T("le NM n'apparaît que par", "the NM only spawns in"));
        if (!s.Nm.Prep.Condition.None)
            return Window(s.Nm.Prep.Condition, s.PrepWindow, Loc.T("le mob de prépa n'apparaît que par", "the prep mob only spawns in"));
        return Loc.T("aucune", "none");
    }

    private void CenterOn(GameMap map, Vector3 world)
    {
        center = map.Geometry.Pixel(world);
        followMe = false;
    }

    private void CenterOnCoordinates(GameMap map, Vector2 coordinates)
    {
        center = map.Geometry.PixelFromCoordinates(coordinates);
        followMe = false;
    }

    private void Checkbox(string label, Func<bool> read, Action<bool> write)
    {
        var value = read();
        if (!ImGui.Checkbox(label, ref value)) return;
        write(value);
        save();
    }

    // ---------- drawing helpers ----------

    private void Icon(ImDrawListPtr draw, uint icon, Vector2 iconCenter, float size)
    {
        if (IconTexture(icon) is not { } image) return;
        var half = new Vector2(size / 2);
        draw.AddImage(image, iconCenter - half, iconCenter + half);
    }

    private ImTextureID? IconTexture(uint icon) =>
        icon != 0 && textures.TryGetFromGameIcon(new GameIconLookup(icon), out var shared) && shared.TryGetWrap(out var image, out _)
            ? image.Handle : null;

    private static void OutlinedText(ImDrawListPtr draw, Vector2 topLeft, string text, uint color, uint outline)
    {
        foreach (var d in new[] { new Vector2(-1, 0), new Vector2(1, 0), new Vector2(0, -1), new Vector2(0, 1) })
            draw.AddText(topLeft + d, outline, text);
        draw.AddText(topLeft, color, text);
    }

    /// <summary>The game flag as a pin standing on its point, so it never covers what is there.</summary>
    private void FlagPin(ImDrawListPtr draw, Vector2 point, float size)
    {
        draw.AddCircleFilled(point, 3, Black);
        if (IconTexture(FlagIcon) is not { } image) return;
        draw.AddImage(image, point + new Vector2(-size * 0.2f, -size), point + new Vector2(size * 0.8f, 0));
    }

    private static bool Near(Vector2 a, Vector2 b, float radius) => Vector2.DistanceSquared(a, b) <= radius * radius;

    /// <summary>ImGui color (ABGR), computed without ImGui so it can be used outside a frame.</summary>
    private static uint Color(float r, float g, float b, float a = 1f) =>
        ((uint)(a * 255) << 24) | ((uint)(b * 255) << 16) | ((uint)(g * 255) << 8) | (uint)(r * 255);
}

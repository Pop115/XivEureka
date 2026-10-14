using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace XivEureka.Zones;

/// <summary>Une FATE en cours vue par la carte : où, combien de fait, combien de temps encore.</summary>
public sealed record FateCarte(ushort Id, string Nom, Vector3 Position, float Rayon, byte Progres, TimeSpan Restant,
    uint Icone, bool EnPreparation);

/// <summary>Un pop annoncé dans le chat, gardé quelques minutes sur la carte.</summary>
public sealed record PopAnnonce(string Titre, Vector3 Position, DateTime Quand);

/// <summary>
/// Ce que la carte du plugin montre de la partie à un instant donné : la carte où l'on est, soi
/// (position et orientation), le groupe, le drapeau du jeu, les FATE en cours et les pops du chat.
/// </summary>
public sealed record EtatCarte(uint Map, uint Territoire, Vector3? Moi, float Orientation,
    IReadOnlyList<Vector3> Groupe, Vector3? Drapeau, IReadOnlyList<FateCarte> Fates, IReadOnlyList<PopAnnonce> Pops)
{
    public static readonly EtatCarte Vide = new(0, 0, null, 0, [], null, [], []);
}

/// <summary>Lit l'<see cref="EtatCarte"/> dans les services de Dalamud et l'agent de la carte du jeu.</summary>
public sealed class LecteurEtatCarte(IClientState clientState, IObjectTable objets, IPartyList groupe, IFateTable fates,
    Func<IReadOnlyList<PopAnnonce>> pops)
{
    public EtatCarte Lire()
    {
        if (!clientState.IsLoggedIn) return EtatCarte.Vide;
        var territoire = clientState.TerritoryType;
        var map = clientState.MapId;
        var moi = objets.LocalPlayer;

        var membres = groupe
            .Where(m => m.Territory.RowId == territoire && (moi == null || m.EntityId != moi.EntityId))
            .Select(m => m.Position)
            .ToList();

        var enCours = fates
            .Where(f => f.State is FateState.Preparing or FateState.Running)
            .Where(f => !SuiviFates.EstFateLapin(f.GameData.Value.Rule))
            .Select(f => new FateCarte(f.FateId, f.Name.TextValue, f.Position, f.Radius, f.Progress,
                TimeSpan.FromSeconds(Math.Max(0, f.TimeRemaining)), f.MapIconId, f.State == FateState.Preparing))
            .ToList();

        return new EtatCarte(map, territoire, moi?.Position, moi?.Rotation ?? 0, membres, Drapeau(map), enCours, pops());
    }

    /// <summary>Le drapeau posé sur la carte du jeu, s'il est sur cette carte (X et Z monde).</summary>
    private static unsafe Vector3? Drapeau(uint map)
    {
        var agent = AgentMap.Instance();
        if (agent == null || agent->FlagMarkerCount == 0) return null;
        var drapeau = agent->FlagMapMarkers[0];
        return drapeau.MapId == map ? new Vector3(drapeau.XFloat, 0, drapeau.YFloat) : null;
    }
}

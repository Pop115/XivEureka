using System.Numerics;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace XivEureka.Zones;

/// <summary>
/// Tout ce qui est position passe par la carte du jeu : drapeau, ouverture de la carte, lien de
/// carte dans le chat. Recette de KangasZ/EurekaHelper (Apache-2.0) : AgentMap.SetFlagMapMarker
/// attend des coordonnées monde (X et Z), pas les coordonnées affichées sur la carte.
/// </summary>
public static unsafe class CarteJeu
{
    /// <summary>Pose le drapeau à une position monde ; ouvre la carte si demandé.</summary>
    public static void Drapeau(uint territoire, uint carte, Vector3 monde, bool ouvrir)
    {
        var agent = AgentMap.Instance();
        if (agent == null) return;
        agent->FlagMarkerCount = 0;
        agent->SetFlagMapMarker(territoire, carte, monde.X, monde.Z);
        if (ouvrir) agent->OpenMap(carte, territoire, null, MapType.FlagMarker);
    }

    /// <summary>Position monde (Y = 0) d'un lien de carte.</summary>
    public static Vector3 Monde(MapLinkPayload lien) => new(lien.RawX / 1000f, 0, lien.RawY / 1000f);

    /// <summary>Position monde de coordonnées de carte (« X: 12.3 Y: 14.5 »).</summary>
    public static Vector3 Monde(uint territoire, uint carte, Vector2 coordonnees) =>
        Monde(new MapLinkPayload(territoire, carte, coordonnees.X, coordonnees.Y));

    /// <summary>Lien de carte cliquable pour le chat, à une position monde.</summary>
    public static SeString Lien(uint territoire, uint carte, Vector3 monde) =>
        SeString.CreateMapLink(territoire, carte, (int)(monde.X * 1000), (int)(monde.Z * 1000));
}

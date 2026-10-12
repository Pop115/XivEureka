# Plan Xiv Eurêka (2026-10-11)

Principes demandés : **rien à taper** (tout s'ouvre ou se déclenche depuis l'UI du jeu), **aspect
proche de l'UI du jeu**, **la carte du jeu** pour tout ce qui est position (pas de carte du plugin).
Penser **Bozja** dès la conception (mêmes besoins : fragments ↔ éclats, CE ↔ FATE, actions de
fortune ↔ actions Logos).

## État (2026-10-12)
- 0 fait : recherche dans le Google Doc « XivEureka – recherche étape 0 (2026-10-12) »
  (protocole du tracker, données NM/logogrammes, ClientStructs, KamiToolKit, Bozja).
- 1 en partie : barre d'infos serveur cliquable, toasts + chat avec lien de carte. Reste : bouton
  natif sur le manipulateur / la carte (KamiToolKit `AddonController`), style « jeu » de la fenêtre.
- 3 + 4 faits (pas encore testés en jeu) : `Zones/CompagnonZone.cs` (FATE, chat, barre),
  `Zones/Meteo.cs` (météo et heure d'Eorzéa locales), `Zones/AnalyseChat.cs`, `Zones/CarteJeu.cs`.
- Suivant : 2 (conseils de farm : données `acquired-by` et NM du site, niveau élémentaire via
  `PlayerState.GetContentValue(2)`, conditions météo/nuit des NM avec `Meteo.Prochaine`).

## 0. À vérifier avant de coder (outils existants)
- Plugins Dalamud Eurêka/Bozja à lire (pas copier) : un « Eureka helper »/auto-pop qui parle au
  tracker, **BozjaBuddy** (Bozja : fragments, actions de fortune, carte), Logogram Helper (fait).
  Noter ce qu'ils lisent et comment (chat, FATE, carte), réutiliser les idées.
- **API du tracker ffxiv-eureka.com** : le site utilise un websocket (`wss://ffxiv-eureka.com/socket`,
  vu dans son code) ; trouver le protocole (canal par instance, ex. `bZFR-s`) pour lire les pops et
  les respawns en direct. Les données NM (niveau, éclats lâchés, position) sont dans son JS.
- **KamiToolKit** (bibliothèque Dalamud pour faire des fenêtres/boutons en nœuds natifs du jeu) :
  voir si elle est utilisable pour l'aspect « UI du jeu » ; sinon ImGui stylé comme le jeu.
- ClientStructs : `AgentMap` (drapeau `SetFlagMapMarker`, marqueurs temporaires, ouvrir la carte sur
  une position), `IFateTable` (FATE en cours), `EurekaState` (niveau élémentaire), `IChatGui`.

## 1. Intégration au jeu (avant les nouvelles fonctions)
- Ouverture sans commande : déjà avec le manipulateur ; ajouter une entrée dans la **barre d'infos
  serveur** (DTR) « Logos : 3 à apprendre » cliquable, et un bouton posé sur la fenêtre du
  manipulateur et sur la carte d'Eurêka (nœud natif si KamiToolKit le permet).
- Notifications façon jeu : toasts du jeu (`IToastGui`) et message dans le chat avec lien carte.
- Style de la fenêtre Logos : couleurs/cadres proches du jeu.

## 2. Conseils « quoi taper pour mes éclats manquants »
- Données : pour chaque éclat (9), où il tombe — élémentaires (sprites), coffres lapin
  bronze/argent/or, NM précis, chaînes de 30, « heatbox » (à identifier) — dans le JS du site
  (`logogram.acquired-by`, `notorious-monster` avec niveau et zone). Les extraire en données comme
  pour les recettes.
- Calcul : actions pas apprises → mnemes manquantes (recette la plus proche) → éclats qui les donnent
  → sources ; pondérer par la probabilité (une mneme parmi N de l'éclat).
- Filtre par **niveau élémentaire** (lu dans `EurekaState`/PlayerState) et par zone courante :
  « À ton niveau 38 à Pyros : farm les élémentaires (éclat d'apprenti), NM X à Y ».
- Affichage : panneau « Où farmer » dans la fenêtre Logos ; un clic pose le drapeau sur la carte du jeu.

## 3. Chat : positions « pop »
- Écouter `IChatGui.ChatMessage` (canaux cris/zone/linkshells/CWLS), en zone Eurêka seulement.
- Repérer : liens de carte (`MapLinkPayload`), coordonnées « (x, y) » en texte, mots « pop »,
  nom de NM ou de FATE. Associer la position à la FATE la plus proche (`IFateTable`, positions
  connues des FATE de NM).
- Réaction : toast + drapeau sur la carte du jeu, option « ouvrir la carte ».

## 4. FATE et carte
- Surveiller `IFateTable` : nouvelle FATE de NM → alerte (sonore optionnelle), drapeau/marqueur sur
  la carte du jeu, temps restant.
- Marqueurs sur la carte du jeu via `AgentMap` (marqueurs temporaires) : NM, FATE en cours, sources
  d'éclats utiles. Aucune carte propre au plugin.

## 5. Tracker en jeu
- Coller ou cliquer un lien `ffxiv-eureka.com/xxxx` dans le chat → le plugin le détecte (chat) et
  propose de le suivre.
- Fenêtre « Tracker » : NM de la zone, statut (up/pop/respawn dans N min), heure d'Eorzéa et météo
  (calcul local), alertes avant respawn. Données en direct via le websocket du site si possible ;
  sinon lien seulement. Optionnel plus tard : signaler un pop au tracker.

## 6. Bozja (plus tard)
- Même squelette : stock de fragments (fenêtre du jeu), actions de fortune, conseils de farm par
  zone/CE, alertes de CE (équivalent FATE), carte du jeu. Réutiliser lecteurs chat/FATE/carte.
- Regarder BozjaBuddy avant pour ne pas refaire ce qu'il fait bien.

## Ordre proposé
1 (intégration, petit) → 3+4 (chat + FATE + carte, utile tout de suite en jeu) → 2 (conseils de farm)
→ 5 (tracker) → 6 (Bozja). Chaque étape : relevé en jeu d'abord si une donnée du jeu est en jeu,
tests sur relevés réels, commit.

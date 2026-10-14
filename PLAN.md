# Plan Xiv Eurêka (2026-10-11)

Principes demandés : **rien à taper** (tout s'ouvre ou se déclenche depuis l'UI du jeu), **aspect
proche de l'UI du jeu**. Positions : **carte propre au plugin** (onglet « Carte », décision du
2026-10-14 : sur la carte du jeu on ne distingue pas ce qui vient du plugin) ; le drapeau du jeu
reste pour les alertes.
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

## Onglet « Carte » (2026-10-14, à valider avec Pop)

La fenêtre du plugin devient « Xiv Eurêka » avec deux onglets : **Actions Logos** | **Carte**.
L'onglet Carte montre la zone d'Eurêka où l'on est (ou celle choisie hors d'Eurêka).

```
┌ Xiv Eurêka ─────────────────────────────────────────────────────────────────┐
│ [Actions Logos] [Carte]                                                     │
│ Pyros · ET 17:01 · Vagues de chaleur → Orage dans 12 min → Blizzard → …     │
├──────────────────────────────────────────────────┬──────────────────────────┤
│ (texture de la carte du jeu, molette = zoom,     │ OÙ ALLER MAINTENANT      │
│  glisser = déplacer)                             │ ① Skoll Nv50 ❄ blizzard   │
│        ◆Penthesilea 50                           │   dans 20 min : prépa    │
│   ◆Eldthurs 43   ⚑                               │   Pyros Shuck (14,33),   │
│      ◆Lamebrix 45  ◆Dux 46 ⚡43min               │   loin, partir tôt  [→]  │
│  ◆Lesath 42   ◆Iris 44  ✦FATE 62% 18min          │ ② FATE Glaukopis en      │
│ ◆Aetolus 41  ◆Batym 40 🌙5min                     │   cours, 62 %      [→]  │
│                    ⌂ Firing Chamber (Nv39)        │ ③ Ton niveau 41 : mobs   │
│      ⌂ Northpoint                                │   41-43 à SW Ice Needles │
│               ▲ toi  ● groupe                    │ ④ Logos : il te manque   │
│   ◆Askalaphos 39 🌀160min  ◆Leucosia 35          │   des « Protecteurs » :  │
│ ◆Ying-Yang 49   ◆Skoll 50   ◆Flauros 36          │   Flauros, Askalaphos…   │
│      ░░ zone de prépa Skoll      ◆Sophist 37     │──────────────────────────│
│              ◆Graffiacane 38                     │ Calques : ☑ NM ☑ prépa   │
│ X 23.4 Y 30.1 (sous la souris)                   │ ☑ FATE ☑ pops chat       │
│                                                  │ ☑ groupe ☐ mobs ☐ quêtes │
└──────────────────────────────────────────────────┴──────────────────────────┘
```

Survol d'un NM : fiche (niveau, élément, mob de prépa et où, condition météo/nuit et quand,
logogramme lâché, statut tracker). Clic sur une ligne « Où aller » : centre la carte dessus ;
bouton → pose aussi le drapeau du jeu.

**Ce que la carte du jeu apporte, repris ici** : ma position et ma direction (`IClientState`),
le groupe (`IPartyList`), le drapeau posé (`AgentMap`), les éthérites et noms de lieux (feuille
`MapMarker`, plage de la ligne `Map`), les FATE en cours avec % et temps restant (`IFateTable`),
les coordonnées sous la souris. En plus, du plugin : NM, zones de prépa, pops du chat (avec leur
âge), élémentaires vus à proximité (table des objets), objectifs de quête par niveau.

**Données** : fond = texture de la carte du jeu (`ui/map/z3fc/00/z3fc00_m.tex` pour Pyros, ligne
Map 484, échelle 100) → marche pour les 4 zones, Hydatos compris, sans image tierce. Positions des
FATE : en direct dans le jeu ; la colonne `Fate.Location` n'est pas une ligne de Level (vérifié),
donc positions fixes dans `Donnees/zones/<zone>.json` relevées sur le wiki : par NM la FATE
(numéro), niveau, élément, coordonnées, mob de prépa (nom, niveau, zone x/y/rayon), condition
(météo ou nuit), logogramme lâché ; mobs de montée de niveau ; points des élémentaires ;
objectifs de quête. Noms affichés dans la langue du client via les feuilles (Fate, BNpcName).

**Étapes** (chacune testée et commitée) :
- C1 POC : fenêtre à onglets, texture de la carte, zoom/déplacement, ma position. Test : formule
  coordonnées monde ↔ carte sur des points connus (éthérite de Northpoint). Vérif en jeu : la
  flèche est au même endroit que sur la carte du jeu.
- C2 : ce que donne le jeu (groupe, drapeau, éthérites, FATE en cours, pops du chat, bandeau
  météo/heure — la météo de l'ancienne barre d'infos serveur vient ici).
- C3 : `pyros.json` (17 NM, prépas, conditions) → icônes, zones de prépa, comptes à rebours.
- C4 : panneau « Où aller » (règles simples, classées, cliquables). Relié à l'étape 2 (Logos).
- C5 : tracker en direct (étape 5) → statut des NM sur la carte. Anémos/Pagos/Hydatos : leur JSON
  quand on y va (Hydatos pas encore débloqué).

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

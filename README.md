# Xiv Eurêka

Plugin Dalamud : un compagnon pour Eurêka, branché sur le
jeu. Il grandit au fil des besoins.

## Installer

`/xlsettings` → Experimental → Custom Plugin Repositories, ajouter
`https://github.com/Pop115/XivEureka/releases/latest/download/repo.json`, enregistrer, puis
installer « Xiv Eurêka » depuis `/xlplugins`. Textes en jeu en français.

**Publier une version** : `git tag v0.1.0 && git push origin v0.1.0`. La CI GitHub
(`.github/workflows/build.yml`) construit, teste et publie une release avec `latest.zip` et
`repo.json` ; Dalamud propose alors la mise à jour.

## Ce qu'il fait

Une fenêtre « Xiv Eurêka » (`/xiveureka`, ou le bouton du plugin dans `/xlplugins`) à trois onglets :

- **Actions Logos** : la page de
  ffxiv-eureka.com/logograms, mais avec le stock lu dans le jeu. Mnemes par éclat à gauche, les 56
  actions au centre, fiche de l'action (description, recettes, stock/besoin) à droite. En violet :
  pas encore apprise et fabricable tout de suite. Les actions apprises sont lues dans le journal des
  actions Logos (ouvert en parlant à Drake) ; clic droit sur une action pour corriger entre-temps.
  La fenêtre s'ouvre toute seule avec le manipulateur Logos.
- **Quoi taper** : la chaîne actions pas apprises → mnemes manquants → éclats Logos → quoi taper.
  En haut, les actions fabricables tout de suite (aller au mélangeur), puis **« Le plus rapide »** :
  pour chaque éclat utile, sa source la plus rapide maintenant. Puis un bloc par éclat utile, le
  plus utile d'abord : les actions qu'il fait avancer, toutes ses sources, la plus rapide d'abord
  (élémentaires sous leur météo, NM avec leur état en direct, coffres des FATE lapin, coffres de
  mutation, mobs au hasard ; bouton « Carte » qui montre l'endroit, bouton « Drapeau » qui pose le
  drapeau du jeu sur l'endroit le plus proche de toi), et les mnemes qu'il peut donner à l'analyse
  avec ce qui manque pour quelles actions. Classement : temps estimé avant le premier mneme **utile**
  = attente (météo, jour/nuit, respawn, FATE) + effort selon la source (élémentaire 3 min, adapté 8,
  FATE de NM en cours 5, NM à préparer 20, FATE lapin 10 en cours / 45 à attendre, +15 argent,
  +30 or, mob 4 niveaux au-dessus +10…) divisé par la chance qu'un éclat donne un mneme qui manque
  encore (mnemes utiles / mnemes de l'éclat, à chances égales supposées ; relevée pour un coffre
  qui en donne plusieurs ; pour un NM, chaque essai de plus = un autre NM du train, 30 min). Des
  ordres de grandeur pour classer. Calcul dans `Hunt` et `HuntOptions`.
- **Carte** (`/xiveureka carte`) : la carte du jeu de la zone d'Eurêka, redessinée par le plugin.
  Repères et noms de zone du jeu, toi (flèche), ton groupe, le drapeau, et en plus les 17 NM de la
  zone avec leur état (FATE en cours, respawn ≥ N min s'il a été vu finir, météo ou nuit à venir),
  la zone de prépa du NM choisi, les pops criés dans le chat. Les NM pas faisables pour l'instant
  (respawn, météo ou nuit à attendre) sont cachés, sauf le NM choisi ; calque « NM pas faisables »
  pour les revoir. En haut : heure d'Eorzéa, jour/nuit,
  météo à venir. À droite : « Où aller maintenant » (FATE en cours, météo ou nuit dans la
  demi-heure avec la prépa, NM à ton niveau, NM qui lâchent les éclats Logos qui te manquent), la
  fiche du NM choisi et les calques. Molette : zoom ; glisser : déplacer ; clic : fiche ; clic
  droit : drapeau du jeu. Hors d'Eurêka : la dernière zone visitée, au choix.
- **Barre d'infos serveur** : une entrée courte « Eurêka » ; clic : la carte à Eurêka, les actions
  Logos ailleurs ; clic droit : les actions Logos ; infobulle : heure d'Eorzéa et météo.
- **FATE** (zones d'Eurêka) : chaque nouvelle FATE de NM est annoncée par un toast du jeu et un message
  avec lien de carte, et le drapeau de la carte du jeu est posé dessus (celles déjà là à l'arrivée
  ne sont pas annoncées). Les FATE lapin sont ignorées (on ne les fait pas) : dans la feuille Fate,
  ce sont les seules FATE d'Eurêka à avoir la règle 4 (« collecte »).
- **Chat** (cris, zone, groupe, alliance, CL, LS, CWLS ; à Eurêka seulement) : un message qui
  annonce un pop (« pop », « spawn », ou le nom d'une FATE en cours) avec un lien de carte, des
  coordonnées (« X: 12.3 Y: 14.5 », « (12.3, 14.5) ») ou le nom d'une FATE en cours → même alerte,
  drapeau sur la position. Un lien ffxiv-eureka.com/xxxx est retenu comme dernier tracker vu. Un
  pop annoncé par toi-même ne déclenche pas d'alerte (relation « joueur » du message, ou ton nom
  comme expéditeur sur les LS).
- **Réglages** : bouton de configuration dans `/xlplugins` (chaque alerte se coupe).
- Le stock est relu deux fois par seconde tant que la **fenêtre des éclats Logos** est ouverte, et
  gardé par personnage dans la configuration du plugin (utilisable loin du manipulateur).

## D'où viennent les données

- **Stock** : tableau `NumberArrayType.EurekaLogosShardList` (137) du jeu, rempli seulement fenêtre
  des éclats ouverte. Case 0 = nombre de sortes, puis 4 cases par mneme dès la case 3 : icône,
  quantité, numéro d'objet, ligne EurekaMagiciteItem (relevé du 2026-10-11, test sur ce relevé).
- **Apprises** : fenêtre `EurekaMagiaActionNotebook` ; ses 56 premières valeurs sont, pour chaque
  action, son numéro si elle est apprise, 0 sinon (relevé du 2026-10-11, test sur ce relevé).
- **Mélangeur** : fenêtre `EurekaMagiciteItemSynthesis` (relevé du 2026-10-16, tests sur ce relevé).
  Valeur 0 = état (2 = « Extraction réussie. » affiché), valeurs 15 à 20 = numéros d'objet des 6
  emplacements (0 = vide), valeur 27 = numéro dans le journal de l'action obtenue. Le jeu retire un
  mneme de la liste des éclats dès qu'il est posé, et vide les emplacements quand il est dépensé :
  stock = liste + mnemes posés. L'action affichée en résultat est marquée apprise aussitôt (le
  message système 9070, « Vous avez découvert l'action Logos “…” », sert de secours).
  Logique dans `LogosTracker` et `LogosBoard`, sans rien du jeu : scénarios par persona, dont un
  de A à Z (arrivée, journal, éclat suggéré, butin, analyse, fabrication) (`LogosScenarioTests`, sur
  un faux jeu qui écrit les fenêtres au format réel) et sessions randomisées à graine fixe
  (`LogosRandomTests`).
- **Recettes** : le jeu ne les a pas (mélange résolu par le serveur). `XivEureka/Data/logos.json`,
  extrait de ffxiv-eureka.com et recoupé avec Logogram Helper (55/56 identiques ; pour Restauration
  le site a une recette à 3 mnemes de plus, gardée).
- **Noms, icônes, descriptions** : feuilles Item, Action, ActionTransient, BNpcName, Weather,
  dans la langue du client.
- **Carte** : texture `ui/map/<id>/<id>_m.tex` de la ligne Map en cours, repères de la feuille
  MapMarker (X/Y en pixels de la texture 2048), formule monde ↔ carte de `MapGeometry`.
- **NM** : `XivEureka/Data/nm.json` (Pyros et Hydatos) : niveau, élément, conditions et
  respawn de ffxiv-eureka.com (les météos y sont des numéros de la feuille Weather), coordonnées
  des FATE et logogrammes du wiki, lieux de prépa **approximatifs** (carte communautaire).
  Hydatos : les 11 NM du tracker (Obscur : Khalamari, Stegodon, Frostmane ; les autres ne lâchent
  pas d'éclat), Daphné placé d'après la carte (le wiki se contredit). Ovni n'est pas suivi par le
  site : laissé de côté. Anémos, Pagos : à ajouter si besoin.
- **Autres sources d'éclats** : `XivEureka/Data/farms.json` (Pyros et Hydatos) : élémentaires (niveau,
  météo, adapté ou non, éclat), mobs de prépa niv. 52+ (Fondamental au hasard), Val gigantopithèque
  (coffres de mutation → Tactique), FATE lapin 1407 et 1408 (bronze : Conceptuel/Fondamental, argent :
  Curatif, or : Offensif/Protecteur). Drops : consolegameswiki et Eurekan Academy, recoupés avec le
  bestiaire communautaire ; lieux **approximatifs** lus sur la carte communautaire (même relevé que
  les prépas). Le Conceptuel ne tombe que des élémentaires de ton niveau ou plus, les adaptés ne
  lâchent leur éclat qu'une fois adaptés. Hydatos : élémentaires d'orage et de fonte (Conceptuel),
  de blizzard et de mort (Fondamental), île aux élémentaires Nv 65, blizzard adapté Nv 64
  (Offensif), mobs de prépa Nv 62+ (Obscur au hasard), coffres humides (Tactique : zébu par neige,
  vouivre et gargouille la nuit, peiste le jour), FATE lapin 1425 (bronze : Inimical/Protecteur/
  Curatif, argent : Mitigatif, or : Offensif).
- **Éclats ↔ mnemes** : numéros d'objet vérifiés sur la feuille Item du jeu le 2026-10-17 ; avant,
  5 éclats (Curatif, Offensif, Protecteur, Mitigatif, Inimical) portaient les mnemes d'un autre, et
  « Quoi taper » envoyait vers les mauvais NM. Test qui fige le bon lien.
- **Niveau élémentaire** : `PlayerState.GetContentValue(2)` (vu juste en jeu le 2026-10-15 : 45).

## Conventions

- **Code et commentaires en anglais** (le plugin pourrait devenir public un jour) ; les textes
  affichés en jeu restent en français pour l'instant. Ce README et le plan restent en français.
- Les données (`Data/*.json`) ont des clés anglaises et une ligne `source`.
- Configuration : version 2 depuis le passage à l'anglais (2026-10-15). Un fichier version 1
  (noms français) est migré au chargement par `ConfigurationMigration`, l'ancien gardé en
  `XivEureka.json.v1.bak`.

- **Rien du jeu dans les constructeurs** : activer le plugin depuis `/xlplugins` le construit hors
  du fil principal, et Dalamud y refuse l'état du jeu (`LocalPlayer` : « Not on main thread! »,
  plantage à l'initialisation du 2026-10-15). Lire le jeu au premier `Framework.Update`.

## Construire, tester, charger

```bash
dotnet test XivEureka.slnx -c Release
```

En jeu : `/xlsettings` → Experimental → Dev Plugin Locations →
`<dossier du dépôt>\XivEureka\bin\Release\XivEureka.dll`, puis `/xlplugins` → Dev Tools.
Après un build : désactiver puis réactiver le plugin.

**Relevés de développement** : toute fenêtre du jeu dont le nom commence par `Eureka` est relevée à
son ouverture dans `%AppData%\XIVLauncher\pluginConfigs\XivEureka\releves\<fenêtre>.json`
(valeurs de la fenêtre + tableaux 137/138). `/xiveureka releve` force celui des éclats.
Le journal Dalamud dit « Relevé écrit : … ».

## À faire / idées

- [x] Actions apprises lues dans le journal des actions Logos.
- [x] Détecter dans le chat les liens de tracker (ffxiv-eureka.com) — retenus ; suivi en direct : étape 5.
- [x] Détecter les messages « pop » (position d'un NM) et poser le drapeau sur la carte du jeu.
- [x] Annoncer les nouvelles FATE avec drapeau.
- [x] Onglet Carte (étapes C1 à C4 du plan).
- [ ] À valider en jeu : drapeau (coordonnées monde), noms de météo FR, textes des pops réels,
  sens de la flèche (orientation), lieux de prépa, signalement des morts sur le tracker créé.
- [x] Vu en jeu le 2026-10-15 : onglet Carte, tracker créé et suivi, niveau élémentaire.
- [x] Tracker en direct : voir « Tracker » ci-dessous.

## Tracker ffxiv-eureka.com

Un tracker est temporaire et lié à une instance. Le plugin suit celui crié dans le chat de la zone
(canal Phoenix `instance:<id>` sur `wss://ffxiv-eureka.com/socket/websocket?vsn=2.0.0`, lecture
sans mot de passe) ; sans tracker au bout de 3 min, il en crée un (`POST /api/instances`, JSON:API
`zone-id`), garde son mot de passe, le rattache au data center (`set_instance_information`) et
l'écrit dans le chat pour qu'on le crie. Sur un tracker créé par le plugin seulement, les NM vus
tués (FATE à l'état `Ended`, à valider en jeu) sont signalés (`set_kill_time`). Le canal
`datacenter:<n>` liste les autres trackers ouverts de la zone (choix dans l'onglet Carte).
Protocole relu dans le JS du site et essayé le 2026-10-14 ; messages réels dans les tests.
- Note : `/eureka` n'est jamais parvenue au plugin (aucun appel, aucune erreur) — d'où `/xiveureka`.

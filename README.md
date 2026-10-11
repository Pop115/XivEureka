# Xiv Eurêka

Plugin Dalamud **privé** (jamais publié, dépôt git local) : un compagnon pour Eurêka, branché sur le
jeu. Il grandit au fil des besoins.

## Ce qu'il fait

- **Actions Logos** (`/xiveureka`, ou le bouton du plugin dans `/xlplugins`) : la page de
  ffxiv-eureka.com/logograms, mais avec le stock lu dans le jeu. Mnemes par éclat à gauche, les 56
  actions au centre, fiche de l'action (description, recettes, stock/besoin) à droite. En violet :
  pas encore apprise et fabricable tout de suite. Les actions apprises sont lues dans le journal des
  actions Logos (ouvert en parlant à Drake) ; clic droit sur une action pour corriger entre-temps.
  La fenêtre s'ouvre toute seule avec le manipulateur Logos.
- Le stock est relu deux fois par seconde tant que la **fenêtre des éclats Logos** est ouverte, et
  gardé par personnage dans la configuration du plugin (utilisable loin du manipulateur).

## D'où viennent les données

- **Stock** : tableau `NumberArrayType.EurekaLogosShardList` (137) du jeu, rempli seulement fenêtre
  des éclats ouverte. Case 0 = nombre de sortes, puis 4 cases par mneme dès la case 3 : icône,
  quantité, numéro d'objet, ligne EurekaMagiciteItem (relevé du 2026-10-11, test sur ce relevé).
- **Apprises** : fenêtre `EurekaMagiaActionNotebook` ; ses 56 premières valeurs sont, pour chaque
  action, son numéro si elle est apprise, 0 sinon (relevé du 2026-10-11, test sur ce relevé).
- **Recettes** : le jeu ne les a pas (mélange résolu par le serveur). `XivEureka/Donnees/logos.json`,
  extrait de ffxiv-eureka.com et recoupé avec Logogram Helper (55/56 identiques ; pour Restauration
  le site a une recette à 3 mnemes de plus, gardée).
- **Noms, icônes, descriptions** : feuilles Item, Action et ActionTransient, dans la langue du client.

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
- [ ] Détecter dans le chat les liens de tracker (ffxiv-eureka.com) et les proposer.
- [ ] Détecter les messages « pop » (position d'un NM) et poser un repère sur la carte.
- [ ] Repérer les FATE sur la carte.
- Note : `/eureka` n'est jamais parvenue au plugin (aucun appel, aucune erreur) — d'où `/xiveureka`.

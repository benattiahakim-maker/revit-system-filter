# APS Design Automation for Revit

> **Mise à jour (feat/zip-io)** : l'Activity prend désormais `inputZip` / `settings` (settings.json) / `result` (output.zip). Les mentions de `rvtFile` et `options.json` ci-dessous décrivent l'ancien contrat ; le contrat à jour est dans le README (section « Design Automation : contrat ZIP »).

Le projet `Egis.Revit.SystemFilter.DesignAutomation` s'exécute **sans interface**,
dans l'environnement headless de
[APS Design Automation for Revit](https://aps.autodesk.com/en/docs/design-automation/v3/).

## Rôle : export d'inventaire des systèmes

Contrairement à l'add-in interactif (qui filtre une vue selon une sélection), la
version Design Automation **ne reçoit aucune sélection**. Au démarrage, elle
parcourt **tous les systèmes** du modèle, remonte pour chacun les éléments qui lui
appartiennent (équipements, gaines, raccords, accessoires, flexibles, terminaux…)
et produit un **JSON normalisé**.

La logique vit dans `Egis.Revit.SystemFilter.Core` (`SystemInventoryService`),
réutilisable ailleurs. La façade se contente de l'appeler et d'écrire le fichier.

```
modèle .rvt ──► SystemInventoryService.BuildInventory(doc) ──► systems-inventory.json
                    │  MechanicalSystem.DuctNetwork
                    └─ PipingSystem.PipingNetwork
```

## Schéma de sortie (normalisé)

Trois collections à plat reliées par clés (chaque **enfant référence son parent**) :

- `systemTypes[]` — `{ id, name, domain, classification }`
- `systems[]` — `{ id, uniqueId, name, systemTypeId, baseEquipmentId, levels }` → parent type ; `baseEquipmentId` (toujours présent) = clé dans `elements[]` de l'équipement de départ (`BaseEquipment`, ex. CTA ; à défaut, repli sur l'équipement mécanique du réseau raccordé au plus de systèmes, puis plus petit id, avec `WARN` dans `run.log`), ou `null` s'il n'y en a pas ou s'il est absent de `elements[]` (alors `WARN` dans `run.log`)
- `elements[]` — `{ id, uniqueId, category, domain, family, type, name, room, level, size, lengthMm, [materiel, numCta], parentIds }` → `parentIds` liste les clés des parents (aujourd'hui le ou les systèmes ; en V2 s'y ajouteront les espaces — voir [cahier des charges](CAHIER-DES-CHARGES.md))
  - `level` = nom du niveau : `LevelId`, puis paramètres de niveau (`FAMILY_LEVEL_PARAM`, `INSTANCE_REFERENCE_LEVEL_PARAM`, `RBS_START_LEVEL_PARAM`, `INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM`), puis niveau de l'hôte (y compris hôte dans un lien, nom du niveau du lien), sinon niveau le plus proche sous l'altitude du point d'insertion (ou du centre de la boîte englobante). `systems[].levels` = noms distincts des niveaux des éléments du système, triés par élévation. `run.log` : `INFO niveaux : N éléments ; sans niveau avant repli : a ; après repli (hôte / altitude) : b`.
  - `room` = `{ id, number, name, source }` : espace MEP (`FamilyInstance.Space` / `GetSpaceAtPoint`, source `space`), sinon pièce de la maquette (`FamilyInstance.Room` / `GetRoomAtPoint`, `room`), sinon pièce d'un lien chargé (`linkedRoom`) ; omis si rien trouvé. `run.log` : `INFO pièces : équipements mécaniques avec pièce : x (space …, room …, linkedRoom …) ; sans pièce : y`.

```jsonc
{
  "schemaVersion": "1.0",
  "generatedAtUtc": "2026-10-21T12:00:00Z",
  "model": { "title": "EGI-TTR-0100-CVC", "revitVersion": "2027" },
  "summary": { "systemTypeCount": 18, "systemCount": 210, "elementCount": 15230 },
  "systemTypes": [ { "id": 111, "name": "CTA BUREAUX ASF", "domain": "Duct", "classification": "SupplyAir" } ],
  "systems":     [ { "id": 201, "uniqueId": "…", "name": "ARJ CTA LT 35", "systemTypeId": 111 } ],
  "elements":    [
    { "id": 9001, "uniqueId": "…", "category": "DuctFitting", "domain": "Duct",
      "family": "Coude rectangulaire", "type": "Rayon 1.0 W", "name": "…",
      "level": "R+12", "size": "300x200", "lengthMm": 2450.0, "parentIds": [ 201 ] }
  ]
}
```

Les champs nuls sont omis. `domain` vaut `Duct` ou `Pipe`. `parentIds` contient tous les systèmes de l'élément (sans doublon, ordre de première rencontre) et ne porte que
des clés (le nom du parent vit une seule fois dans sa collection) ; une clé se résout
dans `systems` (parent système) ou, en V2, dans `spaces` (parent espace). Catégories couvertes :

| Domaine | Catégories |
|---------|------------|
| Duct | DuctCurves, DuctFitting, DuctAccessory, FlexDuctCurves, DuctTerminal, MechanicalEquipment |
| Pipe | PipeCurves, PipeFitting, PipeAccessory, FlexPipeCurves, PlumbingFixtures, Sprinklers, MechanicalEquipment |

Un élément partagé entre plusieurs systèmes est rattaché au **premier** système
rencontré (clé étrangère unique `systemId`).

## Options d'entrée (facultatives)

Sans fichier, l'export porte sur tous les réseaux. Un `options.json` permet de
restreindre la portée — voir
[`options.sample.json`](../src/Egis.Revit.SystemFilter.DesignAutomation/options.sample.json).

| Champ | Type | Défaut | Description |
|-------|------|--------|-------------|
| `includeDuctSystems` | bool | `true` | Exporter les systèmes de gaines |
| `includePipingSystems` | bool | `true` | Exporter les systèmes de tuyauterie |
| `outputFileName` | string | `systems-inventory.json` | Nom du fichier de sortie |

## Bundle AppBundle

Point d'entrée headless : `SystemFilterDbApplication` (`IExternalDBApplication`),
branché sur `DesignAutomationBridge.DesignAutomationReadyEvent`. `DesignAutomationBridge.dll`
est **embarqué** dans le bundle : la compilation le copie en sortie depuis le package
NuGet `Autodesk.Forge.DesignAutomation.Revit` restauré (2026.0.0 pour R2026, 2027.0.0 pour R2027).

Un ZIP par version de Revit :

```
EgisSystemFilter_R<année>.zip
└── EgisSystemFilter.bundle/
    ├── PackageContents.xml              (SeriesMin/Max = R<année>)
    └── Contents/
        ├── EgisSystemFilter.addin
        ├── Egis.Revit.SystemFilter.DesignAutomation.dll (+ .pdb)
        ├── Egis.Revit.SystemFilter.Core.dll (+ .pdb)
        ├── Egis.Revit.SystemFilter.DesignAutomation.Io.dll (+ .pdb)
        └── DesignAutomationBridge.dll
```

### Fabriquer le bundle

```powershell
pwsh ./build/package-da.ps1                      # R2026 et R2027
pwsh ./build/package-da.ps1 -Revit 2027 -NoPdb   # une seule version, sans .pdb
# avec un SDK .NET 10 en scope utilisateur :
pwsh ./build/package-da.ps1 -DotNet "$env:USERPROFILE\.dotnet10\dotnet.exe"
```

Le script compile `Egis.Revit.SystemFilter.DesignAutomation` en Release (net8 → R2026,
net10 → R2027), assemble le bundle à partir de `bundle/PackageContents.template.xml` et
`bundle/EgisSystemFilter.addin`, produit `artifacts/EgisSystemFilter_R2026.zip` et
`artifacts/EgisSystemFilter_R2027.zip`, puis vérifie le contenu de chaque ZIP : il échoue
si un fichier attendu manque. Il ne fait aucun appel APS et ne publie rien.
Il tourne sous Windows et sous Linux (PowerShell 7).

## Côté APS (compte Autodesk)

À réaliser hors dépôt (CLI APS, portail ou client) avec des identifiants APS :

1. **AppBundle** : téléverser le `.zip`, moteur `Autodesk.Revit+2026` (ou `+2027`), créer un alias.
2. **Activity** : paramètres `rvtFile` (entrée), `options` → `options.json` (entrée, facultatif),
   `result` → `systems-inventory.json` (sortie) ; créer un alias.
3. **WorkItem** : poster les URLs signées pour lancer l'exécution.

## Rechargement des liens après ouverture

Avant ouverture, les liens sont redirigés vers le dossier extrait (TransmissionData). Après ouverture, chaque `RevitLinkType` encore non chargé est cherché dans le dossier de travail (chemin relatif au modèle principal, puis nom de fichier unique, récursif) et rechargé par `RevitLinkType.LoadFrom(…, new WorksetConfiguration())` dans une transaction. `run.log` : une ligne `INFO lien rechargé : X.rvt chargé depuis …` ou `INFO lien rechargé : X.rvt introuvable dans le dossier de travail` par lien.

Maquette hôte : `settings.json` → `mainModelPath` (alias `hostModel`), sinon l'unique `.rvt` à la racine du ZIP.


## Pièces et boîtes englobantes (format 1.1)

Le plugin **extrait**, le backend (system-filter-backend, `src/room-analysis.ts`) **calcule** les pièces traversées, desservies et de départ : les règles changent sans republier l'AppBundle. Le plugin n'effectue aucun calcul de conflit.

`schemaVersion: "1.1"`, ajouts seulement (le format 1.0 et le comportement des anciens champs ne changent pas) :
- `models[]` : `{key, title, isHost, transform:{origin (mm), basisX, basisY, basisZ}}`. L'hôte a la clé `main` et une transformation identité ; chaque **instance** de lien chargé a la clé `link-<ElementId de l'instance>` et `RevitLinkInstance.GetTotalTransform()`.
- `rooms[]` : pièces **placées** (`Area > 0`) de l'hôte et des liens chargés : `id` (entier unique dans le fichier, attribué par le plugin car les ElementId Revit peuvent collisionner entre maquettes liées), `revitId`, `uniqueId`, `modelKey`, `number`, `name`, `level`, `area` (m²), `volume` (m³, `null` si non calculé), `phase`, `department`, `height`, `perimeter`, `baseOffset`, `limitOffset` (mm), `upperLimit`, `occupancy`, `comments`, `baseFinish`, `ceilingFinish`, `floorFinish`, `wallFinish` (`null` si absent), `bbox`.
- `bbox` (pièces et `elements[]`) : `{min:[x,y,z], max:[x,y,z]}` en mm, boîte alignée sur les axes dans le repère de la maquette **principale** : `Element.get_BoundingBox(null)`, ses 8 coins transformés par `BoundingBoxXYZ.Transform` puis `GetTotalTransform()` du lien, puis boîte alignée (`Core/Services/BoxMath.cs`, testé sous Linux). `null` si l'élément n'a pas de géométrie.
- Si l'extraction des pièces échoue, le plugin retombe sur le format 1.0 (sans `models`/`rooms`) avec un `WARN` dans `run.log` ; le backend répond alors `roomAnalysis.status: "unavailable"`.

Limites : les systèmes et leurs éléments ne sont lus que dans le document principal (les éléments de systèmes situés dans des liens ne sont pas inventoriés ; seules leurs pièces le sont) ; liens de premier niveau seulement ; toutes les phases (pas de filtre de phase dans les réglages « systems ») ; `Room` seulement (pas les espaces MEP). Le nouveau code Revit (`RoomExtractor`) compile mais n'a pas été exécuté dans Revit : il faut un test APS réel.

## Résultat natif : `envelope.json`

Le plugin calcule lui-même les pièces et écrit, à côté de `systems-inventory.json` (inchangé, format 1.1), un second
fichier `envelope.json` : les objets, propriétés et références prêts à afficher, au format de l'enveloppe commune du
backend (`contractVersion` `1.0`, voir `docs/frontend-contract.md` du dépôt `system-filter-backend`). Il ne contient ni
`serviceId` ni `urn` : le backend les ajoute (service, conversion de chaque maquette pour le viewer).

- **Calcul (`RoomAnalyzer`, hors Revit, testé sous Linux)** : une pièce est en conflit avec un élément si leurs boîtes
  (mm, repère de l'hôte, hôte et liens chargés) se recouvrent de **plus de 1 mm** sur X, Y et Z. Traversée = conflit avec
  un élément du système ; desservie = conflit avec un terminal (`DuctTerminal`, `Sprinklers`, `PlumbingFixtures`, avec ou
  sans `OST_`) ; départ = conflit avec l'équipement de départ (`baseEquipmentId`). Éléments en diagonale : faux positifs
  possibles (boîtes alignées sur les axes).
- **Structure (`ResultEnvelopeBuilder`)** : `system-N` à la racine, ses éléments (`element-N`, partagés entre systèmes
  s'ils sont identiques) puis ses pièces (`room-N`) en enfants ; pièces sans système en racines, sans `type`. Une pièce porte
  `type` (`départ`, sinon `desservie`, sinon `traversée`) et, par système, les références `system`, `servedBy` et `startOf`.
  Chaque élément référence ses pièces (`room`). Systèmes et éléments portent `modelKey` (maquette hôte), les pièces celui de
  leur maquette (`models[].key`).
- **Même résultat que le backend** : sur le job réel 5758d039 (67 pièces, 148 systèmes, 2627 éléments), `envelope.json` est
  identique, objet par objet, à ce que le traducteur provisoire du backend produisait, en 42 ms.
- **Avertissements (`warnings[]`)** : les avertissements écrits dans `run.log` (lignes `WARN`, déjà nettoyées : chemins et
  secrets masqués) sont aussi recopiés dans `warnings[]` de l'enveloppe, qui passe alors à `partial` (contrat du backend :
  `partial` = au moins un texte dans `warnings`). Le validateur du backend n'impose que « tableau de textes » ; le plugin
  borne de lui-même (`EnvelopeWarnings`) : 400 caractères par texte sur une ligne, doublons retirés, 20 textes au plus (le
  20e résume « … et N autre(s) avertissement(s), voir run.log. »). `run.log` reste la source complète.
- **Identifiant Revit des éléments** : en plus de `viewerId` (UniqueId, réservé au viewer), chaque élément porte
  `revitId` (« Identifiant Revit », l'`ElementId`, en texte).
- Un échec d'écriture de `envelope.json` est signalé dans `run.log` et n'empêche jamais l'écriture de l'inventaire.

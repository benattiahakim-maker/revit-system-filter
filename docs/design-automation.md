# APS Design Automation for Revit

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
- `systems[]` — `{ id, uniqueId, name, systemTypeId }` → parent type
- `elements[]` — `{ id, uniqueId, category, domain, family, type, name, level, size, lengthMm, [materiel, numCta], systemId }` → parent système

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
      "level": "R+12", "size": "300x200", "lengthMm": 2450.0, "systemId": 201 }
  ]
}
```

Les champs nuls sont omis. `domain` vaut `Duct` ou `Pipe`. Catégories couvertes :

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
branché sur `DesignAutomationBridge.DesignAutomationReadyEvent`. Le bridge est
fourni par le moteur à l'exécution (référence compile-only, non embarquée).

```
EgisSystemFilter.bundle/
├── PackageContents.xml                 (R2026 + R2027)
└── Contents/
    ├── 2026/   DLL + .addin (net8)
    └── 2027/   DLL + .addin (net10)
```

### Fabriquer le bundle

```powershell
pwsh ./build/package-da.ps1
# avec le SDK .NET 10 en scope utilisateur :
pwsh ./build/package-da.ps1 -DotNet "$env:USERPROFILE\.dotnet10\dotnet.exe"
```

Produit `artifacts/EgisSystemFilter.bundle/` et son `.zip`.

## Côté APS (compte Autodesk)

À réaliser hors dépôt (CLI APS, portail ou client) avec des identifiants APS :

1. **AppBundle** : téléverser le `.zip`, moteur `Autodesk.Revit+2026` (ou `+2027`), créer un alias.
2. **Activity** : paramètres `rvtFile` (entrée), `options` → `options.json` (entrée, facultatif),
   `result` → `systems-inventory.json` (sortie) ; créer un alias.
3. **WorkItem** : poster les URLs signées pour lancer l'exécution.

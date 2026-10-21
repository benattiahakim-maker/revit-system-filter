# APS Design Automation for Revit (phase 2)

Le projet `Egis.Revit.SystemFilter.DesignAutomation` exécute la même logique que
l'add-in interactif, mais **sans interface**, dans l'environnement headless de
[APS Design Automation for Revit](https://aps.autodesk.com/en/docs/design-automation/v3/).

## Principe

La logique métier vit entièrement dans `Egis.Revit.SystemFilter.Core`. La façade
headless se contente de :

1. lire les paramètres depuis `params.json` (`DesignAutomationInput`) ;
2. créer une vue 3D isométrique de vérification ;
3. appliquer les filtres via `ViewFilterService` (service partagé) ;
4. enregistrer le document résultat (`result.rvt`).

```
params.json ──► DesignAutomationInput ──► SystemFilterAutomation.Run(doc, input)
                                                     │
                                                     ├─ CvcLinkResolver
                                                     └─ ViewFilterService (Core)
```

## Paramètres d'entrée

Voir [`params.sample.json`](../src/Egis.Revit.SystemFilter.DesignAutomation/params.sample.json).

| Champ | Type | Description |
|-------|------|-------------|
| `systemTypeNames` | `string[]` | Types de systèmes à afficher |
| `materialNames` | `string[]` | Noms de matériel à afficher |
| `worksetNames` | `string[]` | Worksets PSY à conserver visibles |
| `includePsyWorksets` | `bool` | Active le filtre worksets |
| `viewName` | `string` | Nom de base de la vue générée |
| `userPrefix` | `string` | Préfixe des filtres/vues |

## Bundle AppBundle

Le point d'entrée headless est `SystemFilterDbApplication` (`IExternalDBApplication`),
branché sur `DesignAutomationBridge.DesignAutomationReadyEvent`. Le bridge est
fourni par le moteur à l'exécution : il est référencé en *compile-only*
(package `Autodesk.Forge.DesignAutomation.Revit`, `ExcludeAssets=runtime`) et
n'est donc pas embarqué.

Le bundle cible les deux moteurs via un `PackageContents.xml` multi-versions :

```
EgisSystemFilter.bundle/
├── PackageContents.xml                 (R2026 + R2027)
└── Contents/
    ├── 2026/   Egis.Revit.SystemFilter.DesignAutomation.{dll,addin} + Core.dll
    └── 2027/   idem (net10)
```

### Fabriquer le bundle

```powershell
pwsh ./build/package-da.ps1
# ou, avec le SDK .NET 10 en scope utilisateur :
pwsh ./build/package-da.ps1 -DotNet "$env:USERPROFILE\.dotnet10\dotnet.exe"
```

Produit `artifacts/EgisSystemFilter.bundle/` et `artifacts/EgisSystemFilter.bundle.zip`.

## Côté APS (à faire sur le compte Autodesk)

Ces étapes nécessitent des identifiants APS et se font hors dépôt (CLI APS,
portail, ou un petit client `Autodesk.Forge.DesignAutomation`) :

1. **AppBundle** : téléverser `EgisSystemFilter.bundle.zip`, moteur
   `Autodesk.Revit+2026` (ou `+2027`), créer un alias (`prod`).
2. **Activity** : déclarer les paramètres `rvtFile` (entrée), `params` → `params.json`
   (entrée) et `result` → `result.rvt` (sortie), référencer l'AppBundle, créer un alias.
3. **WorkItem** : poster les URLs signées des entrées/sorties pour exécuter.

Le contrat d'entrée/sortie est déjà respecté par le code : lecture de
`params.json`, écriture de `result.rvt` dans le dossier de travail.

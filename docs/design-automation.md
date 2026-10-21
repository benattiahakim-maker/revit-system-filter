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

## Reste à faire en phase 2

- Référencer le SDK Autodesk DA (`DesignAutomationBridge`) et brancher
  `DesignAutomationReadyEvent` sur `SystemFilterDbApplication.ExecuteWorkitem`.
- Fournir le `PackageContents.xml` du bundle `.bundle`.
- Définir l'`Activity` et l'`AppBundle` APS (CLI ou portail).

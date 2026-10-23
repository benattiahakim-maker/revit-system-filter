# Revit System Filter

Plugin Revit (Egis Bâtiment – Pôle BIM) pour **filtrer dynamiquement les systèmes
de gaines CVCD dans une vue 3D**, afin de vérifier le cheminement des réseaux
fluides tracés dans la maquette CVC liée à la maquette PSY.

Réécriture propre et modernisée de l'outil historique « Plugin PSY Verif SYS »
(Revit 2021), avec une architecture **MVVM / WPF** et une **couche de services
sans dépendance UI**, réutilisable en **APS Design Automation for Revit**.

## Fonctionnalités

À partir de la maquette active (PSY) et de la maquette CVC chargée en lien :

- Lister les **types de systèmes** de gaines, les **worksets** et les **noms de
  matériel** disponibles.
- Afficher / masquer dans la vue 3D active uniquement les **réseaux** (gaines,
  raccords, accessoires, flexibles, bouches) et les **équipements** rattachés aux
  systèmes sélectionnés.
- **Dupliquer et enregistrer** la vue filtrée sous un nom choisi, avec ses filtres.

## Architecture

| Projet | Rôle | Dépendances |
|--------|------|-------------|
| `Egis.Revit.SystemFilter.Core` | Modèles, abstractions et **services métier** (requêtes, filtres, vues). Aucune dépendance UI ni interaction utilisateur. | RevitAPI |
| `Egis.Revit.SystemFilter.AddIn` | Add-in interactif : ruban, commande externe, fenêtre **WPF** et **ViewModels (MVVM)**. | Core, RevitAPI(UI), WPF |
| `Egis.Revit.SystemFilter.DesignAutomation` | Façade **headless** (phase 2) exécutant les services Core dans APS Design Automation. | Core, RevitAPI |

Principe directeur : **toute la logique est dans `Core`**. Le WPF et Design
Automation ne sont que deux points d'entrée appelant les mêmes services.

## Cibles

Multi-ciblage d'un même code source :

| Target Framework | Revit | .NET |
|------------------|-------|------|
| `net8.0-windows` | 2026 | .NET 8 |
| `net10.0-windows` | 2027 | .NET 10 |

> Compiler la cible 2027 nécessite le **SDK .NET 10**.

## Build

```bash
# Les deux cibles
dotnet build -c Release

# Une seule cible (ex. Revit 2026)
dotnet build -c Release -f net8.0-windows
```

Les chemins d'installation Revit sont surchargeables :

```bash
dotnet build -p:Revit2026Path="D:\Autodesk\Revit 2026"
```

## Installation (add-in interactif)

Le build copie l'assemblage et le manifeste `.addin`. Déployer dans :

```
%AppData%\Autodesk\Revit\Addins\2026\   (ou \2027\)
```

## Design Automation : contrat ZIP (Activity `SystemsInventory_R2026` / `_R2027`)

Aligné sur `system-filter-backend` (`src/activity-contract.ts`). Pas de `/i` : l'AppBundle
ouvre lui-même le modèle principal après avoir redirigé ses liens vers le dossier extrait.

| Argument | Verbe | `zip` | `localName` | Contenu |
|----------|-------|-------|-------------|---------|
| `inputZip` | get | `true` | `input` | ZIP : `.rvt` principal + liens, arborescence relative |
| `settings` | get | `false` | `settings.json` | facultatif, voir ci-dessous |
| `result` | put | `true` | `result` | `output.zip` : `systems-inventory.json` (format 1.0 inchangé) + `run.log` |

`systems-inventory.json` : `elements[].parentIds` contient **tous** les systèmes de l'élément
(sans doublon, dans l'ordre de première rencontre), pas seulement le premier.
`systems[].baseEquipmentId` (toujours présent) : clé de l'équipement de départ du système
(`BaseEquipment` de `MechanicalSystem` / `PipingSystem`, ex. une CTA), ou `null` s'il n'y en a pas.
Si cet équipement est absent de `elements[]` (catégorie non couverte, filtré, ou situé dans un lien),
la valeur est `null` et une ligne `WARN` est écrite dans `run.log`.

`settings.json` (clés inconnues refusées, toutes facultatives) :

```json
{
  "schemaVersion": "1.0",
  "mainModelPath": "Main.rvt",
  "outputFileName": "systems-inventory.json",
  "includeDuctSystems": true,
  "includePipingSystems": true
}
```

- `mainModelPath` : chemin relatif dans le ZIP (`/` ou `\`), doit exister. Absent : l'unique `.rvt`
  à la racine du ZIP est retenu ; sinon le job échoue avec un message dans `run.log`.
- Extraction : chemins absolus, `..` et doublons refusés ; liens symboliques refusés ;
  plafonds de 1000 entrées et 4 Gio décompressés (octets réellement lus).
  **Attention :** avec `inputZip` en `zip: true`, c'est Design Automation qui décompresse le ZIP
  *avant* l'exécution de notre code. Les plafonds et contrôles de chemins sont donc vérifiés
  **après** l'écriture sur le disque du moteur (le job échoue alors proprement, mais l'espace a été
  consommé). Le backend doit donc plafonner le ZIP (taille, nombre d'entrées, chemins) **avant l'envoi**.
  L'extraction sécurisée par notre code ne s'applique que si l'entrée arrive sous forme de fichier ZIP.
- Liens Revit : résolus uniquement dans le dossier extrait (chemin relatif au modèle principal,
  puis nom de fichier unique ; ce 2e cas est signalé dans `run.log` par « résolu par nom »). Un lien introuvable n'est pas chargé et apparaît dans `run.log`
  (`WARN lien manquant : X.rvt`) ; le job continue.
- `run.log` : court, sans chemin absolu ni URL ni secret (masqués). Toujours écrit, même en échec.

La logique ZIP / réglages / liens / journal est dans `Egis.Revit.SystemFilter.DesignAutomation.Io`
(sans Revit) et testée sous Linux :

```bash
dotnet test src/Egis.Revit.SystemFilter.DesignAutomation.Io.Tests
```

### Note de déploiement (à faire par l'utilisateur, rien n'est publié par la CI ni par la PR)

1. Compiler en Release sous Windows (`dotnet build -c Release`) et produire le bundle
   `EgisSystemFilter_R2026` / `_R2027` comme d'habitude, en vérifiant que
   `Egis.Revit.SystemFilter.DesignAutomation.Io.dll` est bien dans le dossier `Contents` du bundle.
2. Publier la nouvelle version de l'AppBundle et déplacer son alias (DaManager), d'abord sur un alias de test.
3. Publier la nouvelle version de l'Activity depuis `src/Packaging/definitions/<année>/activity.json`
   (arguments `inputZip` / `settings` / `result` ; l'ancien contrat `rvtFile` / `options` disparaît
   de cette version) et la pointer sur un alias de test, **pas** sur l'alias utilisé en production.
4. Test réel : un `.rvt` seul zippé, sans `settings.json` ; puis un ZIP avec liens + `mainModelPath` ;
   puis un ZIP où un lien manque (vérifier `run.log`) ; enfin un cas en échec (ex. `mainModelPath`
   absent du ZIP) pour vérifier que `result/run.log` est bien envoyé quand `Succeeded` vaut `false`.
   Vérifier aussi qu'une CTA est bien repérée comme équipement de départ (`systems[].baseEquipmentId`
   = id de la CTA, présente dans `elements[]` en `MechanicalEquipment`).
5. Seulement ensuite, régler l'alias de l'Activity systèmes dans l'admin du backend.
   Les WorkItems déjà lancés sur l'ancienne version ne sont pas concernés.

## Licence

Usage interne Egis.

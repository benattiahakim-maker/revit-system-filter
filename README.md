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

## Licence

Usage interne Egis.

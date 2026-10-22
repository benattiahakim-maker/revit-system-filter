# Cahier des charges — Revit System Filter

Outil de vérification des systèmes CVCD : filtrage interactif dans une vue 3D
(add-in WPF) et export headless de l'inventaire des systèmes (APS Design
Automation). Ce document complète [`design-automation.md`](design-automation.md)
(schéma de sortie, bundle, activité).

## Périmètre actuel (V1)

- Add-in interactif : filtrer réseaux et équipements par système dans la vue active, enregistrer la vue filtrée.
- Export Design Automation : parcours de tous les systèmes de gaines et de tuyauterie, sortie `systems-inventory.json` (collections normalisées `systemTypes` / `systems` / `elements` reliées par clés).
- Outils APS : DaManager (publier/mettre à jour le bundle et l'activité) et WorkItemRunner (lancer un workitem, visualiser l'inventaire en arbre).
- Rattachement d'un élément à son **système** par clés : `elements[].parentIds` (liste de clés ; aujourd'hui, le ou les systèmes).

## V2 — Lien espaces ↔ systèmes

### Besoin

Savoir, pour chaque élément, **à quels espaces** il est rattaché, en plus de son
système : quelles **pièces une gaine traverse**, et **quels équipements / terminaux /
gaines une pièce contient**. La relation doit être navigable dans les deux sens.

### Principe : parents typés, par clés, sans duplication

Un élément référence ses parents **uniquement par clés** dans `elements[].parentIds`
(le nom et le détail de chaque parent ne sont définis qu'une fois, dans leur
collection — même logique que `ListeSpaces` du format TRC). Une clé se résout :

- dans `systems` → parent **System** ;
- dans `spaces` → parent **Space**.

Les `ElementId` Revit étant uniques dans la maquette, une clé désigne sans
ambiguïté un système ou un espace. Aucune nature (`kind`) n'est stockée sur
l'élément : elle se déduit de la collection où la clé est trouvée.

Exemple :

```jsonc
"spaces": [
  { "id": 5012, "uniqueId": "…", "name": "Bureau 1", "number": "001", "level": "R+1" }
],
"elements": [
  // Bouche : un système + un espace (la pièce qui la contient)
  { "id": 9050, "category": "DuctTerminal", "domain": "Duct", "family": "Bouche",
    "size": "Ø125", "parentIds": [ 201, 5012 ] },
  // CTA : plusieurs systèmes, aucun espace
  { "id": 9002, "category": "MechanicalEquipment", "family": "CTA", "numCta": "ASF-01",
    "parentIds": [ 201, 205 ] }
]
```

### Source des espaces

1. **Espaces MEP** de la maquette CVC si présents ;
2. sinon **Pièces** de la maquette **architecture** — soit en `RevitLinkInstance`
   chargé dans la CVC (géométrie transformée dans le repère CVC), soit en 2ᵉ
   entrée `archiFile` de l'activité Design Automation.

### Calcul des parents « espace »

- **Éléments ponctuels** (terminaux, équipements) : l'espace qui les contient
  (`FamilyInstance.Space` / `.Room`), soit un parent Space.
- **Éléments linéaires** (gaines, tuyaux, flexibles) : **toutes les pièces
  traversées** — intersection du solide de la gaine avec le solide de chaque pièce
  (`SpatialElementGeometryCalculator`, ou échantillonnage de points le long de la
  courbe). Un élément apparaît donc sous **chaque** pièce qu'il croise.

### Restitution

- `systems-inventory.json` : ajout de la collection `spaces` et des clés espaces dans `parentIds`.
- WorkItemRunner : double navigation obtenue par simple regroupement sur les clés —
  arbre **par système** et arbre **par espace** (un élément listé sous chacun de ses parents).

### Hors périmètre V2 (à préciser)

- Nature de la géométrie d'intersection (tolérance, prise en compte de l'isolant).
- Gestion des pièces non placées et des espaces redondants.
- Rattachement des gaines verticales traversant plusieurs niveaux.

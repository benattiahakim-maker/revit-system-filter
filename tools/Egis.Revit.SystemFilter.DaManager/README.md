# DaManager (EGR-19)

DaManager est l'outil développeur WPF qui gère l'application EG_ROOM_TRACKER sur l'Automation API. Il couvre les
engines, les AppBundles, les Activities, les versions et les alias : publication en test, promotion, retour arrière
et nettoyage des versions. Il suit le parcours de [`docs/PUBLISHING.md`](../../docs/PUBLISHING.md) et s'appuie sur la
bibliothèque [`tools/Egis.Revit.SystemFilter.Aps`](../Egis.Revit.SystemFilter.Aps/README.md), qui porte `deploy.py`.

| Projet | Cible | Contenu |
| --- | --- | --- |
| `tools/Egis.Revit.SystemFilter.DaManager` | `net8.0-windows`, WPF | Vues (XAML) et services de dialogue. Aucune logique |
| `tools/Egis.Revit.SystemFilter.DaManager.Core` | `net8.0` | ViewModels (MVVM sans type WPF), plans d'opérations, options, services APS |
| `tools/Egis.Revit.SystemFilter.DaManager.Tests` | `net8.0` | Tests xUnit sur une Automation API simulée en mémoire, sans réseau |

L'application se compile sous Linux et macOS (`EnableWindowsTargeting`), mais **elle ne se lance que sous Windows**.

## Lancer sous Windows

Prérequis : le SDK .NET indiqué dans `global.json` (.NET 10, qui compile les cibles .NET 8) et le runtime .NET 8
Desktop. Aucun Revit n'est nécessaire.

```powershell
# Identifiants : variables de la session uniquement, jamais dans un fichier versionné
$env:APS_CLIENT_ID     = "<client id>"
$env:APS_CLIENT_SECRET = "<client secret>"      # ou, à défaut : $env:APS_TOKEN = "<jeton 2-legged code:all>"
$env:APS_NICKNAME      = "<nickname>"
# Facultatif (mêmes variables que deploy.py)
$env:APS_DA_BASE_URL   = "https://developer.api.autodesk.com/da/us-east/v3"
$env:REVIT_VERSIONS    = "2025 2026 2027"      # versions cochées au démarrage
$env:APPBUNDLE_DIR     = "artifacts\appbundles" # ZIP produits par build-appbundle.sh ou par la CI
$env:EGSF_REPO_ROOT     = "C:\src\EG_ROOM_TRACKER" # sinon recherché à partir du dossier de l'exécutable
$env:EGSF_PUBLICATION_JOURNAL = "$env:OneDrive\EG_ROOM_TRACKER\publication-journal.json" # recommandé : dossier OneDrive partagé ; défaut : %LOCALAPPDATA%\Egis.Revit.SystemFilter\DaManager\publication-journal.json

dotnet run --project tools\Egis.Revit.SystemFilter.DaManager\Egis.Revit.SystemFilter.DaManager.csproj
# ou
dotnet build tools\Egis.Revit.SystemFilter.DaManager\Egis.Revit.SystemFilter.DaManager.csproj -c Release
tools\Egis.Revit.SystemFilter.DaManager\bin\Release\net8.0-windows\Egis.Revit.SystemFilter.DaManager.exe
```

Lancé depuis le dépôt, DaManager trouve seul la racine (le dossier qui contient `Egis.Revit.SystemFilter.sln` et
`src/Packaging/definitions`). En fin de session, fermer l'application et supprimer les variables :
`Remove-Item Env:APS_CLIENT_SECRET, Env:APS_TOKEN`.

## Fonctions

| Fonction | Écritures APS | Correspondance |
| --- | --- | --- |
| **Définir le nickname…** : une seule fois, avant toute publication ; voir ci-dessous | `PATCH /forgeapps/me` | PUBLISHING section 1, [OUTILS](../../docs/OUTILS.md) section 1 |
| **Actualiser** : engines Revit, AppBundles et Activities du dépôt (versions, alias), liste complète de l'application, alertes sur les engines manquants et sur le quota | aucune | `deploy.py status`, PUBLISHING section 4 |
| **Publier en test** : ZIP contrôlés (`appbundle.py check` porté en C#), nouvelle version d'AppBundle et téléversement, alias `test`, nouvelle version d'Activity, alias `test` | oui | `deploy.py test`, PUBLISHING section 5 |
| **Promouvoir test → prod** : alias `prod` de l'AppBundle sur la version en `test`, nouvelle version d'Activity `+prod`, versions précédentes affichées pour le journal | oui | `deploy.py promote`, PUBLISHING section 7 |
| **Alias / retour arrière** : créer ou déplacer un alias de la ressource sélectionnée vers une version existante | oui | `deploy.py rollback`, PUBLISHING section 8 |
| **Nettoyage des versions** : voir ci-dessous | suppressions | PUBLISHING section 8, quotas |
| **Journal de publication** : ligne ajoutée à chaque publication en test et à chaque promotion, import et export JSON ou Markdown | aucune (fichier local) | PUBLISHING section 8 |

**Plan et confirmation (#35)** : avant chaque écriture, DaManager construit le plan à partir de lectures seules. Ce
plan donne les requêtes prévues, les versions actuelles et suivantes, ainsi que le commit du dépôt, car ZIP et
définitions doivent venir du même commit. Il est affiché dans une fenêtre modale, et rien n'est écrit sans un clic sur
« Confirmer ». Le plan et la décision sont aussi inscrits au journal. Après chaque écriture, l'état est relu.

## Nickname de l'application

Le bouton **Définir le nickname…** (sous le champ Nickname) donne à l'application APS le nickname saisi dans ce
champ, qui préfixe ensuite tous les identifiants (`<nickname>.ExtractRooms_R2026+test`) :

1. lectures seules : `GET /forgeapps/me` (nickname actuel ; sans nickname, APS renvoie l'ID client, qui n'est pas
   affiché), puis `GET /appbundles` et `GET /activities` pour compter les ressources déjà présentes ;
2. plan : nickname actuel, nickname cible, requête `PATCH /forgeapps/me` avec `{"nickname": "…"}`, et avertissements.
   APS n'accepte ce changement **que si l'application n'a encore aucune donnée Design Automation**. C'est donc une
   opération unique, à faire avant la première publication : ensuite, seul `DELETE /forgeapps/me`, qui supprime
   toutes les données de l'application, permettrait d'en changer (DaManager ne le fait pas). Un nickname déjà pris par
   une autre application est refusé (`409 Conflict`) ;
3. après confirmation : `PATCH /forgeapps/me`, puis relecture par `GET /forgeapps/me`.

Si le nickname est déjà celui saisi, rien n'est demandé. Le nickname doit contenir uniquement des lettres, des
chiffres et `_` (64 caractères au plus) : c'est une règle prudente de l'outil, car le nickname préfixe les
identifiants qualifiés, qui ne doivent contenir ni point, ni `+`, ni espace. Ensuite, définir `APS_NICKNAME` avec
cette valeur pour les lancements suivants.

## Nettoyage des versions

APS limite chaque type à 100 versions (AppBundles et Activities séparément), et `deploy.py` ne supprime rien.
DaManager :

1. lit les versions et les alias de la ressource sélectionnée ;
2. conserve **toujours** les versions référencées par un alias (`test`, `prod` ou autre), les *N* plus récentes
   (*N* ≥ 1, 10 par défaut), **toutes les versions citées par le journal de publication** pour cette version de Revit
   et ce type de ressource (relu à chaque nettoyage, nécessaires au retour arrière) et les versions saisies en plus ;
   si le journal est illisible, rien n'est planifié ;
3. affiche la liste complète des versions à supprimer, avec la raison de conservation de chacune des autres ;
4. demande une confirmation explicite : il faut saisir `SUPPRIMER` dans la fenêtre ;
5. relit les alias juste avant de supprimer. Si une version du plan est devenue référencée entre-temps, rien n'est
   supprimé.

Une suppression est définitive : la version supprimée ne peut plus servir de retour arrière.

## Journal de publication

Le journal de [`docs/PUBLISHING.md`](../../docs/PUBLISHING.md) section 8 (Date | Commit | Revit | Alias |
AppBundle test / prod | Activity test / prod | Version précédente de l'alias) est tenu dans un fichier lisible.

**Emplacement** : par défaut `%LOCALAPPDATA%\Egis.Revit.SystemFilter\DaManager\publication-journal.json`, local au poste.
La variable `EGSF_PUBLICATION_JOURNAL` (ou le champ « Fichier ») le remplace. **Un dossier OneDrive partagé est
recommandé** : tous les postes qui publient lisent et complètent alors le même journal, et le nettoyage de chacun
protège les versions publiées par les autres. Par exemple :

```powershell
$env:EGSF_PUBLICATION_JOURNAL = "$env:OneDrive\EG_ROOM_TRACKER\publication-journal.json"
```

**Fichier partagé, dernier écrit gagnant** : OneDrive synchronise des fichiers entiers. Si deux postes écrivent le
journal presque en même temps, la dernière version synchronisée remplace l'autre, et OneDrive peut aussi créer une
copie en conflit (`publication-journal-<poste>.json`). DaManager limite ce risque :

- chaque ligne a un **identifiant stable** (`id`) : date, version de Revit, alias et versions pointées par l'alias
  après la publication, par exemple `2026-10-08/R2026/test/ab4/act7`. Les lignes sans `id` (format 1, table
  Markdown, fichiers écrits avant) reçoivent cet identifiant calculé ;
- **juste avant d'écrire**, DaManager relit le fichier, ajoute ses nouvelles lignes à celles qu'il contient (fusion
  par identifiant, sans doublon), puis écrit dans un fichier temporaire qui remplace le journal d'un coup. Les lignes
  ajoutées par un autre poste depuis l'ouverture de DaManager sont donc conservées ;
- il reste une fenêtre très courte entre la relecture et le remplacement, et surtout le délai de synchronisation
  OneDrive entre postes : **publier l'un après l'autre** (c'est de toute façon la règle, PUBLISHING section 5),
  attendre que OneDrive ait synchronisé, et, en cas de copie en conflit, l'importer (**Importer…**) pour la fusionner.

Format (version 2) :

```json
{
  "format": "egsf-publication-journal",
  "version": 2,
  "entries": [
    {
      "id": "2026-10-08/R2026/test/ab4/act7",
      "date": "2026-10-08",
      "commit": "cb73e3d",
      "revit": "2026",
      "alias": "test",
      "appBundle": { "test": 4, "prod": 3 },
      "activity": { "test": 7, "prod": 6 },
      "previous": { "appBundle": 3, "activity": 5 }
    },
    {
      "id": "2026-10-09/R2026/prod/ab4/act8",
      "date": "2026-10-09",
      "commit": "9b9a3fb",
      "revit": "2026",
      "alias": "prod",
      "appBundle": { "test": 4, "prod": 4 },
      "activity": { "test": 7, "prod": 8 },
      "previous": { "appBundle": 3, "activity": 6 }
    }
  ]
}
```

- Après chaque **publication en test** et chaque **promotion** confirmées, DaManager ajoute une ligne par version de
  Revit. `alias` indique l'alias concerné (`test` ou `prod`), les versions des alias sont relues après l'écriture et
  `previous` donne la version que cet alias pointait avant (`null` la première fois) : c'est la cible du retour arrière.
- **Compatibilité** : un journal au format 1 (sans `alias`, avec `previousProd`) et l'ancienne table Markdown à
  6 colonnes restent lisibles. Leurs lignes sont des promotions (`prod`). Le fichier est réécrit au format 2 à la
  publication suivante. `id` est facultatif à la lecture ; un `id` présent est conservé tel quel.
- **Importer…** lit un journal JSON ou une table Markdown (`.md`, par exemple copiée d'une description de PR ou d'une
  release) et le fusionne sans doublon (par identifiant, le fichier étant relu juste avant l'écriture). Un fichier invalide est refusé avec la ligne ou l'entrée fautive, sans toucher
  au journal.
- **Exporter…** écrit le journal en JSON ou, avec l'extension `.md`, sous forme de la table de PUBLISHING, prête à coller.
- Le nettoyage protège automatiquement toutes les versions citées (test, prod et version précédente).

Le fichier ne contient ni identifiant ni URL.

## Secrets

Le secret client, le jeton, les champs `formData` S3 et les URL signées ne sont jamais affichés. Ils ne figurent ni
dans le journal ni sur le disque. Chaque entrée du journal passe par `SecretRedactor`, et l'en-tête affiche seulement
la *source* des identifiants (`APS_CLIENT_ID / APS_CLIENT_SECRET` ou `APS_TOKEN`).

## Tests

```bash
dotnet test tools/Egis.Revit.SystemFilter.DaManager.Tests/Egis.Revit.SystemFilter.DaManager.Tests.csproj
```

Les ViewModels sont testés contre `FakeApsServer`, une Automation API en mémoire qui échoue si l'on tente de supprimer
une version référencée par un alias. Les tests couvrent les cas suivants :

- plan refusé, donc aucune écriture ;
- publication, promotion et retour arrière confirmés ;
- ZIP refusé, arrêt avant la confirmation ;
- nettoyage avec versions aliasées et protégées ;
- journal : JSON et Markdown équivalents, aller-retour, erreurs explicites, versions protégées automatiquement,
  journal illisible bloquant le nettoyage, lignes ajoutées à la publication en test et à la promotion, lecture et
  mise à niveau d'un journal au format 1, import sans doublon et export ; journal partagé modifié entre le chargement
  et l'écriture (les deux lignes sont gardées, sans doublon), identifiants stables, écriture atomique ;
- nickname : plan refusé sans écriture, `PATCH /forgeapps/me` confirmé puis relu, nickname déjà défini, application
  avec données ou nickname pris (409), nickname invalide refusé avant toute requête, ID client jamais affiché ;
- erreurs journalisées sans secret ;
- identifiants absents.

# WorkItemRunner (EGR-20)

WorkItemRunner est l'outil développeur WPF qui lance l'extraction EG_ROOM_TRACKER sur l'Automation API à partir d'un
ZIP de maquette et d'un `settings.json`. Il téléverse les entrées dans OSS, soumet le WorkItem, suit son statut, puis
récupère le rapport APS et `result.json`. Le résultat est affiché (synthèse, locaux, anomalies) et validé contre
[`docs/schema/result.schema.json`](../../docs/schema/result.schema.json). Chaque lancement est conservé dans un
historique local exportable. L'outil s'appuie sur la bibliothèque
[`tools/Egis.Revit.SystemFilter.Aps`](../Egis.Revit.SystemFilter.Aps/README.md).

| Projet | Cible | Contenu |
| --- | --- | --- |
| `tools/Egis.Revit.SystemFilter.WorkItemRunner` | `net8.0-windows`, WPF | Vues (XAML) et services de dialogue. Aucune logique |
| `tools/Egis.Revit.SystemFilter.WorkItemRunner.Core` | `net8.0` | ViewModel (MVVM sans type WPF), arguments du WorkItem, contrôles des entrées, lecture de `result.json`, historique |
| `tools/Egis.Revit.SystemFilter.WorkItemRunner.Tests` | `net8.0` | Tests xUnit sur une Automation API et un OSS simulés en mémoire, sans réseau |

L'application se compile sous Linux et macOS (`EnableWindowsTargeting`), mais **elle ne se lance que sous Windows**.

## Lancer sous Windows

Prérequis : le SDK .NET indiqué dans `global.json` (.NET 10, qui compile les cibles .NET 8) et le runtime .NET 8
Desktop. Aucun Revit n'est nécessaire. L'Activity visée doit déjà être publiée avec l'alias choisi (DaManager ou
`deploy.py`, voir [`docs/PUBLISHING.md`](../../docs/PUBLISHING.md)).

```powershell
# Identifiants : variables de la session uniquement, jamais dans un fichier versionné
$env:APS_CLIENT_ID     = "<client id>"
$env:APS_CLIENT_SECRET = "<client secret>"      # ou, à défaut : $env:APS_TOKEN = "<jeton 2-legged code:all data:read data:write data:create bucket:create bucket:read>"
$env:APS_NICKNAME      = "<nickname>"
$env:APS_BUCKET        = "egr-workitems-<suffixe>" # recommandé ; [-_.a-z0-9], 3 à 128 caractères, unique chez APS
# Facultatif
$env:APS_DA_BASE_URL   = "https://developer.api.autodesk.com/da/us-east/v3"
$env:EGR_REPO_ROOT     = "C:\src\EG_ROOM_TRACKER" # sinon recherché à partir du dossier de l'exécutable
$env:EGR_RUNNER_DATA   = "D:\egr-runs"           # historique ; défaut : %LOCALAPPDATA%\Egis.Revit.SystemFilter\WorkItemRunner

dotnet run --project tools\Egis.Revit.SystemFilter.WorkItemRunner\Egis.Revit.SystemFilter.WorkItemRunner.csproj
# ou
dotnet build tools\Egis.Revit.SystemFilter.WorkItemRunner\Egis.Revit.SystemFilter.WorkItemRunner.csproj -c Release
tools\Egis.Revit.SystemFilter.WorkItemRunner\bin\Release\net8.0-windows\Egis.Revit.SystemFilter.WorkItemRunner.exe
```

Lancé depuis le dépôt, WorkItemRunner trouve seul la racine (le dossier qui contient `Egis.Revit.SystemFilter.sln` et
`src/Packaging/definitions`). Il y lit les définitions d'Activity, le schéma de `result.json` et l'exemple de
`settings.json`. En fin de session, fermer l'application et supprimer les variables :
`Remove-Item Env:APS_CLIENT_SECRET, Env:APS_TOKEN`.

## Fonctions

| Fonction | Écritures APS | Détail |
| --- | --- | --- |
| **Configuration** : version de Revit (2025, 2026, 2027), alias `test` ou `prod`, nickname, bucket | aucune | L'Activity `<nickname>.SystemFilter_R<année>+<alias>` doit exister ; son alias est vérifié avant le plan |
| **Entrées** : ZIP de maquette et éditeur de `settings.json` (ouvrir un fichier, charger l'exemple) | aucune | Contrôles ci-dessous ; `settings.json` est validé en direct par `SettingsReader`, comme dans l'add-in |
| **Lancer** : plan, confirmation, bucket (transient), téléversements, URL signées, `POST /workitems` | oui, facturé | Les arguments sont construits à partir des `parameters` de `src/Packaging/definitions/<année>/activity.json` |
| **Suivi** : interrogation de `GET /workitems/{id}` (intervalle réglable) | aucune | **Arrêter le suivi** laisse le WorkItem tourner côté APS |
| **Annuler le WorkItem** : `DELETE /workitems/{id}` | oui | Confirmation renforcée : il faut saisir `CONFIRMER` |
| **Résultats** : rapport APS, `result.json` (synthèse, locaux, anomalies, écarts au schéma) | aucune | Une sortie absente est notée, sans erreur (cas D6 de `src/Packaging/README.md`) |
| **Récupérer** : pour le lancement sélectionné, relecture du statut du WorkItem puis, s'il est terminé, rapport et sorties | finalisation des sorties OSS | Fonctionne après un redémarrage ; URL signée de lecture redemandée à chaque fois |
| **Historique** : un dossier par lancement, export ZIP | aucune | Voir ci-dessous |

**Plan et confirmation** : avant tout téléversement et toute soumission, WorkItemRunner construit le plan à partir de
lectures seules. Ce plan donne l'Activity et sa version, le bucket, chaque entrée (fichier, taille, clé d'objet), chaque
sortie et les avertissements des contrôles. Il est affiché dans une fenêtre modale, et rien n'est écrit sans un clic
sur « Confirmer ». Le plan et la décision sont aussi inscrits au journal.

### Bucket OSS

Les clés de bucket sont **globales** chez APS (toutes applications et régions confondues) et doivent respecter
`[-_.a-z0-9]{3,128}` (POST `/oss/v2/buckets`). **Définir `APS_BUCKET` est recommandé.** À défaut, la clé est dérivée
de façon déterministe de l'ID client : `egr-workitems-` suivi des 20 premiers caractères hexadécimaux du SHA-256 de
`APS_CLIENT_ID` (l'ID lui-même n'apparaît pas). Le même poste, ou un autre avec la même application APS, retrouve donc
le même bucket. Sans ID client (`APS_TOKEN` seul), elle dérive du nickname (`egr-workitems-<nickname>`). Une clé
invalide est refusée avant toute écriture.

### Contrôles des entrées

| Contrôle | Effet |
| --- | --- |
| ZIP absent, illisible, vide ou de plus de 5 Gio | bloquant |
| Aucun `.rvt` dans le ZIP | bloquant |
| Plusieurs `.rvt` (préciser `mainModelPath`), chemin de plus de 150 caractères, noms non ASCII sans fichier `.codepage` | avertissement (voir [`docs/INPUTS.md`](../../docs/INPUTS.md)) |
| `settings.json` vide | bloquant |
| `settings.json` refusé par `SettingsReader` | avertissement seulement : le cas D6 envoie volontairement un fichier invalide pour vérifier la réponse du moteur |

### Historique

Chaque lancement est rangé dans `<EGR_RUNNER_DATA>/runs/<date>-<id>/` :

- `run.json` : date, version, alias, Activity, bucket, identifiant et statut du WorkItem, **clés des objets OSS**
  (entrées et sorties, avec la clé de finalisation `uploadKey` des sorties), statistiques, sorties présentes ou
  absentes, statut de `result.json` et nombre d'écarts au schéma. Ce fichier est écrit **avant** la soumission ;
  il ne contient jamais d'URL signée ni de jeton ;
- `settings.json` : copie de l'entrée envoyée ;
- `report.txt` : rapport APS (valable 24 h côté APS) ;
- `result.json` : sortie téléchargée.

**Récupérer** relit ce `run.json` : `GET /workitems/{id}` (statut conservé 3 jours par APS), rapport si le WorkItem
est terminé, puis pour chaque sortie finalisation (`uploadKey`, valable environ 24 h) et téléchargement par une URL
signée de lecture demandée à l'instant. Un WorkItem encore en cours est seulement mis à jour ; la récupération peut
être relancée sans risque (une sortie déjà finalisée est simplement téléchargée de nouveau). Elle fonctionne après
« Arrêter le suivi » comme après la fermeture de l'application, tant que les objets existent (bucket `transient` :
24 h). Les lancements enregistrés avant cette version n'ont pas les clés d'objets et ne sont pas récupérables.

**Exporter** produit un ZIP de ce dossier, à joindre à un relevé de recette. Le journal donne en fin de lancement la
ligne de relevé D6 : `Relevé : WorkItem <statut>, result.json présent (status …)` ou `… result.json absent.`.

## Limites connues

- Les URL signées d'OSS expirent après 60 minutes au plus. Un WorkItem plus long ne pourrait plus écrire
  `result.json` ; le plan le rappelle.
- « Récupérer » dépend des délais APS : statut du WorkItem 3 jours, clé de finalisation et objets `transient` environ
  24 heures.
- Le bucket est créé en politique `transient` : les objets sont supprimés par APS après 24 heures.

## Secrets

Le secret client, le jeton et les URL signées (entrées, sorties, rapport) ne sont jamais affichés. Ils ne figurent ni
dans le journal, ni dans le plan, ni dans l'historique. Chaque entrée du journal passe par `SecretRedactor`, et l'en-tête
affiche seulement la *source* des identifiants (`APS_CLIENT_ID / APS_CLIENT_SECRET` ou `APS_TOKEN`). Les URL S3 et le
rapport sont lus sans en-tête `Authorization`.

## Tests

```bash
dotnet test tools/Egis.Revit.SystemFilter.WorkItemRunner.Tests/Egis.Revit.SystemFilter.WorkItemRunner.Tests.csproj
```

Le ViewModel est testé contre `FakeAps`, une Automation API et un OSS en mémoire dont le « moteur » écrit
`result.json` sur l'URL signée de sortie. Le faux dépôt reprend les vraies définitions d'Activity, le schéma et les
exemples. Les tests couvrent les cas suivants :

- arguments du WorkItem conformes aux paramètres de l'Activity, paramètre requis manquant ou inconnu refusé ;
- contrôles du ZIP et de `settings.json` ;
- plan refusé, donc aucune écriture ;
- lancement complet : téléversements, soumission, rapport, `result.json` valide, historique et export ;
- WorkItem en échec sans `result.json` (relevé D6) et `result.json` non conforme au schéma ;
- arrêt du suivi et annulation confirmée du WorkItem ;
- alias absent ou ZIP refusé, arrêt avant le plan ;
- « Récupérer » après redémarrage (résultat et rapport), WorkItem encore en cours, récupération répétée, échec sans
  sortie, lancement ancien sans clés ;
- bucket par défaut dérivé de l'ID client (déterministe, règle OSS), `APS_BUCKET` prioritaire, bucket invalide refusé ;
- aucune URL signée ni aucun jeton dans le journal, le plan ou l'historique (fichiers sur disque vérifiés).

# Egis.Revit.SystemFilter.Aps

Bibliothèque `net8.0`, sans WPF ni Revit, qui sert de client Autodesk Platform Services aux outils développeur
EGR-19 (#35, `DaManager`) et EGR-20 (#36, `WorkItemRunner`). Elle reprend la logique de
[`src/Packaging/scripts/deploy.py`](../../src/Packaging/scripts/deploy.py), qui reste la référence : mêmes appels, même
ordre, mêmes messages.

| Élément | Rôle |
| --- | --- |
| `ApsSettings` | Lecture des variables d'environnement, création du fournisseur de jeton |
| `TwoLeggedTokenProvider` / `StaticTokenProvider` | OAuth 2-legged (`POST /authentication/v2/token`, jeton en cache) / jeton fourni |
| `DesignAutomation.DesignAutomationClient` | Engines, AppBundles (création, version, téléversement S3), Activities, alias, WorkItems (soumission, suivi, rapport), nickname (`GET` / `PATCH /forgeapps/me`) |
| `Oss.OssClient` | Bucket, téléversement par URL S3 signées (une ou plusieurs parties), URL signées de lecture et d'écriture |
| `Packaging.DefinitionLoader` | Lecture de `src/Packaging/definitions` (`{{NICKNAME}}`, `{{APPBUNDLE_ALIAS}}`). `commandLine` n'est pas modifié |
| `Packaging.AppBundleChecker` | Port de `appbundle.py check`. Les deux implémentations doivent évoluer ensemble |
| `Packaging.VersionCleanupPlanner` / `VersionCleanupService` | Nettoyage des anciennes versions (quota APS de 100). Le plan est pur : il garde les N plus récentes (N ≥ 1), **toutes** les versions référencées par un alias et les versions protégées (journal). Avant toute suppression, `ExecuteAsync` relit les alias et refuse s'il en trouve une nouvelle |
| `Packaging.DeploymentService` | Commandes `test`, `promote`, `rollback` et `status` de `deploy.py` |

## Variables d'environnement

| Variable | Usage |
| --- | --- |
| `APS_CLIENT_ID`, `APS_CLIENT_SECRET` | OAuth 2-legged (prioritaire). Scopes : `code:all`, plus `bucket:create bucket:read data:read data:write data:create` pour OSS |
| `APS_TOKEN` | Jeton 2-legged utilisé en secours, comme dans `deploy.py` |
| `APS_NICKNAME` | Nickname de l'application APS, pour les identifiants qualifiés (`nickname.ExtractRooms_R2026+test`) |
| `APS_DA_BASE_URL` | Valeur par défaut : `https://developer.api.autodesk.com/da/us-east/v3` |

## Secrets

- Le secret client, le jeton, les champs `formData` S3 et les URL signées (téléversement, téléchargement, `reportUrl`)
  ne sont jamais rendus par `ToString()` et n'apparaissent dans aucun message d'`ApsException`.
- Les messages d'erreur désignent la requête par un libellé. Le corps de la réponse y est tronqué à 500 caractères, puis
  masqué par `SecretRedactor` (`Egis.Revit.SystemFilter.Logging`).
- Aucun jeton APS n'est envoyé aux URL S3 signées.

## Exemple

```csharp
using var http = new HttpClient();
var settings = ApsSettings.FromEnvironment();
var tokens = settings.CreateTokenProvider(http, ApsScopes.All);
var da = new DesignAutomationClient(http, tokens, settings.DaBaseUrl);
var deployment = new DeploymentService(da, DefinitionLoader.ForRepository(repoRoot), Console.WriteLine);
await deployment.StatusAsync(DeploymentService.SupportedVersions);
```

Contrairement à `deploy.py`, la bibliothèque n'a pas de mode dry-run : chaque méthode d'écriture appelle APS. Le plan et
la confirmation reviennent aux applications (#35).

## Tests

```bash
dotnet test tools/Egis.Revit.SystemFilter.Aps.Tests/Egis.Revit.SystemFilter.Aps.Tests.csproj
```

Les tests passent par un `HttpMessageHandler` simulé (`FakeHttpHandler`), qui vérifie l'ordre, la méthode, l'URL, les
en-têtes et le corps de chaque requête. Ils ne font aucun appel réseau et n'utilisent aucun identifiant. Les tests de
publication lisent les vraies définitions de `src/Packaging/definitions`.

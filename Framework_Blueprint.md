# Framework Blueprint — socle desktop .NET 8 / WPF d'entreprise

Squelette de démarrage **générique** pour applications de gestion multi-postes
(auto-école, salon de coiffure, stock…). Zéro lien avec un produit existant :
solution propre, zéro code métier dans les briques.

---

## QUICK START — un nouveau produit métier en 2 heures (cheat sheet)

```text
HEURE 0,0          HEURE 0,5              HEURE 1,0               HEURE 1,5
copier/renommer →  entités + contexte →   migrations + synchro →  premier écran CRUD
```

1. **20 min — copier et renommer.** Copier les quatre projets `Framework.*` et
   `Framework.sln` dans le nouveau dépôt. Dans `Framework.WPF.csproj` :
   `AssemblyName`/`Product` (ex. `AutoEcole`). Dans `App.xaml.cs` :
   `NomApplication` (dossier `%LocalAppData%`/`%AppData%`/`%ProgramData%`) et
   `CleLicenceDemo` → la clé HMAC propre au produit.
   `dotnet build Framework.sln -c Release` doit sortir à **0 erreur**.
2. **40 min — le domaine.** Entités héritées d'`EntityBase` ; contexte :
   ```csharp
   public class AutoEcoleDbContext : FrameworkDbContext
   {
       public AutoEcoleDbContext(DbContextOptions<AutoEcoleDbContext> o) : base(o) { }
       public DbSet<Eleve> Eleves => Set<Eleve>();
       protected override void OnModelCreating(ModelBuilder mb)
       {
           base.OnModelCreating(mb);
           mb.ApplyConfigurationsFromAssembly(typeof(AutoEcoleDbContext).Assembly);
       }
   }
   ```
   Déclarer à la composition root : `services.AddFrameworkData<AutoEcoleDbContext>();`
3. **30 min — base et temps réel.** Base créée par l'**assistant réseau** au
   premier lancement (il teste, crée la base si absente, écrit les réglages).
   Migration : `dotnet ef migrations add Initial` (avec une
   `IDesignTimeDbContextFactory`). Synchro inter-postes dans une migration :
   `migrationBuilder.Sql(SyncNotifySql.Script("framework_sync", new[] { "Eleves" }));`
4. **30 min — premier écran.** `ElevesViewModel` (ObservableObject) avec
   `IUnitOfWork` transitoire + `uow.Repository<Eleve>()` ; ajouter la
   `NavigationItem` dans `ShellViewModel.Items`, le `DataTemplate` dans
   `App.xaml`, la View ; s'abonner à `ExternalEntityChangedMessage`
   (**WeakReferenceMessenger uniquement** — le pont est déjà posé dans `App`).
5. **Licence du poste.** L'éditeur émet le code :
   `OfflineLicenseEngine.Emettre(cleSecrete, produit, hwidClient, expiration)` ;
   le poste active : `ILicenseService.Activate(code, client)`.

---

## 1. Les briques

```
Framework.sln
├── Framework.Core      net8.0          contrats + modèles purs — zéro dépendance
├── Framework.Data      net8.0          EF Core 9 + Npgsql — sans UI
├── Framework.Common    net8.0-windows  réglages, chiffrement, licence, Velopack, Sentry, PDF/CSV
├── Framework.WPF       net8.0-windows  shell : hôte DI, navigation, thème, écrans
└── Framework.Tests     net8.0-windows  101 tests xUnit — sans base de données réelle
```

Dépendances toujours dans ce sens : `WPF → Common → Core` et `WPF → Data → Core`.
Data et Common ne se référencent jamais entre eux (jonction au composition root).

| Brique | Pièce | Rôle |
|---|---|---|
| Core | `IEntity`, `IAuditableEntity`, `ISoftDeletableEntity` | conventions de domaine reconnues par le contexte |
| Core | `IGenericRepository<T>`, `IUnitOfWork` | dépôts génériques + frontière d'écriture unique |
| Core | `ISyncNotificationService`, `SyncNotificationDto` | contrat temps réel inter-postes |
| Core | `INavigationService`, `IThemeService`, `IAuthService`, `IHorloge`, `ISettingsService`, `IAlertService`, `ILicenseService` | transverses sans référence UI/DB |
| Core | `PasswordHasher` | PBKDF2-SHA256 150 000 itérations, temps constant |
| Data | `FrameworkDbContext` | clés = `Id`, audit au SaveChanges, DELETE → suppression logique + filtre global, configurations par assembly |
| Data | `GenericRepository<T>`, `UnitOfWork` | contexte privé par UoW, `ExecuteInTransactionAsync` sous `CreateExecutionStrategy` |
| Data | `PostgresConnectionResolver` | chaîne de connexion **source unique**, `Actualiser()` à chaud après assistant, test par SQLSTATE en message français, `GarantirBaseAsync` (whitelist du nom de base) |
| Data | `PostgresSyncListener` | LISTEN/NOTIFY hors pool, battement de cœur 30 s, backoff 1→30 s, anti-écho `sender` |
| Data | `SyncNotifySql` | fonction plpgsql + trigger AFTER I/U/D par table |
| Data | `AuthService` | session + anti-force-brute (3 échecs → gel 60 s via `IHorloge`), amorce du premier compte |
| Common | `JsonSettingsService` | `%LocalAppData%\{AppName}\settings.json`, atomique, fichier cabossé = redémarrage à vide |
| Common | `EncryptionService` | clé AES-256-GCM par machine sous DPAPI, blocs + AAD (nonce+index), `Protect/Unprotect` |
| Common | `HwidProvider` | empreinte machine stable (MachineGuid, sinon ID de volume), Groupement hexa lisible |
| Common | `OfflineLicenseEngine` | challenge/response HMAC-SHA256 : expiration + signature dans un code base32 en 4 paquets |
| Common | `LicenseService` | `%ProgramData%\{AppName}\licence.json`, statut = signature + HWID + date (`IHorloge`) |
| Common | `OnlineLicenseValidator` | révocation en ligne style Firebase REST — ne punit jamais sur panne réseau |
| Common | `PdfReportService` + `RapportModele` | QuestPDF Community : rapports A4 en sections/tabulaires, sans type métier |
| Common | `CsvExportService` | export universel RFC 4180, `;` + BOM UTF-8 par défaut (Excel FR), formats stables |
| Common | `VelopackUpdateService` | null hors installation ; application **uniquement dans `OnExit`** |
| Common | `TelemetrySentry` | amorçage optionnel par DSN, seuil Error+ |
| Common | `GrowlAlertService` | notifications empilées, Dispatcher garanti |
| WPF | `App.xaml.cs` | composition root ; Velopack en toute première ligne ; ouverture **automatique de l'assistant réseau** si le poste n'est pas configuré |
| WPF | `ThemeService` / `NavigationService` / `ShellViewModel` | thème à chaud + accent persisté ; pile de types résolue en DI ; sidebar rétractable |
| WPF | `ConnectionConfigWindow` | **assistant réseau autonome** : test, création de base, enregistrement — premier écran d'un poste neuf |
| Tests | `Framework.Tests` | hachage, chiffrement (fichier + DPAPI), licence/HWID complète, réglages, CSV/PDF, resolver + assistant, `AuthService` (gel inclus) sur EF InMemory |
| CI | `.github/workflows/framework-ci.yml` | windows-latest : Release build + 101 tests sur tout push touchant le framework |

## 2. Règles permanentes du framework

- **Toute écriture passe par `IUnitOfWork`** ; un contexte neuf par écran.
- **Le temps se lit dans `IHorloge`** — les 60 s de gel et l'expiration de
  licence se testent en avançant l'horloge, jamais en dormant.
- **Zéro jargon technique à l'écran** : phrases françaises, trame Npgsql au journal.
- **Zéro secret en dur** : réglages réseau vides par conception ; la clé HMAC de
  licence appartient au produit, pas au framework. `settings.json`, `bin/`, `obj/`,
  `.vs/` ne se committent jamais (`.gitignore` fourni).
- **Un poste non installé sous Velopack n'est pas une anomalie** : le wrapper
  rend `null`, le poste tourne sans [ERR].

## 3. Pièges mesurés, déjà désamorcés

- `AesGcm.Encrypt/Decrypt` (.NET 8) place l'**AAD en dernier** :
  `Encrypt(nonce, clair, chiffré, tag, aad)`. Passer l'AAD en premier se
  compile (surcharge positionnelle) et chiffrait des blocs invalides — trouvé
  par `Framework.Tests`, corrigé dans `EncryptionService`.
- Les **usings implicites d'un projet WPF n'incluent ni `System.IO` ni
  `System.Net.Http`** : les ajouter explicitement.
- `WaitExitThenApplyUpdates(update, silent, restartApp)` : positionnel, appel
  **exclusivement** en fin de fermeture (fenêtre de 60 s).
- **Une fenêtre WPF fermée ne se rouvre pas** : fenêtres `Transient`, la
  nouvelle montrée avant la fermeture de l'ancienne (`OnLastWindowClose`).
- `PasswordBox.Password` n'est **pas une DependencyProperty** : passage au
  ViewModel par le code-behind à chaque frappe.
- Toute clé de thème nouvelle doit exister dans `Light.xaml` **et** `Dark.xaml`
  et se consommer en `DynamicResource`.
- `EnableRetryOnFailure` interdit les transactions « à la main » : passer par
  `ExecuteInTransactionAsync`.
- Le **`sender` d'une notification est l'`Application Name`** (Npgsql rend la clé
  avec un espace) de la connexion émettrice : `BuildConnectionString` est
  obligatoire, y compris pour l'outil de migration.
- Les **`PackageReference` sont épinglées** sur les versions du dépôt hôte
  (EF 9.0.10, Npgsql 9.0.4, QuestPDF 2026.2.4, xunit 2.6.2).

## 4. Ce que le cadre laisse au produit

- **Rôles et permissions** : `CompteEntity.Role` est une chaîne libre ; la
  matrice est la première règle métier du produit.
- **Écrans CRUD** : le shell fournit la mécanique, le produit écrit ses vues.
- **Secret de licence** : l'éditeur du produit tient SA clé HMAC et génère les
  codes d'activation avec `OfflineLicenseEngine.Emettre` (outil distributeur).

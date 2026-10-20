# Framework Blueprint — socle desktop .NET 8 / WPF d'entreprise

Squelette de démarrage **générique** pour applications de gestion multi-postes
(auto-école, salon de coiffure, stock…). Zéro lien avec un produit existant :
solution propre, zéro code métier dans les briques.

> **Documentation lisible** : `documentation/index.html` (structure, tutoriels,
> CDC, manuel de test, qualité). Ce blueprint reste le document de travail de
> l'agent ; en cas d'écart, **le code a raison, puis le blueprint, puis la page**.

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
   La migration `Initial` du socle est **déjà fournie** dans
   `Framework.Data/Migrations` et la fabrique de conception
   `Framework.Data/Design/DesignTimeDbContextFactory.cs` lit ces réglages : sur
   un poste neuf, après l'assistant, une seule commande et rien à écrire —
   ```text
   dotnet tool install --global dotnet-ef --version 9.0.10
   dotnet ef migrations list   --project Framework.Data    # contrôle
   dotnet ef database update   --project Framework.Data    # applique + trigger
   ```
   Nouvelle table du produit ? `dotnet ef migrations add AjoutEleves --project
   Framework.Data --startup-project Framework.WPF`, puis dans la migration :
   `migrationBuilder.Sql(SyncNotifySql.Script("framework_sync", new[] { "Eleves" }));`
   **Attention** : `migrations remove` + `migrations add` écrase le SQL du
   trigger ajouté à la main dans `Initial` — le remettre dans la foulée, sinon
   les postes ne se notifieront plus rien et rien ne le dira à l'écran.
   Sans `--project`, l'outil cherche les migrations dans le projet de démarrage
   et l'applications hôte (Hosting) échoue à charger `App.xaml` : préciser
   `Framework.Data`.
4. **30 min — premier écran.** Copier les **trois fichiers déjà livrés** plutôt
   que de les réinventer : `Framework.Data/Entities/ExempleEntity.cs` (+ sa
   configuration et son `DbSet`), `Framework.WPF/ViewModels/ExempleViewModel.cs`,
   `Framework.WPF/Views/ExempleView.xaml`. Ils portent déjà liste filtrable,
   formulaire création/modification, suppression logique et recharge temps réel
   sur `ExternalEntityChangedMessage` — **WeakReferenceMessenger uniquement**
   (le pont est déjà posé dans `App`). Trois branchements à refaire à la main :
   la `NavigationItem` (avec sa permission) dans `ShellViewModel._tousLesItems`,
   le `DataTemplate` dans `App.xaml`, l'enregistrement `AddTransient` du ViewModel
   dans `App.xaml.cs`.
   *Preuve que le trajet tient* : l'écran **Comptes** (`ComptesViewModel` +
   `ComptesView` + `CompteLigne`, livré en `871c7ea`) est sorti de cette copie
   sans dévier du patron, en y ajoutant ses propres gardes métier.
5. **Droits du produit.** Éditer `Framework.Core/Rbac/MatriceParDefaut.cs` (ou
   mieux : `new MatriceRoles(...)` propre au produit à la composition root) et
   déclarer les permissions dans `Droits` sous la convention `module.verbe`.
   Une écriture se garde avec `_autorisation.Exiger(...)` **dans la commande**,
   pas seulement dans la vue.
6. **Licence du poste.** L'éditeur émet le code :
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
└── Framework.Tests     net8.0-windows  207 tests xUnit — dont le rendu XAML des écrans
```

Dépendances toujours dans ce sens : `WPF → Common → Core` et `WPF → Data → Core`.
Data et Common ne se référencent jamais entre eux (jonction au composition root).

| Brique | Pièce | Rôle |
|---|---|---|
| Core | `IEntity`, `IAuditableEntity`, `ISoftDeletableEntity` | conventions de domaine reconnues par le contexte |
| Core | `IGenericRepository<T>`, `IUnitOfWork` | dépôts génériques + frontière d'écriture unique |
| Core | `SchemaNonInstalleException` | « base joignable, tables absentes » n'est **pas** une panne réseau : diagnostic distinct, message porteur, sans aucun élément de connexion |
| Core | `ISyncNotificationService`, `SyncNotificationDto` | contrat temps réel inter-postes |
| Core | `INavigationService`, `IThemeService`, `IAuthService`, `IHorloge`, `ISettingsService`, `IAlertService`, `ILicenseService` | transverses sans référence UI/DB |
| Core | `Droits`, `RoleDefini`, `IMatriceRoles` / `MatriceRoles` | **RBAC générique** : permissions `module.verbe`, sauvageonne `*`, héritage aplati sans cycle, **rôle inconnu = zéro droit** |
| Core | `MatriceParDefaut` | trois rôles de départ (Administrateur / Utilisateur / Lecteur) : le produit remplace, il ne commente pas |
| Core | `IAutorisationService`, `AutorisationRefuseeException` | droits de la session courante : `Peut()` pour la vue, `Exiger()` pour l'écriture |
| Core | `PasswordHasher` | PBKDF2-SHA256 150 000 itérations, temps constant |
| Data | `FrameworkDbContext` | clés = `Id`, audit au SaveChanges, DELETE → suppression logique + filtre global, configurations par assembly |
| Data | `GenericRepository<T>`, `UnitOfWork` | contexte privé par UoW, `ExecuteInTransactionAsync` sous `CreateExecutionStrategy` |
| Data | `PostgresConnectionResolver` | chaîne de connexion **source unique**, `Actualiser()` à chaud après assistant, test par SQLSTATE en message français, `GarantirBaseAsync` (whitelist du nom de base) |
| Data | `PostgresSyncListener` | LISTEN/NOTIFY hors pool, battement de cœur 30 s, backoff 1→30 s, anti-écho `sender` |
| Data | `SyncNotifySql` | fonction plpgsql + trigger AFTER I/U/D par table |
| Data | `Design/DesignTimeDbContextFactory` + `Migrations/` | **migration `Initial` fournie** (table `Comptes` + trigger de synchro) : l'outil EF lit les réglages de l'assistant, même chaîne d'anti-écho |
| Data | `ExempleEntity` + `ExempleEntityConfiguration` | table de démonstration du CRUD (libellé, catégorie, montant, note) — à supprimer avec l'écran « Exemples » |
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
| WPF | `ThemeService` / `NavigationService` / `ShellViewModel` | thème à chaud + accent persisté ; pile de types résolue en DI ; sidebar **filtrée par les droits de la session**, recalculée à chaque changement de personne |
| WPF | `ConnectionConfigWindow` | **assistant réseau autonome** : test, création de base, enregistrement — premier écran d'un poste neuf |
| WPF | `DroitsViewModel` + `DroitsView` | écran « Rôles et permissions » : la matrice déroulée, la session courante marquée, les rôles déclarés qui ne mènent à aucun écran signalés, et la ligne « Porté par : 1 compte : stage » qui rattache chaque rôle aux comptes du poste sous `comptes.consulter` (refus → « comptes non listés ici », jamais un « 0 » inventé) |
| WPF | `ExempleViewModel` + `ExempleView` | **écran CRUD de référence** : liste filtrable, formulaire, suppression logique, Unit of Work par opération, recharge sur notification d'un autre poste, **boutons grisés par le droit ET par l'état** |
| WPF | `ComptesViewModel` + `ComptesView` + `CompteLigne` | **écran de gestion des comptes** (EF-11) : créer un compte, changer un rôle, désactiver, lever un gel — sans SQL. Empreinte de mot de passe jamais remontée à la vue, liste de rôles fermée par la matrice, identifiant dupliqué refusé avant l'index unique, et **impossibilité de verrouiller sa propre session** (rôle, désactivation, suppression du compte courant refusés) |
| Tests | `Framework.Tests` | hachage, chiffrement (fichier + DPAPI), licence/HWID complète, réglages, CSV/PDF, resolver + assistant, `AuthService` (gel inclus) sur EF InMemory, fabrique de conception (migrations), écran CRUD de référence, **matrice RBAC + `AutorisationService` + gates des écrans** (dont l'écran Comptes : empreinte seule à la création, gardes du compte courant, gel, suppression logique) |
| CI | `.github/workflows/framework-ci.yml` | windows-latest : Release build + 207 tests sur tout push touchant le framework |

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
- **Une vue n'est jamais une sécurité** : `Peut()` grise le bouton, `Exiger()`
  refuse l'écriture dans la commande. Toute écriture du produit appelle les deux.
- **Les droits se déclarent en code** (`MatriceParDefaut` ou la matrice du
  produit) : un rôle lu en base que la matrice ne connaît pas donne zéro droit,
  jamais un droit par défaut.
- **Un écran ne se verrouille jamais lui-même** : un écran
  d'administration refuse la désactivation, la suppression et le changement de
  rôle du compte qui tient la session courante. Le garde se lit sur
  `IAuthService.CurrentUser` à l'exécution, jamais sur la ligne affichée —
  sinon l'exploitant peut scier la branche qui lui donne accès à l'écran.
- **« Vérifié » veut dire écran ouvert, pas build vert** : toute vue nouvelle
  ou modifiée est rendue par `ViewRenduTests` (thread STA, XAML réel, DataContext
  réel, `Measure`/`Arrange`/`UpdateLayout` forcés) **et** jouée à l'écran par
  l'exploitant avant d'être annoncée terminée. Un `XamlParseException` au rendu
  ne quitte jamais le terminal.

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
- **`AuthService` est singleton, pas transient** : il porte `CurrentUser` et
  l'événement de session. En transient, le `ShellViewModel` (singleton) voyait
  une autre instance que celle de la connexion — chip utilisateur vide et
  **tous les droits refusés**, y compris pour un Administrateur.
- `EnableRetryOnFailure` interdit les transactions « à la main » : passer par
  `ExecuteInTransactionAsync`.
- Le **`sender` d'une notification est l'`Application Name`** (Npgsql rend la clé
  avec un espace) de la connexion émettrice : `BuildConnectionString` est
  obligatoire, y compris pour l'outil de migration.
- Les **`PackageReference` sont épinglées** sur les versions du dépôt hôte
  (EF 9.0.10, Npgsql 9.0.4, QuestPDF 2026.2.4, xunit 2.6.2).
- **Npgsql 9.0.4 tire EF Core 9.0.1** par transitoire : épingler
  `Microsoft.EntityFrameworkCore` + `.Relational` en **9.0.10** dans
  `Framework.Data`, sinon `CS1705`/`MSB3277` dès qu'un projet consomme Design.
- **`Microsoft.EntityFrameworkCore.Design` vit dans `Framework.Data`** (l'assembly
  des migrations), jamais dans `Framework.WPF` : `PrivateAssets=all` n'empêche
  pas la DLL de **fuiter dans la sortie de l'app** par le biais du projet de
  tests, et l'outil EF croit alors que le projet de démarrage porte les
  migrations (`Your target project 'FrameworkDemo' doesn't match your migrations
  assembly 'Framework.Data'`).
- Toujours passer **`--project Framework.Data`** à `dotnet ef` : sans lui, l'outil
  instancie le `DbContext` hors DI (échec `DbContextOptions` non résolu) et
  essaie de monter l'hôte générique de `App.xaml.cs`, qui plante sur la
  `ResourceDictionary` WPF en conception.
- La fabrique de conception **relit `settings.json`, ne le duplique pas** : sans
  réglages, elle refuse (`InvalidOperationException` en français) au lieu de
  générer une migration sur une base inventée. `Framework.Data` ne peut pas
  référencer `Framework.Common` (cycle) — d'où son lecteur de réglages interne,
  en lecture seule.
- **Une propriété d'état que le code-behind surveille doit lever
  `PropertyChanged`.** `EstConfigure` était un simple auto-property : la
  fermeture automatique de l'assistant réseau était du code mort, et le poste
  restait bloqué sur la fenêtre *alors que* les réglages étaient écrits. Aucun
  test ne le voyait, car le chemin heureux exige un serveur joignable. Depuis,
  la finalisation est isolée dans `Finaliser(prelude)` et se teste sans réseau.
- **Un `catch (Exception)` large transforme une base vide en panne réseau.**
  `42P01: la relation « Comptes » n'existe pas` levait dans le bloc générique de
  `PreparerAsync` : l'écran envoyait l'exploitant reconfigurer un poste
  parfaitement câblé. Le statut PostgreSQL est donc traduit en
  `SchemaNonInstalleException` côté `AuthService`, et l'écran de connexion tient
  un message distinct. Ne jamais y faire transiter la chaîne de connexion.
- **Une base créée par l'assistant est vide.** Le framework ne fait jamais
  `EnsureCreated` : sans `dotnet ef database update --project Framework.Data`,
  il n'y a ni `Comptes`, ni trigger de synchro — et donc aucun T3 possible.
  L'assistant le dit maintenant à l'enregistrement.
- **Écrire l'état de fin de commande *avant* le rechargement du `finally`**
  rend le message invisible. Le patron « recharger dans le `finally` » terminait
  sur « n lignes affichées » et écrasait « 1 compte désactivé » : le bouton
  avait bien travaillé, l'écran affirmait le contraire. D'où `resultat` local +
  `clore(resultat)` appelé **après** le rechargement. Le même aveuglement reste
  dans `ExempleViewModel`, non corrigé ici parce que sa relecture est reportée
  par l'exploitant.
- **Un écran d'administration ne doit pas pouvoir se couper sa propre branche** :
  sans garde, un Administrateur peut se rétrograder, se désactiver ou se
  supprimer depuis sa propre session — et perd l'accès à l'écran qui aurait
  servi à réparer. Les trois gardes sont écrites dans les commandes, pas
  seulement grisées dans la vue, et lues sur `IAuthService.CurrentUser`.
- **Doublure d'identifiant à vérifier avant l'index unique** : un `username`
  déjà pris levait une contrainte PostgreSQL dont la traduction restait
  générique. `FindAsync(c => c.Username == username)` en excluant sa propre
  ligne donne une phrase sur laquelle l'exploitant peut agir.
- **Une liste que la vue ne voit pas changer est un bouton mort** :
  `DroitsViewModel.Roles` était une propriété `get/set` simple. Le bouton
  « Recharger » recalculait la matrice — le test le vérifiait sur la valeur —
  sans jamais notifier, donc `ItemsSource` gardait la liste à l'écran. Depuis,
  la liste est en `[ObservableProperty]` et le test écoute `PropertyChanged`.
  Comparer une propriété ne prouve pas que l'interface se repeint. Récidive
  évitée de la même façon pour `NoteComptes`, qui passe par
  `OnNoteComptesChanged` plutôt que par un `OnPropertyChanged` manuel.
- **Une propriété en lecture seule liée en TwoWay fait sauter l'écran, build
  vert compris** : `Run.Text` est TwoWay *par défaut*. Posé sur `Porteurs`
  (propriété calculée, sans setter), il levait `XamlParseException` — « Une
  liaison TwoWay ou OneWayToSource ne peut pas fonctionner sur la propriété en
  lecture seule » — à l'ouverture de Rôles et permissions, avec un Release à
  0 erreur et 200 tests au vert : les tests de ViewModel n'appellent que le
  ViewModel, jamais le XAML. Corrigé en `3e60455` par `Mode=OneWay` explicite,
  et `ViewRenduTests` rend désormais les vraies vues (sept écrans) pour que la
  classe de défaut ne revienne pas par un autre chemin.
- **Rendre une vue WPF dans un test a ses trois pièges** : (1) thread STA avec
  Dispatcher, sinon la construction de la vue lève avant le XAML ; (2) les
  ressources du poste — `Application.ResourceAssembly` refuse d'être changée
  une fois définie, donc `App.InitializeComponent()` résout ses URI relatives
  contre l'assembly de lancement (`Framework.Tests`) et échoue : le hote
  recompose les dictionnaires en `pack://application:,,,/{assembly};component/…` ;
  (3) une vue qui pète emporte la boucle du Dispatcher — `UnhandledException`
  avec `Handled = true`, l'exception étant gardée pour le test qui la subit,
  sinon les tests suivants attendent 30 s une opération qui ne passe jamais.
  Les `Window` ne se montrent jamais : on met en page leur `Content`, un
  `Show()` enverrait une fenêtre sur le bureau de l'exploitant.

## 4. Ce que le cadre laisse au produit

- **Sa matrice de rôles** : les trois rôles du socle et les permissions
  `exemples.*` / `rapports.*` / `comptes.gerer` sont un **départ**, pas une
  politique. Le produit écrit `new MatriceRoles(...)` avec ses rôles à lui,
  ajoute ses clefs dans sa propre classe de droits (même convention
  `module.verbe`) et décide qui tient `*`. `CompteEntity.Role` reste une chaîne
  libre : c'est la matrice qui lui donne une valeur, et un rôle qu'elle ne
  connaît pas ne donne accès à rien — l'écran « Rôles et permissions » le dit.
- **Écrans CRUD** : le shell fournit la mécanique et **un écran de référence**
  (`ExempleViewModel` + `ExempleView` + `ExempleEntity` + sa ligne dans la
  migration `Initial`). Le produit le copie, le renomme, puis supprime les
  trois fichiers et la table `Exemples` dès sa première vraie table métier.
  L'écran **Comptes** n'entre pas dans ce nettoyage : il appartient au socle et
  garde les clés `comptes.consulter` / `comptes.gerer` — le produit décide qui
  les tient, pas s'il les supprime.
- **Secret de licence** : l'éditeur du produit tient SA clé HMAC et génère les
  codes d'activation avec `OfflineLicenseEngine.Emettre` (outil distributeur).

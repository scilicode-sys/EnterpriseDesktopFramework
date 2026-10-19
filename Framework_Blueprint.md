# Framework Blueprint — socle desktop .NET 8 / WPF

Squelette de démarrage **générique** pour applications de gestion multi-postes
(auto-école, salon de coiffure, stock…). Il vit dans ce dépôt mais n'est **lié à
aucun** projet NexaMed : solution propre (`Framework/Framework.sln`), zéro code
médical, zéro référence aux assemblages `MonCabinetMedical.*`.

## 1. Architecture

```
Framework/
├── Framework.sln
├── Framework.Core      net8.0          contrats + modèles purs — aucune dépendance NuGet
├── Framework.Data      net8.0          EF Core 9 + Npgsql — sans UI
├── Framework.Common    net8.0-windows  réglages JSON, AES/DPAPI, Velopack, Sentry, Growl
└── Framework.WPF       net8.0-windows  shell : hôte DI, navigation, thème, écrans
```

Règle de dépendance, toujours dans ce sens : `WPF → Common → Core` et `WPF → Data → Core`.
Core ne référence rien ; Data et Common ne se référencent pas entre eux
(au composition root seulement, dans `App.xaml.cs`).

### Ce que chaque projet fournit

| Projet | Pièces | Rôle |
|---|---|---|
| Core | `IEntity`, `IAuditableEntity`, `ISoftDeletableEntity` | conventions de domaine reconnaissables par le contexte |
| Core | `IGenericRepository<TEntity>`, `IUnitOfWork` | dépôts génériques + frontière d'écriture unique |
| Core | `ISyncNotificationService`, `SyncNotificationDto` | contrat temps réel inter-postes |
| Core | `INavigationService`, `IThemeService`, `IAuthService`, `IHorloge`, `ISettingsService`, `IAlertService` | services transverses sans référence UI/DB |
| Core | `PasswordHasher` | PBKDF2-SHA256 150 000 itérations, comparaison temps constant |
| Data | `FrameworkDbContext` | clés = `Id`, audit horodatée au SaveChanges, DELETE → suppression logique + filtre de requête global, configurations discoverées par assembly |
| Data | `GenericRepository<T>`, `UnitOfWork` | contexte privé par UoW (fabrique EF), `ExecuteInTransactionAsync` sous `CreateExecutionStrategy` (compatible `EnableRetryOnFailure`) |
| Data | `PostgresConnectionResolver` | chaîne de connexion **source unique**, `ApplicationName = framework-{MachineName}-{Pid}` (vecteur d'anti-écho), test de connexion par SQLSTATE (28P01/28000, 3D000, timeout) rendu en message français, ne lève jamais |
| Data | `PostgresSyncListener` | connexion dédiée hors pool, `LISTEN`, battement de cœur `WaitAsync(30 s)`, backoff 1 s → 30 s, anti-écho sur `sender` |
| Data | `SyncNotifySql` | fabrique du SQL : fonction plpgsql `framework_notify_sync()` + trigger AFTER I/U/D par table |
| Data | `AuthService` | session + anti-force-brute (3 échecs → gel 60 s) via `IHorloge`, amorce du premier compte |
| Common | `JsonSettingsService` | `%LocalAppData%\{AppName}\settings.json`, plat, écriture atomique, fichier cabossé = redémarrage à vide |
| Common | `EncryptionService` | clé AES-256 **aléatoire par machine** sous DPAPI (CurrentUser + entropie), chiffrement de fichiers GCM par blocs avec AAD (nonce + index), `Protect/Unprotect` pour petits blobs |
| Common | `VelopackUpdateService` | fabrique null hors installation ; **`AppliquerALaFermeture` ne s'appelle QUE depuis `OnExit`** (appelée plus tôt, `WaitExitThenApplyUpdates` tue le poste) |
| Common | `TelemetrySentry` | amorçage optionnel par DSN, seuil Error+ seulement |
| Common | `GrowlAlertService` | notifications empilées en bas à droite, pur WPF sans librairie, maréchalisation Dispatcher garantie |
| WPF | `App.xaml.cs` | composition root : `Host.CreateDefaultBuilder` + Serilog + `AddFrameworkCommon` + `AddFrameworkData`, Velopack en **toute première ligne**, pont écouteur → `WeakReferenceMessenger` |
| WPF | `ThemeService` | palettes `Light.xaml`/`Dark.xaml` échangées à chaud dans les MergedDictionaries ; accent posé en clé directe au niveau application (survit aux swaps), persisté |
| WPF | `NavigationService` | pile de **types**, instance résolue dans la DI à chaque visite, `GoBack`/`Reset` |
| WPF | `ShellViewModel` + `MainWindow` | sidebar rétractable (230 ↔ 56 px), zone de contenu = `ContentControl` sur le ViewModel courant via DataTemplate implicite |
| WPF | `LoginWindow` + `LoginViewModel` | connexion ; si table de comptes vide, le même écran devient création du premier compte |

## 2. Instancier pour un nouveau produit (objectif : 2 heures)

Exemple : « AutoEcoleDemo ». Toutes les étapes ci-dessous sont des **copies/ajouts**,
jamais des modifications du framework.

### 20 min — copier et renommer

1. Copier le dossier `Framework/` du dépôt vers le nouveau dépôt du produit.
2. Dans `Framework.WPF.csproj` : changer `AssemblyName` et `Product` (`AutoEcole`).
3. Dans `App.xaml.cs` : changer `NomApplication` (`"AutoEcole"`) — c'est le dossier
   `%LocalAppData%`/`%AppData%` de l'installation.
4. Restaurer et builder : `dotnet build Framework.sln -c Release` doit sortir à **0 erreur**.

### 40 min — le domaine

5. Créer un projet `AutoEcole.Domain` (ou un dossier) avec les entités métier,
   héritées d'`EntityBase` :
   ```csharp
   public class Eleve : EntityBase
   {
       public string Nom { get; set; } = "";
       public string Telephone { get; set; } = "";
   }
   ```
6. Créer le contexte du produit :
   ```csharp
   public class AutoEcoleDbContext : FrameworkDbContext
   {
       public AutoEcoleDbContext(DbContextOptions<AutoEcoleDbContext> options) : base(options) { }
       public DbSet<Eleve> Eleves => Set<Eleve>();

       protected override void OnModelCreating(ModelBuilder mb)
       {
           base.OnModelCreating(mb);
           mb.ApplyConfigurationsFromAssembly(typeof(AutoEcoleDbContext).Assembly);
       }
   }
   ```
   (les conventions — audit, suppression logique, filtre global — viennent du contexte de base ;
   les entités d'un **autre assembly** exigent le second `ApplyConfigurationsFromAssembly`,
   le framework ne devine pas les assemblies étrangères.)
7. Enregistrer le contexte du produit à la place du contexte de démo, dans `App.xaml.cs` :
   `services.AddFrameworkData<AutoEcoleDbContext>();`

### 30 min — migrations et synchronisation

8. Générer la migration initiale depuis le projet WPF (l'outil EF crée la base **à la main**
   au préalable — le framework ne fait jamais d'`EnsureCreated`) :
   ```
   dotnet ef migrations add Initial -p . -s .
   ```
   avec une `IDesignTimeDbContextFactory<AutoEcoleDbContext>` dans le projet pour que l'outil
   construise la chaîne de connexion hors DI.
9. Dans la migration suivante (ou un ` migrationBuilder.Sql`), poser le SQL de synchro des
   tables à partager entre postes — fabriqué par le framework :
   ```csharp
   migrationBuilder.Sql(SyncNotifySql.Script("framework_sync", new[] { "Eleves" }));
   ```
   Le nom de table est celui de `ToTable(...)` ; le canal doit être identique sur tous les
   postes du produit.
10. Démarrer le poste, créer le premier compte depuis l'écran de connexion (la table
    `Comptes` est vierge → l'écran bascule en mode amorce), puis appliquer les migrations
    sur chaque poste suivant.

### 30 min — premier écran CRUD

11. Écrire `ElevesViewModel` (`ObservableObject`) qui ouvre un `IUnitOfWork` transitoire,
    utilise `uow.Repository<Eleve>()`, et `SaveChangesAsync()`.
12. Ajouter la `NavigationItem` dans `ShellViewModel.Items`, le `DataTemplate`
    ViewModel → Vue dans `App.xaml`, et la View avec ses cartes.
13. Abonner l'écran à `ExternalEntityChangedMessage` (`IRecipient`) pour le rafraîchissement
    temps réel — en **messager faible seulement** : l'écouteur singleton ne doit jamais
    retenir un ViewModel transitoire (c'est le pont posé dans `App.OnStartup`).

## 3. Règles permanentes du framework

- **Zéro modification des postes NexaMed.** Le cadre est copié, pas partagé : aucune
  référence d'assembly entre les deux mondes, aucun `using MonCabinetMedical…`.
- **Toute écriture passe par `IUnitOfWork`.** Pas de `SaveChanges` dans un dépôt ;
  un contexte neuf par écran, jamais de contexte partagé entre ViewModels
  (piège d'`xmin`/entité suivie mesuré sur le poste hôte).
- **Le temps se lit dans `IHorloge`**, jamais `DateTime.Now` en dur : gel de compte,
  cooldowns et fenêtres anti-spam restent testables en avançant l'horloge.
- **Zéro jargon technique à l'écran.** Une panne de serveur, un refus de règle, une
  erreur technique : trois phrases françaises maximum, la trame Npgsql/EF part au
  journal Serilog, pas à l'UI.
- **Zéro secret en dur.** Les champs d'identité de `PostgresConnectionSettings` sont
  vides par conception ; l'assistant réseau du produit écrit le `settings.json` local.
  `settings.json`, `bin/`, `obj/` ne se committent jamais.
- **Un poste non installé sous Velopack n'est pas une anomalie** : le wrapper rend
  `null` et le poste tourne, sans [ERR]. L'application de la file d'attente se joue
  exclusivement dans `App.OnExit`.

## 4. Pièges mesurés, déjà désamorcés ici

- Les **usings implicites d'un projet WPF n'incluent pas `System.IO`** : `Path`, `File`,
  `Directory` et `Stream` demandent le `using` explicite (sinon CS0103/CS0246 en cascade).
- `WaitExitThenApplyUpdates(UpdateInfo, bool silent, bool restartApp)` : **paramètres
  positionnels**, les noms échouent à la compilation, et la fenêtre de 60 s impose l'appel
  en fin de fermeture uniquement.
- Une **fenêtre WPF fermée ne se rouvre pas** : les fenêtres sont `Transient` dans la DI ;
  la nouvelle est montrée **avant** que l'ancienne soit fermée (`ShutdownMode=OnLastWindowClose`).
- `PasswordBox.Password` n'est **pas une DependencyProperty** : le mot de passe arrive au
  ViewModel par le code-behind à chaque frappe, jamais par une liaison.
- Une clé de thème posée dans une seule palette reste **celle d'ouverture** sur l'autre
  thème : toute clé nouvelle doit exister dans `Light.xaml` **et** `Dark.xaml`, et se
  consommer en `DynamicResource`.
- `EnableRetryOnFailure` interdit toute transaction EF « à la main » : la transaction
  passe par `ExecuteInTransactionAsync`, qui enveloppe dans `CreateExecutionStrategy`.
- Le **`sender` d'une notification est l'`ApplicationName`** de la connexion qui a écrit :
  passer par `PostgresConnectionResolver.BuildConnectionString` est donc obligatoire,
  y compris pour l'outil de migration, sinon l'anti-écho ne reconnaît personne.

## 5. Ce que le cadre laisse volontairement au produit

- **Assistant réseau** (écran qui écrit `PostgresConnectionSettings` dans les réglages) :
  le socle fournit le resolver, le test par SQLSTATE et les messages — l'écran est le
  premier écran métier à porter depuis le poste hôte.
- **Rôles et permissions** : `CompteEntity.Role` est une chaîne libre ; la matrice
  (type `AuthorizationRules`) est la première règle métier du produit.
- **Licence / activation** : hors périmètre du cadre, domaine propre au distributeur.
- **Impression, exports, PDF** : dépendances du métier, pas du squelette.

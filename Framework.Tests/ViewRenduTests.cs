using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Framework.Core.Data;
using Framework.Core.Rbac;
using Framework.Data;
using Framework.Data.Entities;
using Framework.WPF;
using Framework.WPF.Converters;
using Framework.WPF.ViewModels;
using Framework.WPF.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Framework.Tests;

/// <summary>
/// Les deux fois où l'Exploitant a ouvert un écran cassé, le build était vert.
/// Les tests de ViewModel appellent le ViewModel : ils ne chargent jamais le XAML,
/// donc ils ne voient ni un StaticResource absent, ni un binding TwoWay posé sur
/// une propriété en lecture seule — le crash de « Porté par » (25bfcb7) est de
/// cette famille. Ces tests installent la vue réelle, son DataContext réel, et
/// forcent le passage de mise en page qui fait vivre les bindings.
/// </summary>
public sealed class ViewRenduTests : IDisposable
{
    private readonly DbContextOptions<FrameworkDbContext> _options;
    private readonly ServiceProvider _portes;
    private readonly MatriceRoles _matrice = MatriceParDefaut.Creer();

    public ViewRenduTests()
    {
        _options = new DbContextOptionsBuilder<FrameworkDbContext>()
            .UseInMemoryDatabase($"framework-rendu-{Guid.NewGuid():N}")
            .Options;

        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<FrameworkDbContext>>(new FabriqueContexte(_options));
        services.AddTransient<IUnitOfWork, UnitOfWork>();
        _portes = services.BuildServiceProvider();
    }

    public void Dispose() => _portes.Dispose();

    [Fact]
    public async Task DroitsView_Rendue_LignePorteeParChaqueRoleSAffiche()
    {
        await semerAsync(("stage", MatriceParDefaut.Lecteur, true));
        var ecran = droits(MatriceParDefaut.Administrateur);
        await ecran.ChargerAsync();

        var rendus = rendre(() => new DroitsView(), ecran);

        Assert.Contains("Porté par : 1 compte : stage", rendus);
        Assert.Contains("Porté par : aucun compte ne porte ce rôle", rendus);
    }

    [Fact]
    public async Task DroitsView_Rendue_LaHeriteduSafficheAussi()
    {
        // Les deux lignes sont des Run : le second binding du même TextBlock ne
        // sauve pas le premier si l'un des deux est mal déclaré.
        await semerAsync(("amine", MatriceParDefaut.Administrateur, true));
        var ecran = droits(MatriceParDefaut.Administrateur);
        await ecran.ChargerAsync();

        var rendus = rendre(() => new DroitsView(), ecran);

        Assert.Contains("Hérite de : Lecteur", rendus);
        Assert.Contains("Hérite de : aucun", rendus);
    }

    [Fact]
    public async Task DroitsView_Rendue_SansDroitComptes_LeDitSansInventerDeZero()
    {
        var matrice = new MatriceRoles(new[]
        {
            new RoleDefini(MatriceParDefaut.Administrateur, "Administrateur", new[] { Droits.Tout }),
            new RoleDefini("Support", "Support", new[] { Droits.DroitsConsulter }),
        });
        var session = new FakeSession("Support");
        var ecran = new DroitsViewModel(
            session, matrice, new AutorisationService(session, matrice),
            _portes.GetRequiredService<IServiceScopeFactory>());
        await ecran.ChargerAsync();

        var rendus = rendre(() => new DroitsView(), ecran);

        Assert.Contains("Porté par : comptes non listés ici", rendus);
    }

    [Fact]
    public async Task ComptesView_Rendue_LesComptesPortentLeurLigne()
    {
        await using var contexte = new FrameworkDbContext(_options);
        contexte.Comptes.Add(new CompteEntity
        {
            Username = "stage",
            NomComplet = "Stagiaire",
            Role = MatriceParDefaut.Lecteur,
            PasswordHash = "pbkdf2$sha256$depuis-le-test",
            Actif = true,
        });
        await contexte.SaveChangesAsync();

        var session = new FakeSession(MatriceParDefaut.Administrateur);
        var ecran = new ComptesViewModel(
            _portes.GetRequiredService<IServiceScopeFactory>(),
            new AlertesEnMemoire(),
            new AutorisationService(session, _matrice),
            session,
            _matrice);
        await ecran.ChargerAsync();

        var rendus = rendre(() => new ComptesView(), ecran);

        Assert.Contains("stage", rendus);
        Assert.Contains("Stagiaire", rendus);
    }

    [Fact]
    public async Task ExempleView_Rendue_laGrilleAfficheSesLignes()
    {
        await using var contexte = new FrameworkDbContext(_options);
        contexte.Exemples.Add(new ExempleEntity
        {
            Libelle = "Location de salle",
            Categorie = "Divers",
            Montant = 120m,
        });
        await contexte.SaveChangesAsync();

        var session = new FakeSession(MatriceParDefaut.Utilisateur);
        var ecran = new ExempleViewModel(
            _portes.GetRequiredService<IServiceScopeFactory>(),
            new AlertesEnMemoire(),
            new AutorisationService(session, _matrice));
        await ecran.ChargerAsync();

        var rendus = rendre(() => new ExempleView(), ecran);

        Assert.Contains("Location de salle", rendus);
    }

    [Fact]
    public void DashboardView_Rendue_leCompteurSePose()
    {
        var ecran = new DashboardViewModel(new FakeHorloge());

        var rendus = rendre(() => new DashboardView(), ecran);

        Assert.Contains("Notifications reçues d'autres postes", rendus);
        Assert.Contains("0", rendus);
    }

    [Fact]
    public void LoginWindow_Rendue_leTitreDuFormulaireSePose()
    {
        // La fenêtre est construite et son contenu mis en page, jamais montrée :
        // un Show() enverrait une fenêtre sur le bureau de l'Exploitant.
        var session = new FakeSession();
        var ecran = new LoginViewModel(session, new AlertesEnMemoire());

        var rendus = rendre(() => new LoginWindow(ecran), ecran);

        Assert.Contains("Framework Desktop — socle de démarrage générique.", rendus);
    }

    /// <summary>Le texte réellement posé sur l'écran, carte par carte.</summary>
    /// <remarks>Porteur = une vue ou une fenêtre ; une fenêtre ne se montre pas, on couche son contenu.</remarks>
    private static IReadOnlyList<string> rendre(Func<object> fabriquePorteur, object dataContext)
    {
        var rendus = new List<string>();
        Exception? echec = null;
        using var fini = new ManualResetEventSlim(false);

        // La vue ne se construit pas ici mais sur le thread STA : un UserControl créé
        // ailleurs lève avant même de lire le XAML.
        Interlocked.Exchange(ref EchecHote, (Exception?)null);

        // Le délai borne le test : un Dispatcher qui ne répond pas doit échouer,
        // pas geler la suite (ni la CI) pour l'éternité.
        Hote().BeginInvoke(new Action(() =>
        {
            try
            {
                FrameworkElement racine;
                if (fabriquePorteur() is Window fenetre)
                {
                    fenetre.DataContext = dataContext;
                    racine = (FrameworkElement)fenetre.Content;
                }
                else
                {
                    var vue = (FrameworkElement)fabriquePorteur();
                    vue.DataContext = dataContext;
                    racine = vue;
                }

                racine.Width = 900;
                racine.Height = 1400;

                // C'est la mise en page qui fabrique les conteneurs du ItemsControl et
                // active leurs bindings : sans elle, le XAML serait seulement chargé, pas vécu.
                racine.Measure(new Size(900, 1400));
                racine.Arrange(new Rect(0, 0, 900, 1400));
                racine.UpdateLayout();

                rendus.AddRange(textes(racine));
            }
            catch (Exception e)
            {
                echec = e;
            }
            finally
            {
                fini.Set();
            }
        }));

        if (!fini.Wait(TimeSpan.FromSeconds(30)))
        {
            throw new TimeoutException("Le thread WPF du test de rendu n'a pas répondu en 30 secondes.");
        }

        // Un accident survenu hors de l'opération (gabarit, passage de mise en page
        // différé) remonte quand même : c'est ce test-là qui doit échouer dessus.
        var coupable = echec ?? Interlocked.Exchange(ref EchecHote, null);
        if (coupable is not null)
        {
            throw coupable;
        }

        return rendus;
    }

    private static IReadOnlyList<string> textes(DependencyObject racine)
    {
        var rendus = new List<string>();
        collecter(racine, rendus);
        return rendus;
    }

    private static void collecter(DependencyObject element, List<string> rendus)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            var enfant = VisualTreeHelper.GetChild(element, i);

            if (enfant is TextBlock bloc)
            {
                var contenu = bloc.Inlines.Count > 0
                    ? string.Concat(bloc.Inlines.OfType<Run>().Select(run => run.Text))
                    : bloc.Text;

                if (!string.IsNullOrEmpty(contenu))
                {
                    rendus.Add(contenu);
                }
            }

            collecter(enfant, rendus);
        }
    }

    private DroitsViewModel droits(string role)
    {
        var session = new FakeSession(role);
        return new DroitsViewModel(
            session,
            _matrice,
            new AutorisationService(session, _matrice),
            _portes.GetRequiredService<IServiceScopeFactory>());
    }

    private async Task semerAsync(params (string Username, string Role, bool Actif)[] comptes)
    {
        await using var contexte = new FrameworkDbContext(_options);
        contexte.Comptes.AddRange(comptes.Select(c => new CompteEntity
        {
            Username = c.Username,
            NomComplet = $"Titulaire {c.Username}",
            Role = c.Role,
            PasswordHash = "pbkdf2$sha256$depuis-le-test",
            Actif = c.Actif,
        }));
        await contexte.SaveChangesAsync();
    }

    /// <summary>
    /// Un écran WPF ne se construit pas n'importe où : le XAML exige un thread STA
    /// avec un Dispatcher, et les ressources du poste (converters, thèmes, templates
    /// ViewModel-vers-vue) viennent de App.xaml. Une seule boucle pour toute la suite.
    /// </summary>
    private static Dispatcher Hote()
    {
        lock (Verrou)
        {
            if (Fil is not null)
            {
                return Fil;
            }

            // Une seule tentative par process : la deuxième instance d'Application
            // est refusée par WPF, et noierait la vraie cause sous un faux message.
            if (Echec is not null)
            {
                throw Echec;
            }

            using var pret = new ManualResetEventSlim(false);
            Dispatcher? nouveau = null;
            Exception? echec = null;

            var thread = new Thread(() =>
            {
                try
                {
                    // Le App.InitializeComponent() du poste ne peut pas servir ici :
                    // ses URI de dictionnaires sont relatives, donc WPF les résout
                    // contre l'assembly de lancement — Framework.Tests, pas la démo —
                    // et Application.ResourceAssembly refuse d'être changée une fois
                    // définie. On recompose donc les ressources du poste avec des URI
                    // absolues ; les StaticResource que les vues exigent sont les mêmes.
                    var assembly = typeof(App).Assembly.GetName().Name!;
                    var application = new Application();

                    application.Resources.MergedDictionaries.Add(ressource(assembly, "Resources/Theme/Controls.xaml"));
                    application.Resources.MergedDictionaries.Add(ressource(assembly, "Resources/Theme/Light.xaml"));

                    application.Resources.Add("BooleenVersVisibilite", new BooleenVersVisibilite());
                    application.Resources.Add("Initiales", new Initiales());
                    application.Resources.Add("TexteVersVisibilite", new TexteVersVisibilite());
                    application.Resources.Add("InverseBooleen", new InverseBooleen());
                    application.Resources.Add("LibelleModeConnexion", new LibelleModeConnexion());

                    nouveau = Dispatcher.CurrentDispatcher;
                    nouveau.UnhandledException += (_, args) =>
                    {
                        // Une vue qui pète emporterait la boucle du Dispatcher, et les
                        // tests suivants attendraient 30 secondes une opération qui ne
                        // passe jamais. L'exception est gardée de côté : c'est le test
                        // qui la subit qui échoue dessus, pas toute la suite.
                        Interlocked.CompareExchange(ref EchecHote, args.Exception, null);
                        args.Handled = true;
                    };
                    pret.Set();
                    Dispatcher.Run();
                }
                catch (Exception e)
                {
                    echec = e;
                    pret.Set();
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();

            if (!pret.Wait(TimeSpan.FromSeconds(20)))
            {
                throw new TimeoutException("L'hôte WPF des tests de rendu n'a pas démarré.");
            }

            if (echec is not null)
            {
                Echec = echec;
                throw echec;
            }

            Fil = nouveau!;
            return Fil;
        }
    }

    private static readonly object Verrou = new();
    private static Dispatcher? Fil;
    private static Exception? Echec;
    private static Exception? EchecHote;

    /// <summary>Un dictionnaire de thème du poste, adressé par assembly explicite.</summary>
    private static ResourceDictionary ressource(string assembly, string chemin)
        => new()
        {
            Source = new Uri($"pack://application:,,,/{assembly};component/{chemin}", UriKind.Absolute),
        };

    private sealed class FabriqueContexte : IDbContextFactory<FrameworkDbContext>
    {
        private readonly DbContextOptions<FrameworkDbContext> _options;

        public FabriqueContexte(DbContextOptions<FrameworkDbContext> options) => _options = options;

        public FrameworkDbContext CreateDbContext() => new(_options);
    }

    private sealed class AlertesEnMemoire : Framework.Core.Notifications.IAlertService
    {
        public List<string> Erreurs { get; } = new();

        public void Notify(Framework.Core.Notifications.AlertKind kind, string message)
        {
            if (kind == Framework.Core.Notifications.AlertKind.Error)
            {
                Erreurs.Add(message);
            }
        }
    }
}

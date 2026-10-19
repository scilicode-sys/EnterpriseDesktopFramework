using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Framework.Core.Notifications;
using Microsoft.Extensions.Logging;

namespace Framework.Common.Notifications;

/// <summary>
/// Service d'alertes empilées (« growl ») sans dépendance à une librairie de
/// contrôles : chaque notification est une petite fenêtre WPF sans cadre,
/// empilée en bas à droite, qui s'auto-ferme. Appelé depuis n'importe quel
/// fil : la maréchalisation sur le Dispatcher est garantie ici.
/// Une erreur technique ne passe JAMAIS par ce service avec sa trame brute :
/// l'appelant fournit une phrase française.
/// </summary>
public sealed class GrowlAlertService : IAlertService, IDisposable
{
    private const double LargeurToast = 380;
    private const double MargeEcran = 16;
    private const double Espacement = 8;
    private static readonly TimeSpan DureeAffichage = TimeSpan.FromSeconds(4);

    private static readonly Dictionary<AlertKind, Brush> CouleursParKind = new()
    {
        [AlertKind.Info] = Enderer(Color.FromRgb(0x2D, 0x8C, 0xEF)),
        [AlertKind.Success] = Enderer(Color.FromRgb(0x2E, 0xAD, 0x6B)),
        [AlertKind.Warning] = Enderer(Color.FromRgb(0xE8, 0xA3, 0x3D)),
        [AlertKind.Error] = Enderer(Color.FromRgb(0xD6, 0x45, 0x45)),
    };

    private readonly ILogger<GrowlAlertService>? _logger;
    private readonly List<ToastWindow> _actives = new();

    public GrowlAlertService(ILogger<GrowlAlertService>? logger = null)
    {
        _logger = logger;
    }

    public void Notify(AlertKind kind, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var application = Application.Current;

        if (application is null)
        {
            // Pas de boucle WPF (outil de ligne de commande, test) : la trace suffit.
            _logger?.LogWarning("Alerte {Kind} sans interface : {Message}", kind, message);
            return;
        }

        application.Dispatcher.BeginInvoke(() => Afficher(kind, message));
    }

    private void Afficher(AlertKind kind, string message)
    {
        try
        {
            var toast = new ToastWindow(kind, message, CouleursParKind[kind]);

            toast.Closed += (_, _) =>
            {
                if (_actives.Remove(toast))
                {
                    Reorganiser();
                }
            };

            _actives.Add(toast);
            toast.Show();
            Reorganiser();

            var horloge = new DispatcherTimer { Interval = DureeAffichage };
            horloge.Tick += (_, _) =>
            {
                horloge.Stop();
                toast.FermerSansDelai();
            };
            horloge.Start();
        }
        catch (Exception ex)
        {
            // Une alerte qui ne peut pas s'afficher ne doit jamais casser l'action
            // qui la produisait : on la consigne au journal et on passe.
            _logger?.LogError(ex, "Échec d'affichage d'une alerte {Kind} : {Message}", kind, message);
        }
    }

    /// <summary>Le plus récent en bas ; la pile monte de bas en haut.</summary>
    private void Reorganiser()
    {
        var zone = SystemParameters.WorkArea;
        var bas = zone.Bottom - MargeEcran;

        foreach (var toast in _actives)
        {
            toast.Positionner(zone.Right - MargeEcran - LargeurToast, bas - toast.Height);
            bas -= toast.Height + Espacement;
        }
    }

    public void Dispose()
    {
        foreach (var toast in _actives.ToArray())
        {
            toast.FermerSansDelai();
        }

        _actives.Clear();
    }

    private static Brush Enderer(Color couleur)
    {
        var brosse = new SolidColorBrush(couleur);
        brosse.Freeze();
        return brosse;
    }

    /// <summary>Petite fenêtre de notification, construite en code (aucune XAML à porter).</summary>
    private sealed class ToastWindow : Window
    {
        private const double PaddingVertical = 18;
        private const double HauteurMinimale = 40;

        public ToastWindow(AlertKind kind, string message, Brush fond)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            Focusable = false;
            ShowActivated = false;
            Width = LargeurToast;

            var texte = new TextBlock
            {
                Text = message,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
            };

            // La hauteur est calculée à la construction : le positionnement de la
            // pile se joue avant le premier rendu, ActualHeight ne serait pas encore utile.
            texte.Measure(new Size(LargeurToast - 24, double.PositiveInfinity));
            Height = Math.Max(HauteurMinimale, Math.Ceiling(texte.DesiredSize.Height) + PaddingVertical);

            Content = new Border
            {
                Background = fond,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 9, 12, 9),
                Child = texte,
            };

            MouseLeftButtonDown += (sender, args) => Close();

            Title = $"Alerte {kind}";
        }

        public void Positionner(double gauche, double haut)
        {
            Left = gauche;
            Top = Math.Max(SystemParameters.WorkArea.Top, haut);
        }

        public void FermerSansDelai()
        {
            try
            {
                Close();
            }
            catch (InvalidOperationException)
            {
                // Fenêtre déjà fermée par un clic : rien à faire.
            }
        }
    }
}

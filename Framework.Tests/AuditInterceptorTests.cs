using Framework.Core.Auth;
using Framework.Core.Domain;
using Framework.Core.Rbac;
using Framework.Data;
using Framework.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Framework.Tests;

/// <summary>
/// L'audit signé : <see cref="AuditSaveChangesInterceptor"/> est le seul endroit
/// qui date ET nomme une écriture. Deux familles de défauts sont visées ici :
/// l'auteur absent (une radiation sans « qui »), et surtout la colonne d'audit
/// écrite dans le ChangeTracker APRÈS la détection des changements d'EF — sans
/// marquage explicite, la valeur est juste à l'écran et fausse en base.
/// </summary>
public sealed class AuditInterceptorTests
{
    private static readonly DateTime HeureFixe = new(2026, 3, 5, 9, 30, 0);

    private readonly DbContextOptions<FrameworkDbContext> _optionsSansAudit;
    private readonly FakeHorloge _horloge = new() { Maintenant = HeureFixe };

    public AuditInterceptorTests()
    {
        _optionsSansAudit = new DbContextOptionsBuilder<FrameworkDbContext>()
            .UseInMemoryDatabase($"framework-audit-{Guid.NewGuid():N}")
            .Options;
    }

    /// <summary>
    /// Deux sessions sur la MÊME base doivent pouvoir s'écrire l'une après
    /// l'autre : sans nom partagé, chaque option ouvre sa base InMemory à soi.
    /// </summary>
    private DbContextOptions<FrameworkDbContext> optionsAvecAudit(IAuthService session, string? nomBase = null)
        => new DbContextOptionsBuilder<FrameworkDbContext>()
            .UseInMemoryDatabase(nomBase ?? $"framework-audit-{Guid.NewGuid():N}")
            .AddInterceptors(new AuditSaveChangesInterceptor(session, _horloge))
            .Options;

    private static FakeSession session(string role, string pseudo)
    {
        var connexion = new FakeSession();
        connexion.SeConnecter(role, pseudo);
        return connexion;
    }

    [Fact]
    public async Task Création_SignéeParLaSessionCourante()
    {
        var options = optionsAvecAudit(session(MatriceParDefaut.Administrateur, "direction"));

        Guid id;
        await using (var contexte = new FrameworkDbContext(options))
        {
            var ligne = new ExempleEntity { Libelle = "Frais de scolarité", Categorie = "Divers" };
            contexte.Exemples.Add(ligne);
            await contexte.SaveChangesAsync();
            id = ligne.Id;
        }

        await using (var lecture = new FrameworkDbContext(options))
        {
            var enBase = await lecture.Exemples.SingleAsync(e => e.Id == id);

            Assert.Equal(HeureFixe, enBase.CreatedAt);
            Assert.Equal("direction", enBase.CreatedBy);
            Assert.Null(enBase.UpdatedAt);
            Assert.Null(enBase.UpdatedBy);
        }
    }

    [Fact]
    public async Task Modification_LeCréateurResteEtLeDernierÉditeurEstNommé()
    {
        var nomBase = $"framework-audit-modif-{Guid.NewGuid():N}";
        var options = optionsAvecAudit(session(MatriceParDefaut.Administrateur, "direction"), nomBase);

        Guid id;
        await using (var contexte = new FrameworkDbContext(options))
        {
            var ligne = new ExempleEntity { Libelle = "Location de salle", Categorie = "Divers" };
            contexte.Exemples.Add(ligne);
            await contexte.SaveChangesAsync();
            id = ligne.Id;
        }

        // Un autre poste, une autre personne : la note est corrigée.
        await using (var contexte = new FrameworkDbContext(
                         optionsAvecAudit(session(MatriceParDefaut.Utilisateur, "compta"), nomBase)))
        {
            var ligne = await contexte.Exemples.SingleAsync(e => e.Id == id);
            ligne.Note = "Corrigé après réclamation.";
            await contexte.SaveChangesAsync();
        }

        await using (var lecture = new FrameworkDbContext(options))
        {
            var enBase = await lecture.Exemples.SingleAsync(e => e.Id == id);

            Assert.Equal("direction", enBase.CreatedBy);
            Assert.Equal("compta", enBase.UpdatedBy);
            Assert.Equal(HeureFixe, enBase.UpdatedAt);
        }
    }

    [Fact]
    public async Task Radiation_NomméeSansSeDéguiserEnModification()
    {
        var options = optionsAvecAudit(session(MatriceParDefaut.Administrateur, "direction"));

        Guid id;
        await using (var contexte = new FrameworkDbContext(options))
        {
            var ligne = new ExempleEntity { Libelle = "Élève radié", Categorie = "Divers" };
            contexte.Exemples.Add(ligne);
            await contexte.SaveChangesAsync();
            id = ligne.Id;
        }

        await using (var contexte = new FrameworkDbContext(options))
        {
            var ligne = await contexte.Exemples.SingleAsync(e => e.Id == id);
            contexte.Exemples.Remove(ligne);
            await contexte.SaveChangesAsync();
        }

        await using (var lecture = new FrameworkDbContext(options))
        {
            // Le filtre global cache la ligne radiée : seule la relecture sans
            // filtre prouve qu'elle vit encore, avec son auteur de radiation.
            var enBase = await lecture.Exemples.IgnoreQueryFilters().SingleAsync(e => e.Id == id);

            Assert.True(enBase.IsDeleted);
            Assert.Equal(HeureFixe, enBase.DeletedAt);
            Assert.Equal("direction", enBase.DeletedBy);
            Assert.Null(enBase.UpdatedAt);
            Assert.Null(enBase.UpdatedBy);
            Assert.Equal("direction", enBase.CreatedBy);
        }
    }

    [Fact]
    public async Task HorsSession_LHeureEstPoséeMaisPersonNEstInventé()
    {
        var options = optionsAvecAudit(new FakeSession());

        Guid id;
        await using (var contexte = new FrameworkDbContext(options))
        {
            var ligne = new ExempleEntity { Libelle = "Amorce sans connexion", Categorie = "Divers" };
            contexte.Exemples.Add(ligne);
            await contexte.SaveChangesAsync();
            id = ligne.Id;
        }

        await using (var lecture = new FrameworkDbContext(options))
        {
            var enBase = await lecture.Exemples.SingleAsync(e => e.Id == id);

            Assert.Equal(HeureFixe, enBase.CreatedAt);
            Assert.Null(enBase.CreatedBy);
        }
    }

    [Fact]
    public async Task ColonneDaudit_EstMarquéeModifiée_QuandLEcriturePart()
    {
        // Le piège d'ordre de passage : SavingChanges s'exécute APRÈS la
        // détection des changements d'EF. Une propriété d'audit assignée à cet
        // instant ne rejoint pas l'UPDATE si elle n'est pas marquée modifiée.
        // InMemory écrit toutes les colonnes de toute façon : c'est donc l'état
        // du ChangeTracker, lu par un second interceptor branché APRÈS l'audit,
        // qui prouve le marquage — donc le SQL qui partira sur un vrai poste.
        var connexion = session(MatriceParDefaut.Administrateur, "direction");
        var espion = new EspionDuChangeTracker();
        var options = new DbContextOptionsBuilder<FrameworkDbContext>()
            .UseInMemoryDatabase($"framework-audit-piege-{Guid.NewGuid():N}")
            .AddInterceptors(new AuditSaveChangesInterceptor(connexion, _horloge), espion)
            .Options;

        Guid id;
        await using (var contexte = new FrameworkDbContext(options))
        {
            var ligne = new ExempleEntity { Libelle = "Note de frais", Categorie = "Divers" };
            contexte.Exemples.Add(ligne);
            await contexte.SaveChangesAsync();
            id = ligne.Id;
        }

        await using (var contexte = new FrameworkDbContext(options))
        {
            var ligne = await contexte.Exemples.SingleAsync(e => e.Id == id);
            ligne.Libelle = "Note de frais requalifiée";
            contexte.ChangeTracker.DetectChanges();

            // À cette seconde, seule la colonne visible est modifiée : l'audit
            // doit l'amener dans l'écriture, sinon la base garde l'heure du vide.
            Assert.False(contexte.Entry(ligne).Property(nameof(IAuditableEntity.UpdatedAt)).IsModified);

            await contexte.SaveChangesAsync();
        }

        Assert.True(espion.UpdatedAtMarquée);
        Assert.True(espion.UpdatedByMarquée);
        Assert.Equal("direction", espion.AuteurEnregistré);
    }

    [Fact]
    public async Task Convention_NaPlusDeSecondeSourceDansLeContexte()
    {
        // Preuve négative assumée : un contexte construit à la main sans
        // l'interceptor ne trace plus rien. Soit on passe par la DI du poste
        // (AddFrameworkData la branche), soit on la déclare — plus de magie
        // cachée dans le contexte qui pourrait contredire l'interceptor.
        Guid id;
        await using (var contexte = new FrameworkDbContext(_optionsSansAudit))
        {
            var ligne = new ExempleEntity { Libelle = "Sans interceptor", Categorie = "Divers" };
            contexte.Exemples.Add(ligne);
            await contexte.SaveChangesAsync();
            id = ligne.Id;
        }

        await using (var lecture = new FrameworkDbContext(_optionsSansAudit))
        {
            var enBase = await lecture.Exemples.SingleAsync(e => e.Id == id);

            Assert.Equal(default, enBase.CreatedAt);
            Assert.Null(enBase.CreatedBy);
        }
    }
}

/// <summary>
/// Second interceptor branché APRÈS l'audit : il ne change rien, il lit l'état
/// du ChangeTracker au moment exact où l'écriture part. C'est le seul moyen de
/// voir une colonne marquée modifiée sans dépendre d'un vrai serveur SQL.
/// </summary>
internal sealed class EspionDuChangeTracker : SaveChangesInterceptor
{
    public bool UpdatedAtMarquée { get; private set; }

    public bool UpdatedByMarquée { get; private set; }

    public string? AuteurEnregistré { get; private set; }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Espionner(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Espionner(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Espionner(DbContext? contexte)
    {
        var entry = contexte?.ChangeTracker.Entries<ExempleEntity>()
            .FirstOrDefault(e => e.State == EntityState.Modified);

        if (entry is null)
        {
            return;
        }

        UpdatedAtMarquée = entry.Property(nameof(IAuditableEntity.UpdatedAt)).IsModified;
        UpdatedByMarquée = entry.Property(nameof(IAuditableEntity.UpdatedBy)).IsModified;
        AuteurEnregistré = entry.Entity.UpdatedBy;
    }
}

using Framework.Core.Auth;
using Framework.Core.Domain;
using Framework.Core.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Framework.Data;

/// <summary>
/// L'unique endroit où une écriture est datée et signée. La convention vivait
/// dans <c>FrameworkDbContext.SaveChanges</c> : elle y écrivait l'heure mais
/// jamais <b>qui</b> — un reçu, une note ou une radiation sans auteur n'est pas
/// opposable. Elle sort donc du contexte pour devenir un interceptor, seul
/// maillon à la fois branché sur la DI (donc sur la session) et commun à tous
/// les contextes, y compris ceux d'un produit dérivé.
/// </summary>
/// <remarks>
/// Piège d'ordre de passage : <c>SavingChanges</c> s'exécute <b>après</b> la
/// détection des changements d'EF. Écrire une propriété d'audit à cet instant
/// ne la fait pas passer dans l'<c>UPDATE</c> — il faut marquer la propriété
/// modifiée à la main, sinon l'écran croit tracer et la colonne ne bouge pas.
/// Les heures viennent d'<c>IHorloge</c> (règle permanente du framework), et
/// la radiation garde son propre auteur : convertir le DELETE en UPDATE ne doit
/// pas effacer qui a rayé la ligne au profit d'une fausse « modification ».
/// </remarks>
public sealed class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly IAuthService _session;
    private readonly IHorloge _horloge;

    public AuditSaveChangesInterceptor(IAuthService session, IHorloge horloge)
    {
        _session = session;
        _horloge = horloge;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        AppliquerConvention(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        AppliquerConvention(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void AppliquerConvention(DbContext? contexte)
    {
        if (contexte is null)
        {
            return;
        }

        var maintenant = _horloge.Maintenant;
        var auteur = _session.CurrentUser?.Username;

        foreach (var entry in contexte.ChangeTracker.Entries())
        {
            switch (entry.State)
            {
                case EntityState.Added when entry.Entity is IAuditableEntity ajoutee:
                    ajoutee.CreatedAt = maintenant;
                    ajoutee.CreatedBy = auteur;
                    break;

                case EntityState.Modified when entry.Entity is IAuditableEntity modifiee:
                    modifiee.UpdatedAt = maintenant;
                    modifiee.UpdatedBy = auteur;
                    MarquerModifiee(entry, nameof(IAuditableEntity.UpdatedAt), nameof(IAuditableEntity.UpdatedBy));
                    break;

                case EntityState.Deleted when entry.Entity is ISoftDeletableEntity supprimee:
                    entry.State = EntityState.Modified;
                    supprimee.IsDeleted = true;
                    supprimee.DeletedAt = maintenant;
                    supprimee.DeletedBy = auteur;

                    // La radiation ne se déguise pas en modification : UpdatedAt et
                    // UpdatedBy restent tels qu'ils étaient, c'est « qui a rayé »
                    // que l'on veut relire plus tard.
                    MarquerModifiee(
                        entry,
                        nameof(ISoftDeletableEntity.IsDeleted),
                        nameof(ISoftDeletableEntity.DeletedAt),
                        nameof(ISoftDeletableEntity.DeletedBy));
                    break;
            }
        }
    }

    /// <summary>
    /// Marque les colonnes d'audit comme modifiées. Une colonne absente du modèle
    /// est une faute de convention dans le produit : elle doit lever, pas se taire.
    /// </summary>
    private static void MarquerModifiee(EntityEntry entry, params string[] proprietes)
    {
        foreach (var propriete in proprietes)
        {
            entry.Property(propriete).IsModified = true;
        }
    }
}

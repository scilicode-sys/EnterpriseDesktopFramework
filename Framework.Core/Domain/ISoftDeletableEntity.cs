namespace Framework.Core.Domain;

/// <summary>
/// Entité effaçable sans perte de données : la suppression logique remplace
/// le DELETE physique (converti par <c>AuditSaveChangesInterceptor</c>).
/// </summary>
public interface ISoftDeletableEntity
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }

    /// <summary>Identifiant de connexion qui a radié la ligne — la question qu'un contrôle pose toujours.</summary>
    string? DeletedBy { get; set; }
}

namespace Framework.Core.Domain;

/// <summary>
/// Entité dont le cycle de vie est horodaté **et signé** par l'interceptor
/// d'audit : qui a créé, qui a modifié. Les horodatages ne sortent plus du
/// contexte, ils sortent d'<c>AuditSaveChangesInterceptor</c>.
/// </summary>
public interface IAuditableEntity
{
    DateTime CreatedAt { get; set; }
    DateTime? UpdatedAt { get; set; }

    /// <summary>Identifiant de connexion de la session qui a créé la ligne ; null si la ligne est née hors session.</summary>
    string? CreatedBy { get; set; }

    /// <summary>Identifiant de connexion de la session qui a le dernier modifié la ligne.</summary>
    string? UpdatedBy { get; set; }
}

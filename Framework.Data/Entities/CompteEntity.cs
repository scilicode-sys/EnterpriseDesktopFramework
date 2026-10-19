namespace Framework.Data.Entities;

/// <summary>
/// Compte de connexion générique du framework (aucun métier attaché).
/// Le champ Rôle est une chaîne libre : c'est le projet dérivé qui définit
/// ses rôles et sa matrice de permissions.
/// </summary>
public class CompteEntity : EntityBase
{
    public string Username { get; set; } = string.Empty;

    public string NomComplet { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public bool Actif { get; set; } = true;

    public int EchecsConsecutifs { get; set; }

    public DateTime? GelJusqua { get; set; }
}

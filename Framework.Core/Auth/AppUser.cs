namespace Framework.Core.Auth;

/// <summary>Compte de connexion générique, sans métier attaché.</summary>
public sealed record AppUser(
    Guid Id,
    string Username,
    string NomComplet,
    string Role,
    bool IsActive);

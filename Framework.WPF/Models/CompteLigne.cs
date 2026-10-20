namespace Framework.WPF.Models;

/// <summary>
/// Ligne de l'écran « Comptes ». L'empreinte de mot de passe n'y figure pas :
/// la liste affiche un état de compte, jamais une matière à secret.
/// </summary>
public sealed record CompteLigne(
    Guid Id,
    string Username,
    string NomComplet,
    string Role,
    bool Actif,
    int EchecsConsecutifs,
    DateTime? GelJusqua,
    DateTime? ModifieLe,
    bool EstCompteCourant)
{
    public string Etat => Actif ? "Actif" : "Désactivé";

    /// <summary>Ce que l'anti-force-brute laisse voir : le gel en cours, jamais le hash.</summary>
    public string Securite => GelJusqua is { } gel
        ? $"Gel jusqu'à {gel:HH:mm:ss}"
        : EchecsConsecutifs > 0
            ? $"{EchecsConsecutifs} échec{(EchecsConsecutifs > 1 ? "s" : "")} consécutif{(EchecsConsecutifs > 1 ? "s" : "")}"
            : "—";

    public string DerniereModification => ModifieLe?.ToString("dd/MM/yyyy HH:mm") ?? "—";

    /// <summary>Marque la ligne du poste : c'est elle que les gardes anti-verrouillage protègent.</summary>
    public string Marque => EstCompteCourant ? "(session de ce poste)" : string.Empty;
}

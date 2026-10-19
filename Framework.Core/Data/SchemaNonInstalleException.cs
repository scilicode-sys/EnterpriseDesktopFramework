namespace Framework.Core.Data;

/// <summary>
/// La base est joignable mais son contenu n'a jamais été installé : les tables
/// du schéma manquent. À ne pas confondre avec une panne réseau — le poste qui
/// la subit doit être migré, pas reconfiguré.
/// </summary>
public sealed class SchemaNonInstalleException : Exception
{
    public SchemaNonInstalleException(string? nomBase)
        : base($"La base « {nomBase ?? "courante"} » est joignable mais ses tables sont absentes. "
            + "Installez le schéma par la migration EF sur ce poste, puis relancez l'application.")
    {
        NomBase = nomBase;
    }

    /// <summary>Nom de la base visée, sans aucun élément de connexion.</summary>
    public string? NomBase { get; }
}

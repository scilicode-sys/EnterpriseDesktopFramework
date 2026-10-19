using System.Globalization;

namespace Framework.Data.Postgres;

/// <summary>
/// Fabrique du SQL d'installation du canal de synchronisation : une fonction
/// plpgsql partagée + un trigger AFTER par table. À rejouer dans une migration
/// EF Core pour chaque table que le projet veut synchroniser entre postes.
/// </summary>
public static class SyncNotifySql
{
    /// <summary>Fonction d'notif commune : le « sender » est l'Application Name de la connexion émettrice.</summary>
    public static string FonctionTrigger(string channel = "framework_sync") => string.Format(
        CultureInfo.InvariantCulture,
        """
        CREATE OR REPLACE FUNCTION framework_notify_sync() RETURNS trigger
        LANGUAGE plpgsql AS $fn$
        DECLARE
            v_id text;
            v_op text := substring(TG_OP from 1 for 1);
        BEGIN
            IF TG_OP = 'DELETE' THEN
                v_id := OLD."Id"::text;
            ELSE
                v_id := NEW."Id"::text;
            END IF;

            PERFORM pg_notify('{0}',
                json_build_object(
                    'table',  TG_TABLE_NAME,
                    'id',     v_id,
                    'op',     v_op,
                    'sender', current_setting('application_name')
                )::text);

            RETURN NULL;
        END
        $fn$;
        """,
        channel);

    /// <summary>Trigger AFTER INSERT/UPDATE/DELETE sur une table (nom exact, tel que ToTable l'a posé).</summary>
    public static string TriggerTable(string table) => string.Format(
        CultureInfo.InvariantCulture,
        """
        CREATE TRIGGER "TX_sync_{0}" AFTER INSERT OR UPDATE OR DELETE ON "{0}"
        FOR EACH ROW EXECUTE FUNCTION framework_notify_sync();
        """,
        table);

    /// <summary>Script complet : fonction + un trigger par table fournie.</summary>
    public static string Script(string channel, IEnumerable<string> tables)
    {
        var lignes = new List<string> { FonctionTrigger(channel) };
        lignes.AddRange(tables.Select(TriggerTable));
        return string.Join("\n\n", lignes);
    }
}

using System;
using System.Collections;
using System.Data;
using System.IO;
using System.Globalization;
using Mono.Data.Sqlite;

public static class UmaKeySync
{
    public static string Url = "https://umapi.buenavista.fans:8443/keys.txt";

    public static bool NeedsSync(string dbPath)
    {
        using var connection = new SqliteConnection("Data Source=" + dbPath);
        connection.Open();
        using var probe = new SqliteCommand("SELECT COUNT(*) FROM pragma_table_info('a') WHERE name='e'", connection);
        if (Convert.ToInt64(probe.ExecuteScalar()) == 0) return true;
        probe.CommandText = "SELECT COUNT(*) FROM a WHERE e IS NOT NULL AND e<>0";
        return Convert.ToInt64(probe.ExecuteScalar()) == 0;
    }

    public static IEnumerator Ensure(string dbPath, Action<bool> completed, Action<string, UIMessageType> progress = null)
    {
        progress?.Invoke("Downloading Windows resource keys", UIMessageType.Default);
        string payload = null;
        yield return UmaViewerDownload.DownloadText(Url, text => payload = text, 30);
        bool success = false;
        try
        {
            if (string.IsNullOrWhiteSpace(payload)) throw new IOException("Windows key service is unavailable.");
            int applied = Apply(dbPath, payload);
            if (applied == 0 || NeedsSync(dbPath)) throw new InvalidDataException("The key table did not match any resource hashes.");
            progress?.Invoke($"Applied keys to {applied} resources", UIMessageType.Default);
            success = true;
        }
        catch (Exception ex)
        {
            progress?.Invoke("Resource key synchronization failed: " + ex.Message, UIMessageType.Error);
        }
        completed?.Invoke(success);
    }

    // Match by the platform-specific content hash; never infer keys from names.
    public static int Apply(string dbPath, string payload)
    {
        using var connection = new SqliteConnection("Data Source=" + dbPath);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = new SqliteCommand("SELECT COUNT(*) FROM pragma_table_info('a') WHERE name='e'", connection, transaction);
        if (Convert.ToInt64(command.ExecuteScalar()) == 0)
        {
            command.CommandText = "ALTER TABLE a ADD COLUMN e INTEGER NOT NULL DEFAULT 0";
            command.ExecuteNonQuery();
        }
        command.CommandText = "CREATE TEMP TABLE resource_keys (h TEXT PRIMARY KEY, e INTEGER NOT NULL)";
        command.ExecuteNonQuery();
        command.CommandText = "INSERT OR REPLACE INTO resource_keys(h,e) VALUES (@h,@e)";
        var hashParameter = command.Parameters.Add("@h", DbType.String);
        var keyParameter = command.Parameters.Add("@e", DbType.Int64);
        using (var lines = new StringReader(payload))
        {
            string line;
            while ((line = lines.ReadLine()) != null)
            {
                int tab = line.IndexOf('\t');
                if (tab <= 0 || !long.TryParse(line.Substring(tab + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long key) || key == 0) continue;
                hashParameter.Value = line.Substring(0, tab).Trim();
                keyParameter.Value = key;
                command.ExecuteNonQuery();
            }
        }
        command.Parameters.Clear();
        command.CommandText = "UPDATE a SET e=(SELECT e FROM resource_keys WHERE resource_keys.h=a.h) WHERE h IN (SELECT h FROM resource_keys)";
        int applied = command.ExecuteNonQuery();
        transaction.Commit();
        return applied;
    }
}

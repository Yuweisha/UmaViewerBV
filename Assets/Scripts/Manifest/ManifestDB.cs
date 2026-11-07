using Mono.Data.Sqlite;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using static BSVReader;
using static ManifestCategory;

public class ManifestDB : IDisposable
{
    public SqliteConnection MetaDB;
    public static string DBPath;
    public static string RootUrl = "https://umapi.buenavista.fans:8443/api/MetaC/root";
    public Action<string, UIMessageType> callback;
    private bool isError;
    public bool Succeeded { get; private set; }
    private readonly string finalMetaPath;
    private readonly string stagedMasterPath;

    public void Dispose()
    {
        MetaDB?.Dispose();
        if (File.Exists(DBPath)) File.Delete(DBPath);
        if (File.Exists(stagedMasterPath)) File.Delete(stagedMasterPath);
    }

    public ManifestDB()
    {
        finalMetaPath = Path.Combine(Config.Instance.MainPath, "meta_umaviewer");
        DBPath = finalMetaPath + ".building." + Guid.NewGuid().ToString("N");
        stagedMasterPath = Path.Combine(Config.Instance.MainPath, "master", "master_umaviewer.mdb.building");
        if (!File.Exists(DBPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DBPath));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(DBPath));
        MetaDB = new SqliteConnection($@"Data Source={DBPath};Pooling=False");
        MetaDB.Open();
        using (var settings = new SqliteCommand("PRAGMA journal_mode=MEMORY; PRAGMA synchronous=OFF; PRAGMA cache_size=-65536;", MetaDB)) settings.ExecuteNonQuery();
        using SqliteCommand cmd = new SqliteCommand("SELECT name FROM sqlite_master");
        cmd.Connection = MetaDB;
        using var reader = cmd.ExecuteReader();
        var hasrows = reader.HasRows;
        reader.Close();
        if (!hasrows) //Is Empty DB
        {
            using SqliteTransaction tran = MetaDB.BeginTransaction();
            cmd.CommandText = "CREATE TABLE `a` ( `i` INTEGER PRIMARY KEY,`n` TEXT NOT NULL,`d` TEXT,`g` INTEGER(4) NOT NULL,`l` INTEGER(8) NOT NULL,`c` INTEGER(8) NOT NULL,`h` TEXT NOT NULL,`m` TEXT NOT NULL,`k` INTEGER(1) NOT NULL,`s` INTEGER(1) NOT NULL,`p` INTEGER(4) NOT NULL DEFAULT 0,`e` INTEGER(8) NOT NULL DEFAULT 0)";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "CREATE INDEX `a0` ON `a`(`n`, `s`)";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "CREATE INDEX `a2` ON `a`(`m`, `s`)";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "CREATE INDEX `a3` ON `a`(`g`, 's')";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "CREATE INDEX `a4` ON `a`(`p`)";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "CREATE INDEX `a1` ON `a`(`s`)";
            cmd.ExecuteNonQuery();
            tran.Commit();
        }
    }

    public IEnumerator UpdateResourceVersion(Action<string, UIMessageType> callback)
    {
        isError = false;
        Succeeded = false;
        this.callback = callback;
        callback?.Invoke("Checking Resource Version", UIMessageType.Default);
        yield return UpdateMetaDB();
        if (!isError)
        {
            bool synced = false;
            yield return UmaKeySync.Ensure(DBPath, result => synced = result, callback);
            isError = !synced;
        }
        if (!isError) yield return UpdateMasterDB();
        MetaDB.Close();
        if (!isError)
        {
            try
            {
                File.Copy(stagedMasterPath, Path.Combine(Config.Instance.MainPath, "master", "master_umaviewer.mdb"), true);
                File.Copy(DBPath, finalMetaPath, true);
                Succeeded = true;
            }
            catch (Exception ex)
            {
                isError = true;
                callback?.Invoke("Publishing rebuilt databases failed: " + ex.Message, UIMessageType.Error);
            }
        }
        callback?.Invoke(isError ? "Standalone rebuild failed. See the preceding stage error." : "Databases and resource keys rebuilt successfully.", isError ? UIMessageType.Error : UIMessageType.Success);
    }

    //umamusume uses the manifest file to generate meta database.
    //it needs to get the root manifest file first
    //then read the root manifest to get other manifest file information
    //recursively read the manifest file to obtain all resource files information
    //finally, insert all data into the meta database
    public IEnumerator UpdateMetaDB()
    {
        string rootHash = null;
        callback?.Invoke("Getting resource version", UIMessageType.Default);
        yield return null;
        string cachePath = Path.Combine(Config.Instance.MainPath, "standalone-root.json");
        string payload = null;
        yield return UmaViewerDownload.DownloadText(RootUrl, txt => payload = txt, 30);
        rootHash = ParseRootHash(payload);
        if (rootHash != null)
        {
            try { File.WriteAllText(cachePath, payload); }
            catch (IOException ex) { Debug.LogWarning("Could not cache root: " + ex.Message); }
        }
        else if (File.Exists(cachePath))
        {
            rootHash = ParseRootHash(File.ReadAllText(cachePath));
            callback?.Invoke("Root service unavailable; trying cached resource version", UIMessageType.Default);
        }

        if (rootHash == null)
        {
            callback?.Invoke("Get RootHash Error", UIMessageType.Error);
            Debug.LogError("Get RootHash Error");
            isError = true;
            yield break;
        }

        ManifestEntry rootEntry = new ManifestEntry(Encoding.UTF8.GetBytes("root"), null, 0, 0, 0, 0, "", 3);
        rootEntry.hname = rootHash;
        yield return UpdateMetaDB(rootEntry);
    }

    public IEnumerator UpdateMetaDB(ulong size, ulong checksum)
    {
        ManifestEntry rootEntry = new ManifestEntry(Encoding.UTF8.GetBytes("root"), null, 0, 0, size, checksum, "", 3);
        rootEntry.hname = rootEntry.CalHameString();
        yield return UpdateMetaDB(rootEntry);
    }

    public IEnumerator UpdateMetaDB(ManifestEntry rootEntry)
    {
        if (MetaDB == null || MetaDB.State != System.Data.ConnectionState.Open)
        {
            Debug.LogError("Open Meta Database Error");
            isError = true;
            yield break;
        }

        using SqliteCommand command = new SqliteCommand(MetaDB);

        //Insert root
        int index = 1;
        UpdateManifestEntry(index, "manifest3", rootEntry, command);

        //Insert platform
        callback?.Invoke("Reading Platform Manifest", UIMessageType.Default);
        yield return null;
        ManifestEntry[] platformEntrys = null;
        yield return GetManifest(rootEntry.hname, Kind.PlatformManifest, entrys => { platformEntrys = entrys; });
        if (platformEntrys == null)
        {
            Debug.LogError("Read PlatformManifest Error");
            isError = true;
            MetaDB.Close();
            yield break;
        }
        ManifestEntry platformEntry = GetLocalPlatformEntry(platformEntrys);
        index++;
        UpdateManifestEntry(index, "manifest2", platformEntry, command);

        //Insert AssetManifest
        callback?.Invoke($"Reading Assets Manifest", UIMessageType.Default);
        yield return null;
        ManifestEntry[] assetEntrys = null;
        yield return GetManifest(platformEntry.hname, Kind.AssetManifest, entrys => { assetEntrys = entrys; });
        if (assetEntrys == null)
        {
            Debug.LogError("Read AssetManifest Error");
            isError = true;
            MetaDB.Close();
            yield break;
        }
        using (SqliteTransaction tran = MetaDB.BeginTransaction())
        {
            for (int i = 0; i < assetEntrys.Length; i++)
            {
                index++;
                UpdateManifestEntry(index, "manifest", assetEntrys[i], command);
            }
            tran.Commit();
        }

        //Insert Asset
        foreach (var assetEntry in assetEntrys)
        {
            ManifestEntry[] assets = null;
            yield return GetManifest(assetEntry.hname, Kind.Default, entrys => { assets = entrys; });
            if (assets == null)
            {
                Debug.LogError("Read AssetManifest Error");
                isError = true;
                callback?.Invoke("Resource manifest failed: " + assetEntry.tname, UIMessageType.Error);
                yield break;
            }
            callback?.Invoke($"Reading AssetBundles Manifest : {assetEntry.tname}", UIMessageType.Default);
            yield return null;
            for (int start = 0; start < assets.Length; start += 2000)
            {
                using var tran = MetaDB.BeginTransaction();
                command.Transaction = tran;
                for (int i = start; i < Math.Min(start + 2000, assets.Length); i++)
                {
                    index++;
                    UpdateManifestEntry(index, assetEntry.tname, assets[i], command);
                }
                tran.Commit();
                command.Transaction = null;
                yield return null;
            }
        }
    }

    public IEnumerator UpdateMasterDB()
    {
        if (MetaDB == null || MetaDB.State != System.Data.ConnectionState.Open)
        {
            Debug.LogError("Open Meta Database Error");
            isError = true;
            yield break;
        }

        var masterPath = stagedMasterPath;
        var masterDir = Path.GetDirectoryName(masterPath);

        callback?.Invoke($"Checking master.mdb", UIMessageType.Default);
        yield return null;
        using SqliteCommand command = new SqliteCommand("SELECT h FROM a WHERE n LIKE 'master.mdb.lz4'", MetaDB);
        using var reader = command.ExecuteReader();

        if (reader.Read())
        {
            yield return null;
            var hash = reader.GetString(0);
            var path = GetManifestPath(hash);
            if (!File.Exists(path))
            {
                callback?.Invoke($"Downloading master.mdb", UIMessageType.Default);
                yield return null;
                byte[] data = null;
                string error = null;
                yield return UmaViewerDownload.DownloadBytes(UmaViewerDownload.GetGenericRequestUrl(hash), (responseData, responseError) =>
                {
                    data = responseData;
                    error = responseError;
                });

                if (data != null)
                {
                    File.WriteAllBytes(path, data);
                }
                else
                {
                    Debug.LogError("Download Master.mdb Failed :" + error);
                    callback?.Invoke("Download Master.mdb Failed :" + error, UIMessageType.Error);
                    isError = true;
                    yield break;
                }
            }

            callback?.Invoke($"Decompress master.mdb", UIMessageType.Default);
            yield return null;
            try
            {
                Directory.CreateDirectory(masterDir);
                var bytes = LZ4Util.DecompressFromFile(path);
                File.WriteAllBytes(masterPath, bytes);
            }
            catch (Exception e)
            {
                Debug.LogError($"Decompress master.mdb Error: {e.Message}");
                callback?.Invoke($"Decompress master.mdb Error", UIMessageType.Error);
                isError = true;
                yield break;
            }
        }
        else
        {
            isError = true;
            callback?.Invoke("master.mdb.lz4 is missing from the resource manifest", UIMessageType.Error);
        }
        reader.Close();
    }

    private void UpdateManifestEntry(int index, string type, ManifestEntry entry, SqliteCommand command)
    {
        command.CommandText = "INSERT OR REPLACE INTO a (i,n,d,g,l,c,h,m,k,s,p) VALUES (@id,@name,@deps,@group,@length,@check,@hash,@m,@k,@s,@p)";
        command.Parameters.Clear();
        command.Parameters.Add(new SqliteParameter("@id", index));
        command.Parameters.Add(new SqliteParameter("@name", (type.Contains("manifest") ? "//" : "") + entry.tname));
        command.Parameters.Add(new SqliteParameter("@deps", entry.tdeps));
        command.Parameters.Add(new SqliteParameter("@group", entry.group));
        command.Parameters.Add(new SqliteParameter("@length", (long)entry.size));
        command.Parameters.Add(new SqliteParameter("@check", (long)entry.checksum));
        command.Parameters.Add(new SqliteParameter("@hash", entry.hname));
        command.Parameters.Add(new SqliteParameter("@m", type));
        command.Parameters.Add(new SqliteParameter("@k", (byte)entry.kind));
        command.Parameters.Add(new SqliteParameter("@s", 1));
        command.Parameters.Add(new SqliteParameter("@p", entry.priority));
        command.ExecuteNonQuery();
    }

    public static ManifestEntry[] GetManifest(string path, bool isCompressed, Kind kind)
    {
        using IBSVReader reader = Init(path, isCompressed);
        if (reader == null) return null;

        ulong count = reader.GetRowCount();
        ManifestEntry[] manifests = new ManifestEntry[count];
        for (ulong i = 0; i < count; i++)
        {
            manifests[i].kind = (int)kind;
            if (kind == Kind.Default)
            {
                if (!reader.ReadLine(ManifestEntry.GetBsvParser(ManifestEntry.Format.Full, reader), ref manifests[i]))
                {
                    Debug.LogError("ReadLineError");
                    return null;
                }
            }
            else
            {
                if (!reader.ReadLine(ManifestEntry.GetBsvParser(ManifestEntry.Format.Simplified, reader), ref manifests[i]))
                {
                    Debug.LogError("ReadLineError");
                    return null;
                }

            }
        }
        return manifests;

    }

    public IEnumerator GetManifest(string hash, Kind kind, Action<ManifestEntry[]> callback)
    {
        var path = GetManifestPath(hash);
        if (!File.Exists(path))
        {
            this.callback?.Invoke($"Downloading Manifest :{hash}", UIMessageType.Default);
            yield return null;
            yield return DownloadManifest(hash);
        }
        if (File.Exists(path))
        {
            yield return null;
            ManifestEntry[] entries = null;
            try { entries = GetManifest(path, true, kind); }
            catch (Exception ex)
            {
                isError = true;
                this.callback?.Invoke($"Manifest parsing failed ({hash}): {ex.Message}", UIMessageType.Error);
            }
            callback.Invoke(entries);
        }
    }

    public IEnumerator DownloadManifest(string hash)
    {
        var url = UmaViewerDownload.GetManifestRequestUrl(hash);
        byte[] data = null;
        string error = null;
        yield return UmaViewerDownload.DownloadBytes(url, (responseData, responseError) =>
        {
            data = responseData;
            error = responseError;
        }, 15);

        if (data != null)
        {
            File.WriteAllBytes(GetManifestPath(hash), data);
        }
        else
        {
            Debug.LogError($"Download Manifest Failed :{url} {error}");
            callback?.Invoke($"Download Manifest Failed :{error}", UIMessageType.Error);
            isError = true;
            yield break;
        }
    }

    public string GetManifestPath(string hash, bool createPath = true)
    {
        string path = $"{Path.GetDirectoryName(DBPath)}/dat/{hash.Substring(0, 2)}";
        if (createPath)
        {
            Directory.CreateDirectory(path);
        }
        return path + $"/{hash}";
    }

    public static string ParseRootHash(string payload)
    {
        try
        {
            var root = JObject.Parse(payload ?? "");
            var hash = (string)root["h"];
            return (string)root["n"] == "//root" && System.Text.RegularExpressions.Regex.IsMatch(hash ?? "", "^[A-Z2-7]{32}$") ? hash : null;
        }
        catch (Newtonsoft.Json.JsonException) { return null; }
    }

    public static ManifestEntry GetLocalPlatformEntry(ManifestEntry[] platformEntrys)
    {
        string platform;
#if UNITY_ANDROID
        platform = "Android";
#elif UNITY_IOS || UNITY_IPHONE || UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        platform = "IOS";
#else
        platform = "Windows";
#endif
        foreach (var entry in platformEntrys)
            if (entry.tname == platform) return entry;
        throw new InvalidDataException("Manifest does not contain platform " + platform);
    }
}

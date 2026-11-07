using System;
using System.Collections.Generic;
using Mono.Data.Sqlite;
using System.Data;
using System.IO;
using Debug = UnityEngine.Debug;

public partial class UmaDatabaseController
{
    private static UmaDatabaseController instance;

    /// <summary>
    /// Database Loader instance
    /// </summary>
    public static UmaDatabaseController Instance
    {
        get
        {
            if (instance == null)
            {
                instance = new UmaDatabaseController();
            }
            return instance;
        }
    }
    public string LoadedMetaPath { get; private set; }
    public string LoadedMasterPath { get; private set; }

    public static bool TrySelectDatabasePaths(string root, WorkMode mode, out string meta, out string master)
    {
        meta = Path.Combine(root, "meta");
        master = Path.Combine(root, "master", "master.mdb");
        if (File.Exists(meta) && File.Exists(master)) return true;
        if (mode != WorkMode.Standalone) return false;
        meta = Path.Combine(root, "meta_umaviewer");
        master = Path.Combine(root, "master", "master_umaviewer.mdb");
        return File.Exists(meta) && File.Exists(master);
    }

    public static void ResetInstance()
    {
        instance?.CloseAllConnection();
        instance = null;
    }

    /// <summary> 3d/chara/body/ </summary>
    public static string BodyPath = "3d/chara/body/";
    /// <summary> 3d/chara/mini/body/ </summary>
    public static string MiniBodyPath = "3d/chara/mini/body/";
    /// <summary> 3d/chara/head/ </summary>
    public static string HeadPath = "3d/chara/head/";
    /// <summary> 3d/motion/ </summary>
    public static string MotionPath = "3d/motion/";
    /// <summary> 3d/chara/ </summary>
    public static string CharaPath = "3d/chara/";
    /// <summary> 3d/effect/ </summary>
    public static string EffectPath = "3d/effect/";
    /// <summary> outgame/dress/ </summary>
    public static string CostumePath = "outgame/dress/";

    public Dictionary<string, UmaDatabaseEntry> MetaEntries;
    public List<DataRow> CharaData;
    public List<DataRow> MobCharaData;
    public List<FaceTypeData> FaceTypeData;
    public List<DataRow> LiveData;
    public string ReadLiveAuthor(int musicId)
        => ReadLiveTitleText(17, musicId);
    public string ReadLiveTitle(int musicId)
        => ReadLiveTitleText(16, musicId);
    public string ReadLiveFlashCharacterName(int characterId,bool isMob)
        => ReadLiveTitleText(isMob?59:6,characterId);
    public Gallop.Live.LiveFlashRaceInfo ReadLatestChampionsLiveRaceInfo()
    {
        // The standalone viewer has no saved race result. Use a consistent
        // master-data event for its preview, rather than a fixed historical ID
        // that the installed Flash resource no longer supports.
        var rows=ReadMaster(masterDb,"SELECT resource_id,start_date FROM champions_schedule ORDER BY start_date DESC LIMIT 1");
        if(rows.Count==0)throw new InvalidDataException("Champions schedule is missing from master data.");
        return new Gallop.Live.LiveFlashRaceInfo {
            ChampionsMeetingResourceId=Convert.ToInt32(rows[0]["resource_id"]),
            DateYear=DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(rows[0]["start_date"])).Year
        };
    }
    string ReadLiveTitleText(int category, int musicId)
    {
        // MasterLiveData.LiveData.get_Author 3eed320 selects text category17.
        // MasterString.String.ctor 525bde0 unescapes the SQL text first.
        var rows = ReadMaster(masterDb, "SELECT text FROM text_data WHERE category=" + category + " AND `index`=" + musicId);
        return rows.Count == 0 ? string.Empty : System.Text.RegularExpressions.Regex.Unescape(Convert.ToString(rows[0]["text"]));
    }
    public List<DataRow> DressData;
    List<DataRow> liveRunPersonalityRows;
    public Gallop.Live.Cutt.LiveRunMotionIdentity ReadLiveRunMotionIdentity(CharaEntry character)
    {
        if(character.IsMob)
        {
            var mob=MobCharaData.Find(r=>Convert.ToInt32(r["mob_id"])==character.Id);
            return new Gallop.Live.Cutt.LiveRunMotionIdentity(Convert.ToInt32(mob["race_running_type"]),Convert.ToInt32(mob["race_personality"]));
        }
        var chara=CharaData.Find(r=>Convert.ToInt32(r["id"])==character.Id);
        // Native GetRaceRunMotionPersonality -> MasterCharaType.Get:
        // scene1/cut1 ordered list, then first chara_id + target_type0 match.
        if(liveRunPersonalityRows==null)liveRunPersonalityRows=ReadMaster(masterDb,"SELECT id,chara_id,target_type,value FROM chara_type WHERE target_scene=1 AND target_cut=1 ORDER BY target_scene ASC");
        var personality=liveRunPersonalityRows.Find(r=>Convert.ToInt32(r["chara_id"])==character.Id&&Convert.ToInt32(r["target_type"])==0);
        return new Gallop.Live.Cutt.LiveRunMotionIdentity(Convert.ToInt32(chara["race_running_type"]),personality!=null?Convert.ToInt32(personality["value"]):1);
    }
    public List<DataRow> ReadStoryLivePositions(int musicId, int setId)
    {
        return ReadMaster(masterDb,"SELECT * FROM story_live_position WHERE music_id=" + musicId +
            " AND set_id=" + setId + " ORDER BY position_id");
    }

    public UnityEngine.Vector2 ReadEyeHighlightInterpolate(int animationId)
    {
        var rows = ReadMaster(masterDb, "SELECT in_time,out_time FROM highlight_interpolate WHERE id=" + animationId);
        if (rows.Count == 0) return UnityEngine.Vector2.zero;
        // Original GetInterpolateTime -> FinishTimeInt2Float multiplies by 0.0001f.
        return new UnityEngine.Vector2(Convert.ToInt32(rows[0]["in_time"]) * 0.0001f,
            Convert.ToInt32(rows[0]["out_time"]) * 0.0001f);
    }

    public string[] ReadLiveRandomEarMotionNames(Gallop.EarType type)
    {
        return ReadMaster(masterDb,"SELECT motion_name FROM random_ear_tail_motion WHERE parts_type=1 AND ear_type="+
            (int)type+" ORDER BY id").ConvertAll(row=>Convert.ToString(row["motion_name"])).ToArray();
    }

    //从master里面取点歌机歌曲目录
    public Gallop.MasterJukeboxMusicData JukeboxMusicData;


    //修改(载入通用服装ColorSet相关)
    public List<DataRow> CharaDressColor;


    /// <summary> Meta Database Connection </summary>
    public SqliteConnection metaDb;
    /// <summary> Master Database Connection </summary>
    private SqliteConnection masterDb;
    /// <summary> Loads the file database </summary>
    public UmaDatabaseController()
    {
        try
        {
            var dbKey = Config.Instance.DBKey;
            if (Config.Instance.Region == Region.Global)
            {
                dbKey = Config.Instance.GlobalDBKey;
            }

#if UNITY_ANDROID || UNITY_IOS || UNITY_IPHONE
            EnsureMobileDatabaseFiles();

            var mobileMetaPath = Path.Combine(Config.Instance.MainPath, "meta");
            var mobileMasterPath = Path.Combine(Config.Instance.MainPath, "master", "master.mdb");

            if (!File.Exists(mobileMetaPath)) throw new FileNotFoundException("Mobile meta database not found", mobileMetaPath);
            if (!File.Exists(mobileMasterPath)) throw new FileNotFoundException("Mobile master database not found", mobileMasterPath);

            var encryptedDbKey = GenFinalKey((byte[])dbKey.Clone());
            MetaEntries = ReadMetaFromEncryptedDb(mobileMetaPath, encryptedDbKey, 3);
            masterDb = new SqliteConnection($@"Data Source={mobileMasterPath};");
#else
            if (!TrySelectDatabasePaths(Config.Instance.MainPath, Config.Instance.WorkMode, out var metaPath, out var masterPath))
                throw new FileNotFoundException("A complete meta/master database pair was not found.");
            LoadedMetaPath = metaPath;
            LoadedMasterPath = masterPath;
            Debug.Log($"[Database] meta={metaPath}; master={masterPath}");
            metaDb = new SqliteConnection($@"Data Source={metaPath};");
            masterDb = new SqliteConnection($@"Data Source={masterPath};");

            try
            {
                metaDb.Open();
                MetaEntries = ReadMeta(metaDb);
            }
            catch (Exception)
            {
                try
                {
                    var dbPath = LoadedMetaPath;
                    var key = GenFinalKey((byte[])dbKey.Clone());
                    MetaEntries = ReadMetaFromEncryptedDb(dbPath, key, 3);
                }
                catch (Exception)
                {
                    throw;
                }
            }
#endif


            masterDb.Open();
            CharaData = ReadCharaMaster(masterDb);
            MobCharaData = ReadMobCharaMaster(masterDb);
            FaceTypeData = ReadFaceTypeData(masterDb);
            LiveData = ReadAllLiveData(masterDb);
            DressData = ReadAllDressData(masterDb);
            JukeboxMusicData = new Gallop.MasterJukeboxMusicData(masterDb);


            //修改(载入通用服装ColorSet相关)
            CharaDressColor = ReadAllCharaDressColor(masterDb);


#if UNITY_STANDALONE_WIN
            if (!Config.Instance.RegionDetectionPassed)
            {
                SetDefaultGameRegion(masterDb);
            }
#endif
        }
        catch (Exception ex)
        {
            Debug.LogError("Database initialization failed: " + ex);
            CloseAllConnection();
#if UNITY_ANDROID || UNITY_IOS || UNITY_IPHONE
            var msg = $"Mobile database initialization failed at: {Config.Instance.MainPath}\n{ex.GetType().Name}: {ex.Message}";
            WriteMobileDatabaseErrorLog(ex);
#else
            var msg = $"Database not found at: {Config.Instance.MainPath}";
            msg += "\n" + ex.Message;

#endif
            UmaViewerUI.Instance?.ShowMessage(msg, UIMessageType.Error);
        }
    }

    private static void WriteMobileDatabaseErrorLog(Exception ex)
    {
#if UNITY_ANDROID || UNITY_IOS || UNITY_IPHONE
        try
        {
            var logPath = Path.Combine(Config.Instance.MainPath, "MobileDatabaseInitError.txt");
            File.WriteAllText(logPath, ex.ToString());
        }
        catch
        {
        }
#endif
    }

    private static void EnsureMobileDatabaseFiles()
    {
        Directory.CreateDirectory(Config.Instance.MainPath);
        CopyBuiltinDatabaseToFile("Database/meta", Path.Combine(Config.Instance.MainPath, "meta"));
        CopyBuiltinDatabaseToFile("Database/master.mdb", Path.Combine(Config.Instance.MainPath, "master", "master.mdb"));
    }

    private static void CopyBuiltinDatabaseToFile(string resourcePath, string targetPath)
    {
        var asset = UnityEngine.Resources.Load<UnityEngine.TextAsset>(resourcePath);
        if (asset == null)
        {
            throw new FileNotFoundException($"Built-in database resource not found: Resources/{resourcePath}.bytes");
        }

        try
        {
            var bytes = asset.bytes;
            var target = new FileInfo(targetPath);
            if (target.Exists && target.Length == bytes.LongLength)
            {
                MarkNoBackup(targetPath);
                return;
            }

            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
                MarkNoBackup(directory);
            }

            var tempPath = targetPath + ".tmp";
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            File.WriteAllBytes(tempPath, bytes);
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
            File.Move(tempPath, targetPath);
            MarkNoBackup(targetPath);
        }
        finally
        {
            UnityEngine.Resources.UnloadAsset(asset);
        }
    }

    private static void MarkNoBackup(string path)
    {
#if UNITY_IOS || UNITY_IPHONE
        UnityEngine.iOS.Device.SetNoBackupFlag(path);
#endif
    }



    //修改(载入通用服装ColorSet相关)
    static List<DataRow> ReadAllCharaDressColor(SqliteConnection conn)
    {
        SqliteCommand sqlite_cmd = conn.CreateCommand();
        sqlite_cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='chara_dress_color_set'";
        SqliteDataReader sqlite_datareader = sqlite_cmd.ExecuteReader();
        bool HasTable = sqlite_datareader.HasRows;

        if (!HasTable)
        {
            return new List<DataRow>();
        }

        return ReadMaster(conn, "SELECT * FROM chara_dress_color_set");
    }



    static Dictionary<string, UmaDatabaseEntry> ReadMeta(SqliteConnection conn)
    {
        bool HasEncryption = false;
        SqliteCommand sqlite_cmd = conn.CreateCommand();
        sqlite_cmd.CommandText = "SELECT name FROM pragma_table_info('a') WHERE name = 'e'";
        SqliteDataReader sqlite_datareader = sqlite_cmd.ExecuteReader();
        HasEncryption = sqlite_datareader.HasRows;
        sqlite_datareader.Close();

        sqlite_cmd = conn.CreateCommand();
        if (HasEncryption)
        {
            sqlite_cmd.CommandText = "SELECT m,n,h,d,e FROM a WHERE d IS NOT NULL";
        }
        else
        {
            sqlite_cmd.CommandText = "SELECT m,n,h,d FROM a WHERE d IS NOT NULL"; //filter out manifest entries, whitch have null prerequisites
        }
        sqlite_datareader = sqlite_cmd.ExecuteReader();
        Dictionary<string, UmaDatabaseEntry> meta = new Dictionary<string, UmaDatabaseEntry>();
        while (sqlite_datareader.Read())
        {
            UmaDatabaseEntry entry = null;
            try
            {
                var typestr = sqlite_datareader.GetString(0);
                if (Enum.TryParse<UmaFileType>(typestr, false, out var type))
                {
                    var name = sqlite_datareader.GetString(1);
                    var url = sqlite_datareader.GetString(2);
                    var prerequisites = sqlite_datareader.GetString(3);
                    var key = 0L;

                    if (HasEncryption)
                    {
                        key = sqlite_datareader.IsDBNull(4) ? 0L : sqlite_datareader.GetInt64(4);
                    }

                    if (string.IsNullOrEmpty(name))
                    {
                        throw new Exception($"Invalid entry name {name} or URL {url}");
                    }

                    entry = new UmaDatabaseEntry
                    {
                        Type = type,
                        Name = name,
                        Url = url,
                        Prerequisites = prerequisites,
                        Key = key
                    };
                }
                else
                {
                    Debug.LogWarning($"Unrecognized EntryType Enum Value :{typestr}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error caught while reading Entry : \n {e}");
            }

            if (entry != null && !meta.ContainsKey(entry.Name))
            {

                meta.Add(entry.Name, entry);
            }
        }
        sqlite_datareader.Close();
        return meta;
    }

    public static Dictionary<string, UmaDatabaseEntry> ReadMetaFromEncryptedDb(string dbPath, byte[] keyBytes, int cipherIndex = -1)
    {
        var meta = new Dictionary<string, UmaDatabaseEntry>(StringComparer.Ordinal);
        IntPtr db = IntPtr.Zero;

        try
        {
            db = Sqlite3MC.Open(dbPath);

            if (cipherIndex >= 0)
            {
                try
                {
                    int cfgRc = Sqlite3MC.MC_Config(db, "cipher", cipherIndex);
                }
                catch (Exception e)
                {
                    Debug.LogError($"MC_Config thrown: {e}");
                }
            }

            int rcKey = Sqlite3MC.Key_SetBytes(db, keyBytes);
            if (rcKey != Sqlite3MC.SQLITE_OK)
            {
                string em = Sqlite3MC.GetErrMsg(db);
                throw new InvalidOperationException($"sqlite3_key returned rc={rcKey}, errmsg={em}");
            }

            if (!Sqlite3MC.ValidateReadable(db, out string validateErr))
            {
                Debug.LogError($"DB validation after key failed: {validateErr}");
                throw new InvalidOperationException("DB validation after key failed: " + validateErr);
            }

            string sql = "SELECT m,n,h,c,d,e FROM a";
            Sqlite3MC.ForEachRow(sql, db, (stmt) =>
            {
                try
                {
                    string m = Sqlite3MC.ColumnText(stmt, 0);
                    string n = Sqlite3MC.ColumnText(stmt, 1);
                    string h = Sqlite3MC.ColumnText(stmt, 2);
                    string c = Sqlite3MC.ColumnText(stmt, 3);
                    string d = Sqlite3MC.ColumnText(stmt, 4);
                    long e = Sqlite3MC.ColumnInt64(stmt, 5);

                    if (string.IsNullOrEmpty(m))
                    {
                        Debug.LogWarning("Skipping row: empty type string (m).");
                        return;
                    }

                    if (!Enum.TryParse<UmaFileType>(m, /*ignoreCase*/ false, out UmaFileType type))
                    {
                        Debug.LogWarning($"Unrecognized EntryType Enum Value :{m}");
                        return;
                    }

                    if (string.IsNullOrEmpty(n))
                    {
                        Debug.LogError($"Invalid entry name '{n}' or URL '{h}'. Skipping row.");
                        return;
                    }

                    var entry = new UmaDatabaseEntry
                    {
                        Type = type,
                        Name = n,
                        Url = h,
                        Checksum = c,
                        Prerequisites = d,
                        Key = e
                    };

                    if (!meta.ContainsKey(entry.Name))
                    {
                        meta.Add(entry.Name, entry);
                    }
                }
                catch (Exception exRow)
                {
                    Debug.LogError("Error caught while reading row: " + exRow);
                }
            });
        }
        catch (Exception ex)
        {
            Debug.LogError("ReadMetaFromEncryptedDb failed: " + ex);
            throw;
        }
        finally
        {
            if (db != IntPtr.Zero)
            {
                try { Sqlite3MC.Close(db); }
                catch (Exception e) { Debug.LogError("Closing DB failed: " + e); }
            }
        }

        return meta;
    }

    static void SetDefaultGameRegion(SqliteConnection conn)
    {
        var str = ReadMaster(conn, "SELECT text FROM text_data LIMIT 1");
        //check if first character of the first text_data entry is in English
        bool isAscii = str[0][0].ToString()[0] < 128;
        Region detectedRegion = isAscii ? Region.Global : Region.Jp;

        if (Config.Instance.Region != detectedRegion)
        {
            if (detectedRegion == Region.Global)
                Popup.Create($"Different game region was detected. Current region is {Config.Instance.Region}.\nDo you want to change it? (It can be changed again in `Other` settings)", -1, 200,
                    "Set to Global (Steam)", () =>
                    {
                        Config.Instance.RegionDetectionPassed = true;
                        Config.Instance.Region = detectedRegion;
                        Config.Instance.UpdateConfig(false);
                        UmaViewerUI.Instance.OtherSettings.ApplySettings();
                        UmaViewerUI.Instance.StartCoroutine(UmaViewerUI.Instance.ApplyGraphicsSettings());
                    },
                    "Keep Japan (DMM)", () =>
                    {
                        Config.Instance.RegionDetectionPassed = true;
                        Config.Instance.UpdateConfig(false);
                    });
            else
            {
                Popup.Create($"Different game region was detected. Current region is {Config.Instance.Region}.\nDo you want to change it? (It can be changed again in `Other` settings)", -1, 200,
                    "Set to Japan (DMM)", () =>
                    {
                        Config.Instance.RegionDetectionPassed = true;
                        Config.Instance.Region = detectedRegion;
                        Config.Instance.UpdateConfig(false);
                        UmaViewerUI.Instance.OtherSettings.ApplySettings();
                        UmaViewerUI.Instance.StartCoroutine(UmaViewerUI.Instance.ApplyGraphicsSettings());
                    },
                    "Keep Global (Steam)", () =>
                    {
                        Config.Instance.RegionDetectionPassed = true;
                        Config.Instance.UpdateConfig(false);
                    });
            }
        }
    }

    static List<DataRow> ReadCharaMaster(SqliteConnection conn)
    {
        return ReadMaster(conn, "SELECT * FROM chara_data C,(SELECT D.'index' charaid,D.'text' charaname FROM text_data D WHERE id like 6) T WHERE C.id like T.charaid");
    }

    static List<DataRow> ReadMobCharaMaster(SqliteConnection conn)
    {
        return ReadMaster(conn, "SELECT * FROM mob_data M,(SELECT D.'index' charaid,D.'text' charaname FROM text_data D WHERE id like 59) T WHERE M.mob_id like T.charaid");
    }

    static List<DataRow> ReadMaster(SqliteConnection conn, string sql)
    {
        List<DataRow> dr = new List<DataRow>();

        using (var sqlite_cmd = conn.CreateCommand())
        {
            sqlite_cmd.CommandText = sql;
            using var sqlite_datareader = sqlite_cmd.ExecuteReader();
            var result = new DataTable();
            for (int i = 0; i < sqlite_datareader.FieldCount; i++)
            {
                result.Columns.Add(sqlite_datareader.GetName(i), sqlite_datareader.GetFieldType(i));
            }
            while (sqlite_datareader.Read())
            {
                DataRow row = result.NewRow();
                for (int i = 0; i < sqlite_datareader.FieldCount; i++)
                {
                    row[i] = sqlite_datareader[i];
                }
                dr.Add(row);
            }
        }
        return dr;
    }

    static List<FaceTypeData> ReadFaceTypeData(SqliteConnection conn)
    {
        List<FaceTypeData> data = new List<FaceTypeData>();
        SqliteCommand sqlite_cmd = conn.CreateCommand();
        sqlite_cmd.CommandText = "SELECT * FROM face_type_data";
        SqliteDataReader sqlite_datareader = sqlite_cmd.ExecuteReader();
        while (sqlite_datareader.Read())
        {
            FaceTypeData entry = new FaceTypeData()
            {
                label = sqlite_datareader.GetString(0),
                eyebrow_l = sqlite_datareader.GetString(1),
                eyebrow_r = sqlite_datareader.GetString(2),
                eye_l = sqlite_datareader.GetString(3),
                eye_r = sqlite_datareader.GetString(4),
                mouth = sqlite_datareader.GetString(5),
                mouth_shape_type = sqlite_datareader.GetInt32(6),
                inverce_face_type = sqlite_datareader.GetString(7),
                set_face_group = sqlite_datareader.GetInt32(8),
            };
            data.Add(entry);
        }
        return data;
    }

    static List<DataRow> ReadAllLiveData(SqliteConnection conn)
    {
        return ReadMaster(conn, $"SELECT * FROM live_data L,(SELECT D.'index' songid,D.'text' songname FROM text_data D WHERE id like 16) T WHERE L.music_id like T.songid");
    }

    static List<DataRow> ReadAllDressData(SqliteConnection conn)
    {
        return ReadMaster(conn, $"SELECT * FROM dress_data C,(SELECT D.'index' dressid,D.'text' dressname FROM text_data D WHERE id like 14) T WHERE C.id like T.dressid");
    }

    public DataRow ReadDressData(int dressId)
    {
        var rows = ReadMaster(masterDb, "SELECT * FROM dress_data WHERE id=" +
            dressId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return rows.Count > 0 ? rows[0] : null;
    }

    public static DataRow ReadCharaData(CharaEntry chara)
    {
        if (chara.IsMob)
        {
            return instance.MobCharaData.Find(e => { return Convert.ToInt32(e["mob_id"]) == chara.Id; });
        }
        return instance.CharaData.Find(e => { return Convert.ToInt32(e["id"]) == chara.Id; });
    }

    // MasterLiveData.IsExistSongData / MasterLivePermissionData.IsPlayChara.
    public List<int> ReadSongEligibleCharacters(int musicId)
    {
        var live = LiveData.Find(x => Convert.ToInt32(x["music_id"]) == musicId);
        if (live == null) return new List<int>();
        int type = Convert.ToInt32(live["song_chara_type"]);
        if (type == 2)
            return ReadMaster(masterDb, $"SELECT chara_id FROM live_permission_data WHERE music_id={musicId} ORDER BY chara_id")
                .ConvertAll(x => Convert.ToInt32(x["chara_id"]));
        if (type == 1)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return CharaData.FindAll(x => Convert.ToInt32(x["id"]) > 1 && Convert.ToInt64(x["start_date"]) <= now)
                .ConvertAll(x => Convert.ToInt32(x["id"]));
        }
        return new List<int>();
    }

    public static DataRow ReadMobDressColor(string mobid)
    {
        var results = ReadMaster(instance.masterDb, $"SELECT * FROM mob_dress_color_set WHERE id LIKE {mobid}");

        return results.Count > 0 ? results[0] : null;
    }

    public static DataRow ReadMobHairColor(string colorid)
    {
        var results = ReadMaster(instance.masterDb, $"SELECT * FROM mob_hair_color_set WHERE id LIKE {colorid}");
        foreach (var data in results)
        {
            return data;
        }
        return null;
    }

    public void CloseAllConnection()
    {
        JukeboxMusicData?.Unload();
        JukeboxMusicData = null;
        masterDb?.Close();
        metaDb?.Close();
        masterDb?.Dispose();
        metaDb?.Dispose();
        SqliteConnection.ClearAllPools();
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    public byte[] GenFinalKey(byte[] key)
    {
        var baseKey = Config.Instance.DBBaseKey;
        if (baseKey.Length < 13)
            throw new Exception("Invalid Base Key length");
        for (int i = 0; i < key.Length; i++)
        {
            key[i] = (byte)(key[i] ^ baseKey[i % 13]);
        }
        return key;
    }
}

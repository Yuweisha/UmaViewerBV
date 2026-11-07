using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using UnityEngine;
using static UmaViewerUI;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;

public class UmaViewerDownload : MonoBehaviour
{
    public static string MANIFEST_ROOT_URL =
        "https://prd-storage-app-umamusume.akamaized.net/dl/resources/Manifest";

    public static string GENERIC_BASE_URL =
        "https://prd-storage-game-umamusume.akamaized.net/dl/resources/Generic";

#if UNITY_IOS || UNITY_IPHONE || UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
    public static string ASSET_BASE_URL =
        "https://prd-storage-game-umamusume.akamaized.net/dl/resources/iOS/assetbundles/";
#elif UNITY_ANDROID
    public static string ASSET_BASE_URL =
        "https://prd-storage-game-umamusume.akamaized.net/dl/resources/Android/assetbundles/";
#else
    public static string ASSET_BASE_URL =
        "https://prd-storage-game-umamusume.akamaized.net/dl/resources/Windows/assetbundles/";
#endif

    private static bool IsCnCdn
    {
        get
        {
            return Config.Instance != null &&
                   string.Equals(Config.Instance.CdnMode, "CN",
                       StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void ApplyCdnMode()
    {
        if (IsCnCdn)
        {
            MANIFEST_ROOT_URL =
                "https://umamusume.cn-sy1.rains3.com/dat/";

            GENERIC_BASE_URL =
                "https://umamusume.cn-sy1.rains3.com/dat/";

#if UNITY_ANDROID
            ASSET_BASE_URL =
                "https://prd-storage-game-umamusume.akamaized.net/dl/resources/Android/assetbundles/";
#else
            ASSET_BASE_URL =
                "https://umamusume.cn-sy1.rains3.com/dat/";
#endif
        }
        else
        {
            MANIFEST_ROOT_URL =
                "https://prd-storage-app-umamusume.akamaized.net/dl/resources/Manifest";

            GENERIC_BASE_URL =
                "https://prd-storage-game-umamusume.akamaized.net/dl/resources/Generic";

#if UNITY_IOS || UNITY_IPHONE || UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            ASSET_BASE_URL =
                "https://prd-storage-game-umamusume.akamaized.net/dl/resources/iOS/assetbundles/";
#elif UNITY_ANDROID
            ASSET_BASE_URL =
                "https://prd-storage-game-umamusume.akamaized.net/dl/resources/Android/assetbundles/";
#else
            ASSET_BASE_URL =
                "https://prd-storage-game-umamusume.akamaized.net/dl/resources/Windows/assetbundles/";
#endif
        }
    }

#if UNITY_ANDROID || UNITY_IOS || UNITY_IPHONE
    private const int maxConcurrentDownloads = 6;
#else
    private const int maxConcurrentDownloads = 30;
#endif
    private static SemaphoreSlim semaphore = new SemaphoreSlim(maxConcurrentDownloads);
    private static List<Coroutine> downloadCoroutines = new List<Coroutine>();
    private static int CurrentCoroutinesCount = 0;
    private static int activeAssetDownloadBatches;
    public static bool IsDownloadingAssets => activeAssetDownloadBatches > 0;
    private static long completedAssetDownloads;
    public static long CompletedAssetDownloads => Interlocked.Read(ref completedAssetDownloads);
    private static WaitUntil downloadWaitUntil = new WaitUntil(() => CurrentCoroutinesCount < maxConcurrentDownloads);
    private static WaitUntil downloadWaitUntilComplete = new WaitUntil(() => CurrentCoroutinesCount == 0);
    private static List<Task> downloadTasks = new List<Task>();
    private const int downloadRetryCount = 3;
    private const string unityPlayerUserAgent = "UnityPlayer/2019.4.21f1 (UnityWebRequest/1.0, libcurl/7.52.0-DEV)";
    private static readonly object httpClientLock = new object();
    private static HttpClient httpClient;
    private static string httpClientConfiguration;

    private sealed class DownloadResponse
    {
        public byte[] Data;
        public string Error;

        public bool IsSuccess => Data != null && string.IsNullOrEmpty(Error);
    }

    public static IEnumerator DownloadText(string url, System.Action<string> callback, int timeoutSeconds = 3)
    {
        byte[] data = null;
        string error = null;
        yield return DownloadBytes(url, (responseData, responseError) =>
        {
            data = responseData;
            error = responseError;
        }, timeoutSeconds);

        if (data == null)
        {
            Debug.Log(error);
            callback("");
            yield break;
        }

        callback(Encoding.UTF8.GetString(data));
    }

    public static void DownloadAssetSync(UmaDatabaseEntry entry, Action<string , UIMessageType> callback = null)
    {
        string baseurl = entry.IsAssetBundle ? GetAssetRequestUrl(entry.Url) : GetGenericRequestUrl(entry.Url);
        DownloadResponse response = DownloadBytesAsync(baseurl).GetAwaiter().GetResult();
        if (!response.IsSuccess)
        {
            Debug.LogError(response.Error);
            callback?.Invoke($"Failed to download resources : {response.Error}", UIMessageType.Error);
        }
        else
        {
            Debug.Log("saving " + entry.Url);
            SaveDownloadedAsset(entry, response.Data);
        }
    }
  
    public static IEnumerator DownloadAssets(List<UmaDatabaseEntry> entries, Action<int, int, string> callback = null)
    {
        entries = entries.Where(e => !File.Exists(e.Path) && !UmaMissingAssets.IsMissing(e.Url)).ToList();
        if (entries.Count == 0) yield break;
        int completed = 0;
        activeAssetDownloadBatches++;
        try
        {
            callback?.Invoke(0, entries.Count, "DownLoading");
            foreach (var entry in entries)
            {
                yield return downloadWaitUntil;
                CurrentCoroutinesCount++;
                downloadCoroutines.Add(Instance.StartCoroutine(DownloadTask(entry, () =>
                    callback?.Invoke(++completed, entries.Count, "DownLoading"))));
            }

            yield return downloadWaitUntilComplete;
        }
        finally { activeAssetDownloadBatches--; }
    }

    public static IEnumerator DownloadTask(UmaDatabaseEntry entry, Action onFinished = null)
    {
        try
        {
            string baseurl = (string.IsNullOrEmpty(Path.GetExtension(entry.Name)) ? GetAssetRequestUrl(entry.Url) : GetGenericRequestUrl(entry.Url));
            byte[] data = null;
            string error = null;
            yield return DownloadBytes(baseurl, (responseData, responseError) =>
            {
                data = responseData;
                error = responseError;
            });

            if (data == null)
            {
                if (Instance)
                {
                    Instance.ShowMessage($"Failed to download resources : {error}", UIMessageType.Error);
                }
            }
            else
            {
                SaveDownloadedAsset(entry, data);
            }
        }
        finally
        {
            CurrentCoroutinesCount--;
            onFinished?.Invoke();
        }
    }

    public static async void DownloadAssets(IEnumerable<UmaDatabaseEntry> entrys)
    {
        downloadTasks.Clear();
        foreach (var entry in entrys)
        {
            await semaphore.WaitAsync();
            downloadTasks.Add(DownloadTask(entry, semaphore));
        }
        await Task.WhenAll(downloadTasks);
    }

    public static async Task DownloadTask(UmaDatabaseEntry entry, SemaphoreSlim semaphore)
    {
        try
        {
            if (!File.Exists(entry.Path))
            {
                string baseurl = (string.IsNullOrEmpty(Path.GetExtension(entry.Name)) ? GetAssetRequestUrl(entry.Url) : GetGenericRequestUrl(entry.Url));
                DownloadResponse response = await DownloadBytesAsync(baseurl);
                if (!response.IsSuccess)
                {
                    Debug.LogError(response.Error);
                }
                else
                {
                    Debug.Log("saving " + entry.Url);
                    SaveDownloadedAsset(entry, response.Data);
                }
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    public static IEnumerator DownloadBytes(string url, Action<byte[], string> callback, int timeoutSeconds = 0)
    {
        Task<DownloadResponse> task = DownloadBytesAsync(url, timeoutSeconds);
        yield return new WaitUntil(() => task.IsCompleted);

        DownloadResponse response = task.Status == TaskStatus.RanToCompletion
            ? task.Result
            : new DownloadResponse { Error = task.Exception?.GetBaseException().Message ?? "Download was cancelled." };
        callback?.Invoke(response.Data, response.Error);
    }

    private static async Task<DownloadResponse> DownloadBytesAsync(string url, int timeoutSeconds = 0)
    {
        string hash = ResourceHash(url);
        if (hash != null && UmaMissingAssets.IsMissing(hash))
            return new DownloadResponse { Error = "Cached unavailable resource: " + hash };
        string lastError = null;
        for (int attempt = 0; attempt <= downloadRetryCount; attempt++)
        {
            try
            {
                HttpClient client = GetHttpClient();
                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
                using var timeoutCancellation = timeoutSeconds > 0
                    ? new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds))
                    : null;
                CancellationToken cancellationToken = timeoutCancellation?.Token ?? CancellationToken.None;
                using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    lastError = $"HTTP {(int)response.StatusCode} ({response.ReasonPhrase})";
                    if (hash != null && UmaMissingAssets.IsPermanentFailure(lastError))
                    {
                        UmaMissingAssets.MarkMissing(hash, lastError);
                        return new DownloadResponse { Error = lastError };
                    }
                }
                else
                {
                    return new DownloadResponse { Data = await ReadResponseBytesAsync(response, timeoutSeconds).ConfigureAwait(false) };
                }
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }

            if (attempt < downloadRetryCount)
            {
                await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
        }

        return new DownloadResponse { Error = $"{lastError} (after {downloadRetryCount} retries)" };
    }

    private static string ResourceHash(string url)
    {
        var uri = new Uri(url);
        if (!uri.AbsolutePath.StartsWith("/dl/resources/", StringComparison.Ordinal)) return null;
        return System.IO.Path.GetFileName(uri.AbsolutePath);
    }

    private static async Task<byte[]> ReadResponseBytesAsync(HttpResponseMessage response, int timeoutSeconds)
    {
        using var idleCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds > 0 ? timeoutSeconds : 30));
        // Unity's Mono response streams may ignore ReadAsync's cancellation token.
        // Closing the response also interrupts that pending socket read.
        using var bodyCancellation = idleCancellation.Token.Register(() => response.Dispose());
        using Stream responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var memoryStream = response.Content.Headers.ContentLength is long contentLength && contentLength >= 0 && contentLength <= int.MaxValue
            ? new MemoryStream((int)contentLength)
            : new MemoryStream();
        var buffer = new byte[64 * 1024];
        // ResponseHeadersRead ends HttpClient's timeout at the headers. Keep a
        // stalled key/manifest body from leaving startup waiting indefinitely.
        int bytesRead;
        while (true)
        {
            idleCancellation.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds > 0 ? timeoutSeconds : 30));
            bytesRead = await responseStream.ReadAsync(buffer, 0, buffer.Length, idleCancellation.Token).ConfigureAwait(false);
            if (bytesRead == 0) break;
            await memoryStream.WriteAsync(buffer, 0, bytesRead).ConfigureAwait(false);
        }

        if (response.Content.Headers.ContentLength is long expectedLength && memoryStream.Length != expectedLength)
            throw new EndOfStreamException($"Incomplete download: expected {expectedLength} bytes, received {memoryStream.Length}.");

        return memoryStream.ToArray();
    }

    private static HttpClient GetHttpClient()
    {
        bool useProxy = Config.Instance == null || Config.Instance.UseNetworkProxy;
        bool useSystemProxy = useProxy && (Config.Instance == null || Config.Instance.UseSystemNetworkProxy);
        Uri proxyUri = null;
        string configuration = useSystemProxy ? "system" : "direct";
        if (useProxy && !useSystemProxy)
        {
            if (!Config.Instance.TryGetNetworkProxyUri(out proxyUri, out string error))
            {
                throw new InvalidOperationException(error);
            }

            configuration = proxyUri.AbsoluteUri;
        }

        lock (httpClientLock)
        {
            if (httpClient != null && httpClientConfiguration == configuration)
            {
                return httpClient;
            }

            var handler = new HttpClientHandler
            {
                UseProxy = useProxy
            };
            if (useSystemProxy)
            {
                // Resolve the host computer's proxy instead of assuming a local
                // application's port. Preserve the OS proxy/bypass rules.
                handler.Proxy = WebRequest.GetSystemWebProxy();
            }
            else if (useProxy)
            {
                handler.Proxy = new WebProxy(proxyUri);
            }

            var newClient = new HttpClient(handler, true)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            newClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", unityPlayerUserAgent);

            HttpClient oldClient = httpClient;
            httpClient = newClient;
            httpClientConfiguration = configuration;
            oldClient?.Dispose();
            return httpClient;
        }
    }

    private static void SaveDownloadedAsset(UmaDatabaseEntry entry, byte[] data)
    {
        var directory = Path.GetDirectoryName(entry.Path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            MarkNoBackup(directory);
        }

        // Parallel isolated Editors share the content-addressed game cache.
        // Publish a complete file once; never delete another reader's winner.
        var tempPath = entry.Path + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(tempPath, data);
            try { File.Move(tempPath, entry.Path); }
            catch (IOException) when (File.Exists(entry.Path)) { }
        }
        finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
        MarkNoBackup(entry.Path);
        Interlocked.Increment(ref completedAssetDownloads);
    }

    private static void MarkNoBackup(string path)
    {
#if UNITY_IOS || UNITY_IPHONE
        UnityEngine.iOS.Device.SetNoBackupFlag(path);
#endif
    }

    public static string GetManifestRequestUrl(string hash)
    {
        ApplyCdnMode();
        return $"{MANIFEST_ROOT_URL.TrimEnd('/')}/{hash.Substring(0, 2)}/{hash}";
    }

    public static string GetGenericRequestUrl(string hash)
    {
        ApplyCdnMode();
        return $"{GENERIC_BASE_URL.TrimEnd('/')}/{hash.Substring(0, 2)}/{hash}";
    }
    
    public static string GetAssetRequestUrl(string hash)
    {
        ApplyCdnMode();
        return $"{ASSET_BASE_URL.TrimEnd('/')}/{hash.Substring(0, 2)}/{hash}";
    }
}
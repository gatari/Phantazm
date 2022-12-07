using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Cysharp.Threading.Tasks;
using UltraLiteDB;
using UnityEngine;
using UnityEngine.Networking;

namespace Phantazm
{
    public class CacheStore : ICacheStore
    {
        private readonly string _cacheRootDir;
        private readonly string _cacheDataDir;
        private const string CacheCollectionName = "cached_items";
        private readonly string _dbPath;

        public CacheStore(string cacheRoot)
        {
            _cacheRootDir = cacheRoot;
            _dbPath = Path.Combine(cacheRoot, "CacheStore.db");
            _cacheDataDir = Path.Combine(cacheRoot, "Files");
            Initialize();
        }

        private void Initialize()
        {
            if (!Directory.Exists(_cacheRootDir))
            {
                Directory.CreateDirectory(_cacheRootDir);
            }

            if (!Directory.Exists(_cacheDataDir))
            {
                Directory.CreateDirectory(_cacheDataDir);
            }

            using var db = new UltraLiteDatabase(_dbPath);
            var cacheItems = db.GetCollection<CacheItem>(CacheCollectionName);
            cacheItems.EnsureIndex("ExpireTime");
        }

        public UniTask SaveAsync(string key, byte[] data, TimeSpan expiresIn)
        {
            var hash = Hash(key);

            if (!Directory.Exists(_cacheDataDir))
            {
                Directory.CreateDirectory(_cacheDataDir);
            }

            return UniTask.Create(async () =>
            {
                try
                {
                    var newCachePath = Path.Combine(_cacheDataDir, hash);
                    using var fileStream =
                        new FileStream(newCachePath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await fileStream.WriteAsync(data, 0, data.Length);

                    using var db = new UltraLiteDatabase(_dbPath);
                    var cacheItems = db.GetCollection<CacheItem>(CacheCollectionName);

                    var newEntry = new CacheItem()
                    {
                        ExpireTime = DateTime.UtcNow.Add(expiresIn),
                        Id = hash,
                    };
                    cacheItems.Upsert(hash, newEntry);
                    Debug.Log(newEntry.ToString());
                }
                catch (Exception ex)
                {
                    Debug.LogError(ex);
                }
            });
        }

        public async UniTask<(byte[], CacheError)> LoadAsync(string url)
        {
            var id = Hash(url);

            using var db = new UltraLiteDatabase(_dbPath);
            var cacheItems = db.GetCollection<CacheItem>(CacheCollectionName);
            var query = cacheItems.FindById(id);
            if (query == default)
            {
                return (null, CacheError.NotFound("no matching item"));
            }

            if (query.IsExpired())
            {
                return (null, CacheError.Expired());
            }

            var cachePath = Path.Combine(_cacheDataDir, id);
            if (!File.Exists(cachePath))
            {
                return (null, CacheError.NotFound("matching entry found but no file"));
            }

            using var fileStream = new FileStream(cachePath, FileMode.Open, FileAccess.Read);
            var data = new byte[fileStream.Length];
            await fileStream.ReadAsync(data, 0, data.Length);
            return (data, CacheError.Success());
        }

        public async UniTask<DownloadAudioClipResponse> DownloadAudioClipAsync(string url)
        {
            var id = Hash(url);

            using var db = new UltraLiteDatabase(_dbPath);
            var cacheItems = db.GetCollection<CacheItem>(CacheCollectionName);
            var query = cacheItems.FindById(id);

            if (query == default)
            {
                return new DownloadAudioClipResponse()
                {
                    Clip = null,
                    CacheError = CacheError.NotFound("no matching entry")
                };
            }

            if (query.IsExpired())
            {
                return new DownloadAudioClipResponse()
                {
                    Clip = null,
                    CacheError = CacheError.Expired()
                };
            }

            try
            {
                var uri = $"file://{_cacheDataDir}/{id}";
                Debug.Log($"streaming from local file {uri}");
                using var uwr = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.MPEG);
                await uwr.SendWebRequest();

                if (uwr.result == UnityWebRequest.Result.Success)
                {
                    var audioClip = DownloadHandlerAudioClip.GetContent(uwr);
                    return new DownloadAudioClipResponse()
                    {
                        Clip = audioClip,
                        CacheError = CacheError.Success()
                    };
                }
                else
                {
                    return new DownloadAudioClipResponse()
                    {
                        Clip = null,
                        CacheError = CacheError.Unknown(uwr.error)
                    };
                }
            }
            catch (Exception ex)
            {
                return new DownloadAudioClipResponse()
                {
                    Clip = null,
                    CacheError = CacheError.Unknown(ex.Message)
                };
            }
        }

        public async UniTask DeleteExpiredAsync()
        {
            // using var db = new UltraLiteDatabase(_dbPath);
            // var cacheItems = db.GetCollection<CacheItem>(CacheCollectionName);
            // var expired = cacheItems.Find(Query.LT("ExpireTime", DateTime.UtcNow)).ToList();
            // foreach (var cachePath in expired.Select(e => Path.Combine(_cacheDataDir, e.HashedUrl)))
            // {
            //     using var fileStream = new FileStream(cachePath, FileMode.Open, FileAccess.Write, FileShare.None, 1,
            //         FileOptions.DeleteOnClose);
            //     await fileStream.FlushAsync();
            // }
        }

        private static string Hash(string input)
        {
            var md5Hasher = MD5.Create();
            var data = md5Hasher.ComputeHash(Encoding.Default.GetBytes(input));
            return BitConverter.ToString(data);
        }

        public void DeleteAll()
        {
            using var db = new UltraLiteDatabase(_dbPath);
            db.DropCollection(CacheCollectionName);
        }
    }

    public class CacheItem
    {
        [BsonId] public string Id { get; set; }
        [BsonField("ExpireTime")] public DateTime ExpireTime { get; set; }

        public bool IsExpired()
        {
            return ExpireTime < DateTime.UtcNow;
        }

        public override string ToString()
        {
            return $"$id: {Id}, $ExpireTime: {ExpireTime}";
        }
    }

    public class DownloadAudioClipResponse
    {
        public AudioClip Clip { get; set; }
        public CacheError CacheError { get; set; }
    }

    public class CacheError
    {
        public string Message { private set; get; }

        public CacheErrorStatusCode StatusCode { private set; get; }

        public enum CacheErrorStatusCode
        {
            Success,
            Unknown,
            Expired,
            NotFound,
        }

        public static CacheError Unknown(string message)
        {
            return new CacheError()
            {
                Message = $"unknown error occured {message}",
                StatusCode = CacheErrorStatusCode.Unknown
            };
        }

        public static CacheError Expired()
        {
            return new CacheError()
            {
                Message = "cache is expired",
                StatusCode = CacheErrorStatusCode.Expired
            };
        }

        public static CacheError NotFound(string message)
        {
            return new CacheError()
            {
                Message = $"matching cache is not found {message}",
                StatusCode = CacheErrorStatusCode.NotFound
            };
        }

        public static CacheError Success()
        {
            return new CacheError()
            {
                Message = "success",
                StatusCode = CacheErrorStatusCode.Success
            };
        }
    }
}
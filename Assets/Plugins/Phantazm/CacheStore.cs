using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using SQLite;

namespace Phantazm
{
    public class CacheStore : ICacheStore
    {
        private readonly SQLiteConnection _connection;
        private readonly string _cacheDataDir;

        public CacheStore(string cacheRoot)
        {
            var dbPath = Path.Combine(cacheRoot, "CacheStore.db");

            _cacheDataDir = Path.Combine(cacheRoot, "Cache");
            if (!Directory.Exists(_cacheDataDir))
            {
                Directory.CreateDirectory(_cacheDataDir);
            }

            _connection = new SQLiteConnection(dbPath);
            _connection.CreateTable<CacheItem>();
        }

        public UniTask SaveAsync(string key, byte[] data, TimeSpan expiresIn)
        {
            var hash = Hash(key);
            var entry = new CacheItem()
            {
                ID = hash,
                ExpireTime = DateTime.Now.Add(expiresIn)
            };

            if (!Directory.Exists(_cacheDataDir))
            {
                Directory.CreateDirectory(_cacheDataDir);
            }

            return UniTask.Create(async () =>
            {
                _connection.InsertOrReplace(entry);
                var newCachePath = Path.Combine(_cacheDataDir, hash);
                using var fileStream = new FileStream(newCachePath, FileMode.Create, FileAccess.Write, FileShare.None);
                await fileStream.WriteAsync(data, 0, data.Length);
            });
        }

        public async UniTask<(byte[], CacheError)> LoadAsync(string key)
        {
            var hash = Hash(key);
            var query = _connection.Table<CacheItem>().FirstOrDefault(i => i.ID == hash);
            if (query == default)
            {
                return (null, CacheError.NotFound());
            }

            if (query.IsExpired())
            {
                return (null, CacheError.Expired());
            }

            var cachePath = Path.Combine(_cacheDataDir, hash);
            if (!File.Exists(cachePath))
            {
                return (null, CacheError.NotFound());
            }

            using var fileStream = new FileStream(cachePath, FileMode.Open, FileAccess.Read);
            var data = new byte[fileStream.Length];
            await fileStream.ReadAsync(data, 0, data.Length);
            return (data, CacheError.Success());
        }

        public async UniTask<DownloadAudioClipResponse> DownloadAudioClipAsync(string url)
        {
            var hash = Hash(url);
            var query = _connection.Table<CacheItem>().FirstOrDefault(i => i.ID == hash);
            if (query == default)
            {
                return new DownloadAudioClipResponse()
                {
                    Clip = null,
                    CacheError = CacheError.NotFound()
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

            var localCacheUri = Path.Combine("file:", _cacheDataDir, query.ID);
            Debug.Log(localCacheUri);
            using var uwr = UnityWebRequestMultimedia.GetAudioClip(localCacheUri, AudioType.MPEG);
            var operation = await uwr.SendWebRequest();

            if (operation.result == UnityWebRequest.Result.Success)
            {
                var audioClip = DownloadHandlerAudioClip.GetContent(operation);
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
                    CacheError = CacheError.Unknown(operation.error)
                };
            }
        }

        public async UniTask DeleteExpiredAsync()
        {
            var expired = _connection.Table<CacheItem>().Where(i => i.ExpireTime < DateTime.Now).ToList();
            foreach (var cachePath in expired.Select(e => Path.Combine(_cacheDataDir, e.ID)))
            {
                using var fileStream = new FileStream(cachePath, FileMode.Open, FileAccess.Write, FileShare.None, 1,
                    FileOptions.DeleteOnClose);
                await fileStream.FlushAsync();
            }
        }

        private static string Hash(string input)
        {
            var md5Hasher = MD5.Create();
            var data = md5Hasher.ComputeHash(Encoding.Default.GetBytes(input));
            return BitConverter.ToString(data);
        }

        public void DeleteAll()
        {
             _connection.DeleteAll<CacheItem>();
        }
    }

    public class CacheItem
    {
        [PrimaryKey] public string ID { get; set; }
        [Indexed] public DateTime ExpireTime { get; set; }

        public bool IsExpired()
        {
            return ExpireTime < DateTime.Now;
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

        public static CacheError NotFound()
        {
            return new CacheError()
            {
                Message = "matching cache is not found",
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
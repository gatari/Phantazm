using System;
using Cysharp.Threading.Tasks;

namespace Phantazm
{
    public interface ICacheStore
    {
        UniTask SaveAsync(string key, byte[] data, TimeSpan expiresIn);
        UniTask<(byte[], CacheError)> LoadAsync(string key);
        UniTask<DownloadAudioClipResponse> DownloadAudioClipAsync(string url);
        UniTask DeleteExpiredAsync();
        void DeleteAll();
    }
}
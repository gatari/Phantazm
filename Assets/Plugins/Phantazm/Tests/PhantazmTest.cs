using System;
using System.Collections;
using System.Text;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Phantazm;
using UnityEngine;
using UnityEngine.TestTools;

namespace Plugins.Phantazm.Tests
{
    public class PhantazmTest
    {
        [UnityTest]
        public IEnumerator TestCacheStore()
        {
            var store = new CacheStore(Application.temporaryCachePath);
            yield return UniTask.Run(async () =>
                {
                    await store.DeleteExpiredAsync();
                    await store.SaveAsync("hoge", Encoding.Default.GetBytes("Hello World"), TimeSpan.FromSeconds(10));
                    await store.SaveAsync("huga", Encoding.Default.GetBytes("This should expires soon"), TimeSpan.Zero);

                    var loadHogeResult = await store.LoadAsync("hoge");
                    Assert.AreEqual(loadHogeResult.Item2, true);
                    Assert.AreEqual(loadHogeResult.Item1, Encoding.Default.GetBytes("Hello World"));

                    var loadHugaResult = await store.LoadAsync("huga");
                    Assert.AreEqual(loadHugaResult.Item2, false);

                    var loadPiyoResult = await store.LoadAsync("piyo");
                    Assert.AreEqual(loadPiyoResult.Item2, false);
                })
                .ToCoroutine();
        }
    }
}
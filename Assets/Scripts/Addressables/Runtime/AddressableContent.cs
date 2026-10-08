using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
namespace Portable.AddressableContent
{
    // 호출자가 직접 실행하는 API이며, 테이블에 의존하거나 자동으로 시작하지 않습니다.
    public static class ContentLoader
    {
        public static async Task InitializeAsync(bool checkForUpdates = true)
        {
            await Consume(Addressables.InitializeAsync(false), "Initialization");
            if (!checkForUpdates) return;
            var catalogs = await Consume(Addressables.CheckForCatalogUpdates(false), "Catalog check");
            if (catalogs != null && catalogs.Count > 0)
                await Consume(Addressables.UpdateCatalogs(catalogs, false), "Catalog update");
        }
        public static async Task<AssetLease<T>> LoadLabelAsync<T>(string label) where T : UnityEngine.Object
        {
            var handle = Addressables.LoadAssetsAsync<T>(label, false, null);
            try
            {
                await Wait(handle);
                EnsureSuccess(handle, $"Load label '{label}'");
                return new AssetLease<T>(handle);
            }
            catch { if (handle.IsValid()) Addressables.Release(handle); throw; }
        }
        private static async Task<T> Consume<T>(AsyncOperationHandle<T> handle, string operation)
        {
            try
            {
                var result = await Wait(handle);
                EnsureSuccess(handle, operation);
                return result;
            }
            finally { if (handle.IsValid()) Addressables.Release(handle); }
        }
        private static Task<T> Wait<T>(AsyncOperationHandle<T> handle)
        {
            if (handle.IsDone) return Task.FromResult(handle.Result);
            var completion = new TaskCompletionSource<T>();
            handle.Completed += completed => completion.TrySetResult(completed.Result);
            return completion.Task;
        }
        private static void EnsureSuccess<T>(AsyncOperationHandle<T> handle, string operation)
        {
            if (handle.Status != AsyncOperationStatus.Succeeded)
                throw new InvalidOperationException(operation + " failed", handle.OperationException);
        }
    }
    public sealed class AssetLease<T> : IDisposable where T : UnityEngine.Object
    {
        private AsyncOperationHandle<IList<T>> _handle;
        private bool _disposed;
        internal AssetLease(AsyncOperationHandle<IList<T>> handle) { _handle = handle; }
        public IList<T> Assets => !_disposed ? _handle.Result : throw new ObjectDisposedException(nameof(AssetLease<T>));
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_handle.IsValid()) Addressables.Release(_handle);
        }
    }
}

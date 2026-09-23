using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace ProjectT.DataTable
{
    /// <summary>
    /// 게임 시작 시 자동으로:
    ///  1) Addressables 초기화
    ///  2) 원격 카탈로그에 패치(업데이트된 콘텐츠)가 있는지 확인
    ///  3) 있으면 카탈로그를 갱신 (바뀐 테이블 번들만 새로 받음)
    ///  4) TableManager 초기화 (모든 DataTable 로드)
    ///
    /// 테이블을 쓰는 쪽은 TableManager.InitializeAsync()를 직접 부르지 말고 <see cref="Ready"/>를 기다리세요.
    /// 패치 확인보다 먼저 테이블을 읽으면 패치 전 데이터를 들고 있게 됩니다.
    ///   await AddressablesBootstrap.Ready;
    ///   var row = TableManager.GetRow("Item", 2001);
    ///
    /// 에디터/Development 빌드에서는 로드된 테이블 요약을 콘솔에 남깁니다.
    /// </summary>
    public static class AddressablesBootstrap
    {
        /// <summary>테이블 요약 로그에 보여줄 최대 행 수 (행이 많은 테이블이 콘솔을 뒤덮지 않도록)</summary>
        private const int MaxRowsInSummary = 5;

        private static TaskCompletionSource<bool> _ready = new TaskCompletionSource<bool>();

        /// <summary>패치 확인과 테이블 로딩이 모두 끝나면 완료되는 Task. 실패하면 예외로 끝납니다.</summary>
        public static Task Ready => _ready.Task;

        // "Enter Play Mode Options"로 도메인 리로드를 끈 경우에도 Play할 때마다 새로 시작하도록 초기화
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            TableManager.Reset();
            _ready = new TaskCompletionSource<bool>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void OnLoad()
        {
            _ = RunAsyncSafe();
        }

        private static async Task RunAsyncSafe()
        {
            try
            {
                await RunAsync();
                _ready.TrySetResult(true);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AddressablesBootstrap] 예외 발생: {e}");
                _ready.TrySetException(e);
            }
        }

        private static async Task RunAsync()
        {
            Debug.Log("[AddressablesBootstrap] 초기화 시작...");

            var initHandle = Addressables.InitializeAsync(false);
            await AwaitAndRelease(initHandle, "Addressables 초기화");
            Debug.Log("[AddressablesBootstrap] Addressables 초기화 완료");

            var checkHandle = Addressables.CheckForCatalogUpdates(false);
            List<string> catalogsToUpdate = await AwaitAndRelease(checkHandle, "카탈로그 확인");

            if (catalogsToUpdate != null && catalogsToUpdate.Count > 0)
            {
                Debug.Log($"[AddressablesBootstrap] 패치 발견: {string.Join(", ", catalogsToUpdate)} -> 카탈로그 업데이트 중...");
                var updateHandle = Addressables.UpdateCatalogs(catalogsToUpdate, false);
                var locators = await AwaitAndRelease(updateHandle, "카탈로그 업데이트");
                Debug.Log($"[AddressablesBootstrap] 카탈로그 업데이트 완료 ({locators?.Count ?? 0}개 로케이터)");
            }
            else
            {
                Debug.Log("[AddressablesBootstrap] 새 패치 없음 (최신 상태)");
            }

            await TableManager.InitializeAsync();
            LogLoadedTables();
        }

        private static async Task<T> AwaitAndRelease<T>(AsyncOperationHandle<T> handle, string operation)
        {
            try
            {
                var result = await handle.Task;
                if (handle.Status != AsyncOperationStatus.Succeeded)
                    throw new System.InvalidOperationException($"{operation} 실패", handle.OperationException);
                return result;
            }
            finally
            {
                if (handle.IsValid()) Addressables.Release(handle);
            }
        }

        /// <summary>
        /// 실제로 로드된 테이블을 전부 순회하며 요약을 남깁니다. 테이블 이름을 코드에 적을 필요가 없어서
        /// 테이블이 추가/삭제되어도 이 코드는 고칠 필요가 없습니다.
        /// 릴리즈 빌드에서는 출력하지 않습니다.
        /// </summary>
        private static void LogLoadedTables()
        {
            if (!Debug.isDebugBuild) return;

            var tables = TableManager.Tables.OrderBy(t => t.tableName).ToList();
            if (tables.Count == 0)
            {
                Debug.LogWarning($"[AddressablesBootstrap] 로드된 테이블이 없습니다. '{TableManager.DefaultLabel}' 라벨이 붙은 테이블이 있는지 확인하세요.");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.Append($"[AddressablesBootstrap] 테이블 {tables.Count}개 로드 완료: {string.Join(", ", tables.Select(t => t.tableName))}");

            foreach (var table in tables)
            {
                sb.Append($"\n  - {table.tableName} ({table.RowCount}행, 컬럼: {string.Join(", ", table.columns.Select(c => c.name))})");

                var preview = table.Rows.Take(MaxRowsInSummary)
                    .Select(row => row.GetString(table.idColumn) + (row.HasColumn("Name") ? "/" + row.GetString("Name") : ""));
                sb.Append("\n      ").Append(string.Join("  ", preview));
                if (table.RowCount > MaxRowsInSummary)
                {
                    sb.Append($"  ... 외 {table.RowCount - MaxRowsInSummary}행");
                }
            }

            Debug.Log(sb.ToString());
        }
    }
}

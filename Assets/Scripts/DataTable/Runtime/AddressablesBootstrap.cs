using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace ProjectT.DataTable
{
    /// <summary>
    /// 게임 시작 시 자동으로:
    ///  1) Addressables 초기화
    ///  2) 원격 카탈로그에 패치(업데이트된 콘텐츠)가 있는지 확인
    ///  3) 있으면 카탈로그를 갱신 (바뀐 테이블 번들만 새로 받음)
    ///  4) TableManager 초기화 (모든 DataTable 로드)
    /// 결과를 콘솔에 요약 로그로 남겨서, Play 모드에서 바로 확인할 수 있게 합니다.
    /// </summary>
    public static class AddressablesBootstrap
    {
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
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AddressablesBootstrap] 예외 발생: {e}");
            }
        }

        private static async Task RunAsync()
        {
            Debug.Log("[AddressablesBootstrap] 초기화 시작...");

            var initHandle = Addressables.InitializeAsync();
            await initHandle.Task;
            Debug.Log("[AddressablesBootstrap] Addressables 초기화 완료");

            var checkHandle = Addressables.CheckForCatalogUpdates(false);
            List<string> catalogsToUpdate = await checkHandle.Task;
            Addressables.Release(checkHandle);

            if (catalogsToUpdate != null && catalogsToUpdate.Count > 0)
            {
                Debug.Log($"[AddressablesBootstrap] 패치 발견: {string.Join(", ", catalogsToUpdate)} -> 카탈로그 업데이트 중...");
                var updateHandle = Addressables.UpdateCatalogs(catalogsToUpdate);
                var locators = await updateHandle.Task;
                Addressables.Release(updateHandle);
                Debug.Log($"[AddressablesBootstrap] 카탈로그 업데이트 완료 ({locators?.Count ?? 0}개 로케이터)");
            }
            else
            {
                Debug.Log("[AddressablesBootstrap] 새 패치 없음 (최신 상태)");
            }

            Debug.Log("[AddressablesBootstrap] TableManager.InitializeAsync() 호출 시작...");
            await TableManager.InitializeAsync();
            Debug.Log("[AddressablesBootstrap] TableManager 초기화 완료. 로드된 데이터 확인:");

            LogTableIfExists("Monster");
            LogTableIfExists("Item");
        }

        private static void LogTableIfExists(string tableName)
        {
            var table = TableManager.Get(tableName);
            if (table == null)
            {
                Debug.LogWarning($"[AddressablesBootstrap] 테이블 없음: {tableName}");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.Append($"[AddressablesBootstrap] Table '{tableName}' ({table.RowCount}행): ");
            foreach (var row in table.Rows)
            {
                var idCol = table.idColumn;
                sb.Append(row.GetString(idCol)).Append(row.HasColumn("Name") ? ("/" + row.GetString("Name")) : "").Append("  ");
            }
            Debug.Log(sb.ToString());
        }
    }
}

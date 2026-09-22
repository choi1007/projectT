using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace ProjectT.DataTable
{
    /// <summary>
    /// 모든 DataTableAsset을 Addressables 라벨로 한 번에 비동기 로드해서 캐싱하고,
    /// 어디서든 정적으로 접근할 수 있게 해주는 매니저.
    ///
    /// 사용법:
    ///   await TableManager.InitializeAsync();   // 게임 시작 시 한 번
    ///   var row = TableManager.GetRow("Monster", 1001);
    ///   int hp = row.GetInt("Hp");
    /// </summary>
    public static class TableManager
    {
        public const string DefaultLabel = "DataTable";

        private static readonly Dictionary<string, DataTableAsset> _tables = new Dictionary<string, DataTableAsset>();
        private static AsyncOperationHandle<IList<DataTableAsset>> _handle;
        private static bool _initialized;
        private static Task _initTask;

        /// <summary>
        /// Addressables에서 DefaultLabel(또는 지정한 라벨)이 붙은 모든 DataTableAsset을 로드합니다.
        /// 여러 곳에서 동시에 호출해도 안전하며, 이미 초기화되었으면 즉시 반환합니다.
        /// </summary>
        public static Task InitializeAsync(string label = DefaultLabel)
        {
            if (_initialized) return Task.CompletedTask;
            if (_initTask != null) return _initTask;

            _initTask = LoadAsync(label);
            return _initTask;
        }

        private static async Task LoadAsync(string label)
        {
            _handle = Addressables.LoadAssetsAsync<DataTableAsset>(label, table =>
            {
                if (table == null) return;
                if (string.IsNullOrEmpty(table.tableName))
                {
                    Debug.LogWarning($"[TableManager] tableName이 비어있는 DataTableAsset이 있습니다: {table.name}");
                    return;
                }
                _tables[table.tableName] = table;
            });

            await _handle.Task;
            _initialized = true;
        }

        /// <summary>테이블 이름으로 DataTableAsset을 가져옵니다. 초기화 전이면 null.</summary>
        public static DataTableAsset Get(string tableName)
        {
            if (!_initialized)
            {
                Debug.LogError("[TableManager] 아직 초기화되지 않았습니다. TableManager.InitializeAsync()를 먼저 await 하세요.");
                return null;
            }

            if (_tables.TryGetValue(tableName, out var table)) return table;

            Debug.LogError($"[TableManager] 테이블을 찾을 수 없습니다: {tableName}");
            return null;
        }

        /// <summary>테이블 이름 + ID로 행을 바로 가져오는 헬퍼.</summary>
        public static DataRow GetRow(string tableName, int id) => Get(tableName)?.GetRow(id);

        public static bool IsInitialized => _initialized;

        /// <summary>테스트/에디터용: 캐시를 비우고 다시 초기화할 수 있게 합니다.</summary>
        public static void Reset()
        {
            if (_handle.IsValid())
            {
                Addressables.Release(_handle);
            }
            _tables.Clear();
            _initialized = false;
            _initTask = null;
        }
    }
}

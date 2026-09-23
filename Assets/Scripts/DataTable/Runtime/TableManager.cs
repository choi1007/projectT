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
    ///
    ///   foreach (var table in TableManager.Tables) { ... }   // 로드된 모든 테이블 순회
    /// </summary>
    public static class TableManager
    {
        public const string DefaultLabel = "DataTable";

        private static readonly Dictionary<string, DataTableAsset> _tables = new Dictionary<string, DataTableAsset>();
        private static AsyncOperationHandle<IList<DataTableAsset>> _handle;
        private static bool _initialized;
        private static Task _initTask;
        private static int _generation;

        /// <summary>
        /// Addressables에서 DefaultLabel(또는 지정한 라벨)이 붙은 모든 DataTableAsset을 로드합니다.
        /// 여러 곳에서 동시에 호출해도 안전하며, 이미 초기화되었으면 즉시 반환합니다.
        /// </summary>
        public static Task InitializeAsync(string label = DefaultLabel)
        {
            if (_initialized) return Task.CompletedTask;
            if (_initTask != null && !_initTask.IsFaulted && !_initTask.IsCanceled) return _initTask;

            _initTask = LoadAsync(label);
            return _initTask;
        }

        private static async Task LoadAsync(string label)
        {
            int generation = _generation;
            // 실패 시에도 상태를 확인한 뒤 직접 해제할 수 있도록 핸들을 유지합니다.
            var handle = Addressables.LoadAssetsAsync<DataTableAsset>(label, false, null);
            _handle = handle;
            try
            {
                var tables = await handle.Task;
                if (generation != _generation)
                    throw new System.OperationCanceledException("테이블 초기화가 리셋되었습니다.");
                if (handle.Status != AsyncOperationStatus.Succeeded)
                    throw new System.InvalidOperationException($"테이블 로드 실패: {label}", handle.OperationException);

                var loaded = new Dictionary<string, DataTableAsset>();
                foreach (var table in tables)
                {
                    if (table == null || string.IsNullOrEmpty(table.tableName))
                        throw new System.InvalidOperationException("이름이 없거나 유효하지 않은 테이블입니다.");
                    if (loaded.ContainsKey(table.tableName))
                        throw new System.InvalidOperationException($"중복 테이블 이름: {table.tableName}");
                    loaded.Add(table.tableName, table);
                }
                _tables.Clear();
                foreach (var pair in loaded) _tables.Add(pair.Key, pair.Value);
                _initialized = true;
            }
            catch
            {
                if (generation == _generation)
                {
                    if (handle.IsValid()) Addressables.Release(handle);
                    _handle = default;
                    _tables.Clear();
                    _initialized = false;
                }
                throw;
            }
        }

        /// <summary>테이블 이름으로 DataTableAsset을 가져옵니다. 초기화 전이거나 없으면 에러 로그 후 null.</summary>
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

        /// <summary>
        /// 에러 로그 없이 테이블을 찾아봅니다. 있을 수도 없을 수도 있는 테이블(패치로 추가/삭제되는 테이블 등)을 확인할 때 쓰세요.
        /// </summary>
        public static bool TryGet(string tableName, out DataTableAsset table)
        {
            table = null;
            return _initialized && _tables.TryGetValue(tableName, out table);
        }

        /// <summary>테이블 이름 + ID로 행을 바로 가져오는 헬퍼.</summary>
        public static DataRow GetRow(string tableName, int id) => Get(tableName)?.GetRow(id);

        /// <summary>로드된 모든 테이블. 초기화 전이면 비어 있습니다.</summary>
        public static IReadOnlyCollection<DataTableAsset> Tables => _tables.Values;

        /// <summary>로드된 모든 테이블 이름. 초기화 전이면 비어 있습니다.</summary>
        public static IReadOnlyCollection<string> TableNames => _tables.Keys;

        public static bool IsInitialized => _initialized;

        /// <summary>테스트/에디터용: 캐시를 비우고 다시 초기화할 수 있게 합니다.</summary>
        public static void Reset()
        {
            _generation++;
            if (_handle.IsValid())
            {
                Addressables.Release(_handle);
            }
            _tables.Clear();
            _handle = default;
            _initialized = false;
            _initTask = null;
        }
    }
}

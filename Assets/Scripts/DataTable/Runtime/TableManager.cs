using System;
using System.Collections.Generic;
using UnityEngine;
namespace ProjectT.DataTable
{
    // 테이블 로딩과 에셋 수명 관리는 호출자가 담당합니다.
    public static class TableManager
    {
        public const string DefaultLabel = "DataTable";
        private static readonly Dictionary<string, DataTableAsset> _tables = new Dictionary<string, DataTableAsset>();
        public static bool IsInitialized { get; private set; }
        public static IReadOnlyCollection<DataTableAsset> Tables => _tables.Values;
        public static IReadOnlyCollection<string> TableNames => _tables.Keys;
        public static void Initialize(IEnumerable<DataTableAsset> tables)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            var loaded = new Dictionary<string, DataTableAsset>();
            foreach (var table in tables)
            {
                if (table == null || string.IsNullOrWhiteSpace(table.tableName))
                    throw new InvalidOperationException("Invalid table or empty table name.");
                if (loaded.ContainsKey(table.tableName))
                    throw new InvalidOperationException($"Duplicate table: {table.tableName}");
                table.BuildIndex();
                loaded.Add(table.tableName, table);
            }
            _tables.Clear();
            foreach (var pair in loaded) _tables.Add(pair.Key, pair.Value);
            IsInitialized = true;
        }
        public static DataTableAsset Get(string tableName)
        {
            if (TryGet(tableName, out var table)) return table;
            Debug.LogError($"[TableManager] Not initialized or missing table: {tableName}");
            return null;
        }
        public static bool TryGet(string tableName, out DataTableAsset table)
        {
            table = null;
            return IsInitialized && _tables.TryGetValue(tableName, out table);
        }
        public static DataRow GetRow(string tableName, int id) => Get(tableName)?.GetRow(id);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset() { _tables.Clear(); IsInitialized = false; }
    }
}

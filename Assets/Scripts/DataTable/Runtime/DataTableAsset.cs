using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ProjectT.DataTable
{
    /// <summary>
    /// 컬럼 하나의 이름과 타입(int/float/bool/string)을 저장합니다.
    /// </summary>
    [Serializable]
    public class ColumnDef
    {
        public string name;
        public ColumnType type;
    }

    public enum ColumnType
    {
        Int,
        Float,
        Bool,
        String
    }

    /// <summary>
    /// 한 행(row)의 데이터. 값은 원본 문자열 그대로 컬럼 순서에 맞춰 저장되고,
    /// 컬럼 이름으로 조회할 때 타입에 맞게 파싱됩니다.
    /// 새 컬럼이 추가되어도(스키마 확장) 기존 코드가 깨지지 않도록 이름 기반 조회를 사용합니다.
    /// </summary>
    [Serializable]
    public class DataRow
    {
        public List<string> cells = new List<string>();

        [NonSerialized] private Dictionary<string, string> _byName;

        public void BuildIndex(List<ColumnDef> columns)
        {
            _byName = new Dictionary<string, string>(columns.Count);
            for (int i = 0; i < columns.Count && i < cells.Count; i++)
            {
                _byName[columns[i].name] = cells[i];
            }
        }

        public int GetInt(string column, int defaultValue = 0)
        {
            if (_byName != null && _byName.TryGetValue(column, out var v) &&
                int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r))
            {
                return r;
            }
            return defaultValue;
        }

        public float GetFloat(string column, float defaultValue = 0f)
        {
            if (_byName != null && _byName.TryGetValue(column, out var v) &&
                float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var r))
            {
                return r;
            }
            return defaultValue;
        }

        public bool GetBool(string column, bool defaultValue = false)
        {
            if (_byName == null || !_byName.TryGetValue(column, out var v)) return defaultValue;
            v = v.Trim();
            if (bool.TryParse(v, out var b)) return b;
            if (v == "1") return true;
            if (v == "0") return false;
            return defaultValue;
        }

        public string GetString(string column, string defaultValue = "")
        {
            if (_byName != null && _byName.TryGetValue(column, out var v)) return v;
            return defaultValue;
        }

        public bool HasColumn(string column) => _byName != null && _byName.ContainsKey(column);
    }

    /// <summary>
    /// 엑셀/CSV에서 임포트한 테이블 하나를 담는 ScriptableObject.
    /// Addressable로 등록해서 런타임에 비동기로 로드/패치할 수 있습니다.
    /// </summary>
    [CreateAssetMenu(fileName = "NewDataTable", menuName = "Data Table/Data Table Asset")]
    public class DataTableAsset : ScriptableObject
    {
        [Tooltip("TableManager.Get(tableName)으로 조회할 때 쓰는 키")]
        public string tableName;

        [Tooltip("행을 식별하는 ID로 사용할 컬럼 이름")]
        public string idColumn = "Id";

        public List<ColumnDef> columns = new List<ColumnDef>();
        public List<DataRow> rows = new List<DataRow>();

        private Dictionary<int, DataRow> _byId;

        private void OnEnable()
        {
            BuildIndex();
        }

        public void BuildIndex()
        {
            _byId = new Dictionary<int, DataRow>(rows.Count);
            foreach (var row in rows)
            {
                row.BuildIndex(columns);
                var idStr = row.GetString(idColumn, null);
                if (idStr != null && int.TryParse(idStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                {
                    _byId[id] = row;
                }
            }
        }

        /// <summary>ID로 행을 조회합니다. 없으면 null.</summary>
        public DataRow GetRow(int id)
        {
            if (_byId == null) BuildIndex();
            return _byId.TryGetValue(id, out var row) ? row : null;
        }

        public IReadOnlyList<DataRow> Rows => rows;

        public int RowCount => rows.Count;
    }
}

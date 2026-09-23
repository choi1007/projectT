using ProjectT.DataTable;
using UnityEngine;

namespace ProjectT.Game
{
    /// <summary>
    /// 씬에 배치된 아이템 큐브 (아이템 Id 하나당 큐브 하나).
    ///
    /// 씬에는 아이템 Id만 저장하고, 이름/가격/무게/겹치기 여부는 게임 시작 시 Item 테이블에서 읽어옵니다.
    /// 그래서 테이블이 패치되어 값이 바뀌어도 씬을 고칠 필요가 없습니다.
    /// Play 중에는 아래 "테이블에서 읽어온 값"을 인스펙터에서 확인할 수 있습니다.
    /// </summary>
    public class ItemCube : MonoBehaviour
    {
        private const string TableName = "Item";

        [Tooltip("Item 테이블의 Id")]
        [SerializeField] private int itemId;

        [Header("테이블에서 읽어온 값 (Play 중에 채워짐)")]
        [SerializeField] private string itemName;
        [SerializeField] private int price;
        [SerializeField] private float weight;
        [SerializeField] private bool stackable;

        public int ItemId => itemId;
        public string ItemName => itemName;
        public int Price => price;
        public float Weight => weight;
        public bool Stackable => stackable;

        /// <summary>테이블 값이 채워졌는지 여부</summary>
        public bool IsLoaded { get; private set; }

        private async void Start()
        {
            try
            {
                // 패치 확인 + 테이블 로딩이 끝날 때까지 대기
                await AddressablesBootstrap.Ready;
            }
            catch (System.Exception)
            {
                if (this == null) return;
                Debug.LogError($"[ItemCube] {name}: 테이블 로딩에 실패해서 데이터를 채우지 못했습니다. 위쪽 [AddressablesBootstrap] 에러를 확인하세요.", this);
                return;
            }

            // 기다리는 동안 오브젝트가 파괴되었을 수 있음
            if (this == null) return;

            ApplyTableData();
        }

        private void ApplyTableData()
        {
            IsLoaded = false;
            if (!TableManager.TryGet(TableName, out var table))
            {
                Debug.LogError($"[ItemCube] {name}: '{TableName}' 테이블이 로드되지 않았습니다.", this);
                return;
            }

            if (!table.HasColumn("Id", ColumnType.Int) ||
                !table.HasColumn("Name", ColumnType.String) ||
                !table.HasColumn("Price", ColumnType.Int) ||
                !table.HasColumn("Weight", ColumnType.Float) ||
                !table.HasColumn("Stackable", ColumnType.Bool))
            {
                Debug.LogError($"[ItemCube] {name}: Item 필수 컬럼이 없거나 타입이 잘못되었습니다. Id:int, Name:string, Price:int, Weight:float, Stackable:bool이 필요합니다.", this);
                return;
            }

            var row = table.GetRow(itemId);
            if (row == null)
            {
                Debug.LogError($"[ItemCube] {name}: Item 테이블에 Id {itemId}이(가) 없습니다.", this);
                return;
            }

            itemName = row.GetString("Name");
            price = row.GetInt("Price");
            weight = row.GetFloat("Weight");
            stackable = row.GetBool("Stackable");
            IsLoaded = true;

            Debug.Log($"[ItemCube] Id={itemId} Name={itemName} Price={price} Weight={weight} Stackable={stackable}", this);
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

public class UtilEx : MonoBehaviour
{
    // 오브젝트에 컴포넌트를 붙이고 플레이하면 모든 예제를 한 번 실행합니다.
    private void Start()
    {
        RunAllExamples();
    }

    // 컴포넌트의 우클릭 메뉴에서 전체 또는 개별 예제를 실행할 수도 있습니다.
    [ContextMenu("예제/전체 실행")]
    public void RunAllExamples()
    {
        ExampleCurrentTime();
        ExampleDateTimeConversion();
        ExampleOffsetConversion();
        ExampleListSort();
        ExampleArraySort();
        ExampleCustomComparer();
    }

    [ContextMenu("예제/현재 UTC+8 시각")]
    public void ExampleCurrentTime()
    {
        DateTimeOffset now = Utility.GetUtc8Now();
        Debug.Log($"[현재 시각] UTC+8: {now:yyyy-MM-dd HH:mm:ss zzz}", this);
    }

    [ContextMenu("예제/UTC 시각 변환")]
    public void ExampleDateTimeConversion()
    {
        // 시간대가 없는 DateTime 대신 DateTimeKind.Utc를 지정합니다.
        DateTime utc = new DateTime(2026, 10, 8, 20, 30, 0, DateTimeKind.Utc);
        DateTimeOffset converted = Utility.ToUtc8(utc);
        // 결과: 2026-10-09 04:30:00 +08:00 (다음 날로 변경)
        Debug.Log($"[UTC 변환] {utc:yyyy-MM-dd HH:mm:ss} UTC → {converted:yyyy-MM-dd HH:mm:ss zzz}", this);
    }

    [ContextMenu("예제/다른 시간대에서 UTC+8 변환")]
    public void ExampleOffsetConversion()
    {
        DateTimeOffset koreaTime = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.FromHours(9));
        DateTimeOffset converted = Utility.ToUtc8(koreaTime);
        // 결과: 12:00 +09:00 → 11:00 +08:00. 같은 순간을 다른 시간대로 표현합니다.
        Debug.Log($"[시간대 변환] {koreaTime:HH:mm zzz} → {converted:HH:mm zzz}", this);
    }

    [ContextMenu("예제/리스트 가격순 정렬")]
    public void ExampleListSort()
    {
        var items = new List<ExampleItem>
        {
            new ExampleItem("검", 300),
            new ExampleItem("체력 물약", 50),
            new ExampleItem("마나 물약", 50)
        };

        // 정렬 기준은 Price입니다. 기본값은 오름차순이며 새 리스트를 반환합니다.
        List<ExampleItem> ascending = Utility.Sort(items, item => item.Price);
        List<ExampleItem> descending = Utility.Sort(items, item => item.Price, descending: true);
        // 같은 가격의 체력 물약과 마나 물약은 입력 순서를 유지합니다.
        Debug.Log($"[리스트 원본] {string.Join(", ", items)}", this);
        Debug.Log($"[가격 오름차순] {string.Join(", ", ascending)}", this);
        Debug.Log($"[가격 내림차순] {string.Join(", ", descending)}", this);
    }

    [ContextMenu("예제/숫자 배열 정렬")]
    public void ExampleArraySort()
    {
        int[] numbers = { 30, 10, 20, 10 };
        // 숫자 자체를 정렬 기준으로 지정합니다. 원본 배열은 바뀌지 않습니다.
        int[] ascending = Utility.Sort(numbers, number => number);
        int[] descending = Utility.Sort(numbers, number => number, descending: true);
        Debug.Log($"[배열 원본] {string.Join(", ", numbers)}", this);
        Debug.Log($"[배열 오름차순] {string.Join(", ", ascending)}", this);
        Debug.Log($"[배열 내림차순] {string.Join(", ", descending)}", this);
    }

    [ContextMenu("예제/문자열 비교 기준 지정")]
    public void ExampleCustomComparer()
    {
        string[] names = { "banana", "Cherry", "apple" };
        // 대소문자를 구분하지 않는 비교 방법을 직접 넘깁니다.
        string[] sorted = Utility.Sort(names, name => name, comparer: StringComparer.OrdinalIgnoreCase);
        // 결과: apple, banana, Cherry
        Debug.Log($"[대소문자 무시 정렬] {string.Join(", ", sorted)}", this);
    }

    // 리스트 정렬 예제에서만 사용하는 간단한 데이터입니다.
    private sealed class ExampleItem
    {
        public string Name { get; }
        public int Price { get; }

        public ExampleItem(string name, int price)
        {
            Name = name;
            Price = price;
        }

        public override string ToString() => $"{Name}({Price})";
    }
}

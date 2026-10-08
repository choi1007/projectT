using System;
using System.Collections.Generic;
using System.Linq;

public static class Utility
{
    private static readonly TimeSpan Utc8Offset = TimeSpan.FromHours(8);

    /// <summary>같은 시각을 UTC+8 기준으로 변환합니다.</summary>
    public static DateTimeOffset ToUtc8(DateTimeOffset time)
    {
        return time.ToOffset(Utc8Offset);
    }

    /// <summary>UTC 또는 로컬 시각을 UTC+8로 변환합니다. 시간대가 없는 값은 허용하지 않습니다.</summary>
    public static DateTimeOffset ToUtc8(DateTime time)
    {
        if (time.Kind == DateTimeKind.Unspecified)
            throw new ArgumentException("UTC 또는 로컬 시간대가 지정된 값을 사용하세요.", nameof(time));

        return ToUtc8(new DateTimeOffset(time));
    }

    /// <summary>현재 시각을 UTC+8 기준으로 반환합니다.</summary>
    public static DateTimeOffset GetUtc8Now() => ToUtc8(DateTimeOffset.UtcNow);

    /// <summary>지정한 기준으로 정렬한 새 리스트를 반환합니다. 원본과 같은 기준값끼리의 순서는 유지됩니다.</summary>
    /// <param name="source">정렬할 리스트</param>
    /// <param name="keySelector">정렬 기준값을 선택하는 함수. 예: item => item.Price</param>
    /// <param name="descending">true이면 내림차순, false이면 오름차순</param>
    /// <param name="comparer">기준값 비교 방법. 생략하면 해당 타입의 기본 비교를 사용합니다.</param>
    public static List<T> Sort<T, TKey>(List<T> source, Func<T, TKey> keySelector,
        bool descending = false, IComparer<TKey> comparer = null)
    {
        return Order(source, keySelector, descending, comparer).ToList();
    }

    /// <summary>지정한 기준으로 정렬한 새 배열을 반환합니다. 원본과 같은 기준값끼리의 순서는 유지됩니다.</summary>
    /// <param name="source">정렬할 배열</param>
    /// <param name="keySelector">정렬 기준값을 선택하는 함수. 예: item => item.Price</param>
    /// <param name="descending">true이면 내림차순, false이면 오름차순</param>
    /// <param name="comparer">기준값 비교 방법. 생략하면 해당 타입의 기본 비교를 사용합니다.</param>
    public static T[] Sort<T, TKey>(T[] source, Func<T, TKey> keySelector,
        bool descending = false, IComparer<TKey> comparer = null)
    {
        return Order(source, keySelector, descending, comparer).ToArray();
    }

    private static IOrderedEnumerable<T> Order<T, TKey>(IEnumerable<T> source,
        Func<T, TKey> keySelector, bool descending, IComparer<TKey> comparer)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));

        return descending
            ? source.OrderByDescending(keySelector, comparer)
            : source.OrderBy(keySelector, comparer);
    }
}

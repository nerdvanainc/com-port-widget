namespace ComPortWidget.Core.Listing;

/// <summary>새로 만든 목록을 기존 목록과 맞춰, 내용이 같은 항목은 기존 개체를 그대로 쓰게 한다.</summary>
public static class ListMerge
{
    /// <summary>
    /// desired의 각 항목에 대해, current에 same이 참인 항목이 같은 자리에 있으면 그 기존 개체를 쓴다.
    /// 돌려준 목록이 current와 개체까지 같으면 화면을 건드릴 필요가 없다.
    /// </summary>
    public static List<T> Merge<T>(IReadOnlyList<T> current, IReadOnlyList<T> desired, Func<T, T, bool> same)
        where T : class
    {
        var result = new List<T>(desired.Count);
        for (var i = 0; i < desired.Count; i++)
            result.Add(i < current.Count && same(current[i], desired[i]) ? current[i] : desired[i]);
        return result;
    }

    /// <summary>두 목록이 같은 개체를 같은 순서로 담고 있는지.</summary>
    public static bool SameInstances<T>(IReadOnlyList<T> a, IReadOnlyList<T> b) where T : class
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
            if (!ReferenceEquals(a[i], b[i])) return false;
        return true;
    }
}

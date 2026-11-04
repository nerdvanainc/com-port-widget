namespace ComPortWidget.Core.Listing;

/// <summary>
/// 목록을 다시 만들어도 되는 때를 정한다. 메모 편집 중이거나 우클릭 메뉴가 열려 있으면 미루고,
/// 풀리는 순간 미룬 갱신이 있으면 알려 준다.
/// </summary>
public sealed class RebuildGate
{
    private bool _editing, _menuOpen, _pending;

    public bool IsBlocked => _editing || _menuOpen;

    /// <summary>다시 만들기 직전에 부른다. 막혀 있으면 미룸 표시를 남기고 false.</summary>
    public bool TryEnter()
    {
        if (IsBlocked)
        {
            _pending = true;
            return false;
        }
        _pending = false;
        return true;
    }

    /// <summary>편집 상태를 바꾼다. 막힘이 풀리고 미룬 갱신이 있으면 true.</summary>
    public bool SetEditing(bool value) { _editing = value; return Released(); }

    /// <summary>메뉴 열림 상태를 바꾼다. 막힘이 풀리고 미룬 갱신이 있으면 true.</summary>
    public bool SetMenuOpen(bool value) { _menuOpen = value; return Released(); }

    private bool Released() => !IsBlocked && _pending;
}

/// <summary>동시에 한 줄만 편집하도록 현재 편집 중인 줄을 추적한다.</summary>
public sealed class EditTracker<T> where T : class
{
    public T? Current { get; private set; }

    /// <summary>row의 편집을 시작한다. 다른 줄이 편집 중이었으면 저장해야 하므로 그 줄을 돌려준다.</summary>
    public T? Begin(T row)
    {
        var previous = Current is not null && !ReferenceEquals(Current, row) ? Current : null;
        Current = row;
        return previous;
    }

    /// <summary>row의 편집을 끝낸다. row가 현재 편집 중이던 줄이면 true.</summary>
    public bool End(T row)
    {
        if (!ReferenceEquals(Current, row)) return false;
        Current = null;
        return true;
    }
}

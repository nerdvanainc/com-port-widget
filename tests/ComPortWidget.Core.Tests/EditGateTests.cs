using ComPortWidget.Core.Listing;
using ComPortWidget.Core.Text;

namespace ComPortWidget.Core.Tests;

public class EditGateTests
{
    private sealed class Item(string name) { public string Name { get; } = name; }

    [Fact]
    public void Gate_defers_while_editing_and_reports_release()
    {
        var g = new RebuildGate();
        Assert.True(g.TryEnter());
        g.SetEditing(true);
        Assert.False(g.TryEnter());
        Assert.True(g.SetEditing(false)); // 미룬 갱신이 있다
        Assert.True(g.TryEnter());
        Assert.False(g.SetEditing(false)); // 미룬 갱신 없음
    }

    [Fact]
    public void Gate_stays_blocked_until_both_menu_and_edit_end()
    {
        var g = new RebuildGate();
        g.SetEditing(true);
        g.SetMenuOpen(true);
        Assert.False(g.TryEnter());
        Assert.False(g.SetEditing(false)); // 메뉴가 아직 열려 있다
        Assert.True(g.SetMenuOpen(false));
    }

    [Fact]
    public void Tracker_begin_returns_previous_row_to_commit()
    {
        var t = new EditTracker<Item>();
        var a = new Item("a"); var b = new Item("b");
        Assert.Null(t.Begin(a));
        Assert.Same(a, t.Begin(b));
        Assert.Same(b, t.Current);
        Assert.Null(t.Begin(b)); // 같은 줄이면 저장할 이전 줄이 없다
    }

    [Fact]
    public void Tracker_end_only_clears_current_row()
    {
        var t = new EditTracker<Item>();
        var a = new Item("a"); var b = new Item("b");
        t.Begin(a);
        Assert.False(t.End(b));
        Assert.Same(a, t.Current);
        Assert.True(t.End(a));
        Assert.Null(t.Current);
    }

    [Fact]
    public void Merge_reuses_existing_instances_when_same()
    {
        var a = new Item("a"); var b = new Item("b");
        var merged = ListMerge.Merge(new[] { a, b }, new[] { new Item("a"), new Item("b") }, (x, y) => x.Name == y.Name);
        Assert.True(ListMerge.SameInstances(new[] { a, b }, merged));
    }

    [Fact]
    public void Merge_replaces_only_changed_and_added_items()
    {
        var a = new Item("a"); var b = new Item("b");
        var newB = new Item("B2"); var c = new Item("c");
        var merged = ListMerge.Merge(new[] { a, b }, new[] { new Item("a"), newB, c }, (x, y) => x.Name == y.Name);
        Assert.Same(a, merged[0]);
        Assert.Same(newB, merged[1]);
        Assert.Same(c, merged[2]);
        Assert.False(ListMerge.SameInstances(new[] { a, b }, merged));
    }

    [Fact]
    public void Merge_handles_shrinking_list()
    {
        var a = new Item("a"); var b = new Item("b");
        var merged = ListMerge.Merge(new[] { a, b }, new[] { new Item("a") }, (x, y) => x.Name == y.Name);
        Assert.Single(merged);
        Assert.Same(a, merged[0]);
    }

    [Theory]
    [InlineData("a\r\nb\nc\rd", "a b c d")]
    [InlineData("  x\n", "x")]
    [InlineData("", "")]
    public void Memo_normalize_flattens_line_breaks(string input, string expected)
        => Assert.Equal(expected, MemoText.Normalize(input));

    [Fact]
    public void Memo_normalize_limits_length()
        => Assert.Equal(500, MemoText.Normalize(new string('x', 800)).Length);

    [Fact]
    public void Memo_flatten_keeps_all_text_of_multiline_paste()
        => Assert.Equal("one two three", MemoText.FlattenLineBreaks("one\ntwo\r\nthree"));
}

using MikeShift.Core.Editing;

namespace MikeShift.Core.Tests.Editing;

public class FileOrderModelTests
{
    private static FileOrderModel Model(params string[] names) => new(names.Select(n => @"C:\in\" + n));

    private static string[] Names(FileOrderModel m) => [.. m.Paths.Select(p => Path.GetFileName(p)!)];

    [Fact]
    public void Starts_in_the_order_it_was_given_and_is_not_changed()
    {
        var m = Model("b.pdf", "a.pdf", "c.pdf");

        Assert.Equal(["b.pdf", "a.pdf", "c.pdf"], Names(m));
        Assert.Equal(3, m.Count);
        Assert.False(m.IsChanged);
    }

    [Fact]
    public void MoveUp_and_MoveDown_move_one_place_and_return_the_new_index()
    {
        var m = Model("a", "b", "c");
        var raised = 0;
        m.Changed += () => raised++;

        Assert.Equal(0, m.MoveUp(1));
        Assert.Equal(["b", "a", "c"], Names(m));
        Assert.Equal(1, m.MoveDown(0));
        Assert.Equal(["a", "b", "c"], Names(m));
        Assert.Equal(2, raised);
    }

    [Fact]
    public void Moving_past_either_end_changes_nothing()
    {
        var m = Model("a", "b", "c");
        var raised = 0;
        m.Changed += () => raised++;

        Assert.False(m.CanMoveUp(0));
        Assert.False(m.CanMoveDown(2));
        Assert.Equal(0, m.MoveUp(0));
        Assert.Equal(2, m.MoveDown(2));
        Assert.Equal(["a", "b", "c"], Names(m));
        Assert.Equal(0, raised);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void CanMove_is_false_for_an_index_outside_the_list(int index)
    {
        var m = Model("a", "b", "c");
        Assert.False(m.CanMoveUp(index));
        Assert.False(m.CanMoveDown(index));
        Assert.Equal(index, m.MoveUp(index));
        Assert.Equal(index, m.MoveDown(index));
        Assert.Equal(index, m.Move(index, 0));
        Assert.False(m.IsChanged);
    }

    [Theory]
    [InlineData(0, 2, new[] { "b", "c", "a" })]
    [InlineData(2, 0, new[] { "c", "a", "b" })]
    [InlineData(1, 1, new[] { "a", "b", "c" })]
    [InlineData(0, 99, new[] { "b", "c", "a" })]
    [InlineData(2, -5, new[] { "c", "a", "b" })]
    public void Move_puts_the_file_at_any_place_clamped_to_the_list(int from, int to, string[] expected)
    {
        var m = Model("a", "b", "c");

        var landed = m.Move(from, to);

        Assert.Equal(expected, Names(m));
        Assert.Equal(Array.IndexOf(expected, new[] { "a", "b", "c" }[from]), landed);
    }

    [Fact]
    public void SetOrder_takes_over_a_reordering_of_the_same_files_and_says_so()
    {
        var m = Model("a", "b", "c");
        var raised = 0;
        m.Changed += () => raised++;

        m.SetOrder([@"C:\in\c", @"C:\in\a", @"C:\in\b"]);
        m.SetOrder([@"C:\in\c", @"C:\in\a", @"C:\in\b"]); // the same again: nothing to report

        Assert.Equal(["c", "a", "b"], Names(m));
        Assert.True(m.IsChanged);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void SetOrder_refuses_other_files_and_keeps_the_order()
    {
        var m = Model("a", "b", "c");

        Assert.Throws<ArgumentException>(() => m.SetOrder([@"C:\in\a", @"C:\in\b"]));
        Assert.Throws<ArgumentException>(() => m.SetOrder([@"C:\in\a", @"C:\in\b", @"C:\in\x"]));
        Assert.Equal(["a", "b", "c"], Names(m));
        Assert.False(m.IsChanged);
    }

    [Fact]
    public void IsChanged_compares_with_the_start_not_with_the_last_move()
    {
        var m = Model("a", "b", "c");

        m.MoveDown(0);
        Assert.True(m.IsChanged);
        m.MoveUp(1);
        Assert.False(m.IsChanged);
    }

    [Fact]
    public void SortByName_reads_numbers_as_numbers_and_ignores_case()
    {
        var m = Model("scan 10.pdf", "Scan 2.pdf", "scan 1.pdf", "SCAN 20.pdf");

        m.SortByName();

        Assert.Equal(["scan 1.pdf", "Scan 2.pdf", "scan 10.pdf", "SCAN 20.pdf"], Names(m));
        Assert.True(m.IsChanged);
    }

    [Fact]
    public void SortByName_orders_by_file_name_not_by_folder()
    {
        var m = new FileOrderModel([@"C:\z\a.pdf", @"C:\a\c.pdf", @"C:\m\b.pdf"]);

        m.SortByName();

        Assert.Equal([@"C:\z\a.pdf", @"C:\m\b.pdf", @"C:\a\c.pdf"], m.Paths);
    }

    [Fact]
    public void SortByName_keeps_files_with_the_same_name_in_their_order_and_is_quiet_when_nothing_moves()
    {
        var m = new FileOrderModel([@"C:\two\a.pdf", @"C:\one\a.pdf", @"C:\one\b.pdf"]);
        var raised = 0;
        m.Changed += () => raised++;

        m.SortByName();

        Assert.Equal([@"C:\two\a.pdf", @"C:\one\a.pdf", @"C:\one\b.pdf"], m.Paths);
        Assert.Equal(0, raised);
        Assert.False(m.IsChanged);
    }

    [Theory]
    [InlineData("a2", "a10", -1)]
    [InlineData("a10", "a2", 1)]
    [InlineData("a2", "a2", 0)]
    [InlineData("a002", "a3", -1)]
    [InlineData("1a", "a1", -1)]
    [InlineData("a", "a1", -1)]
    [InlineData("B", "a", 1)]
    [InlineData("", "a", -1)]
    public void CompareNames_is_natural(string a, string b, int sign) =>
        Assert.Equal(sign, Math.Sign(FileOrderModel.CompareNames(a, b)));

    [Fact]
    public void CompareNames_tells_equal_values_with_different_zeros_apart_so_the_sort_is_deterministic()
    {
        Assert.NotEqual(0, FileOrderModel.CompareNames("a01", "a1"));
        Assert.Equal(-FileOrderModel.CompareNames("a01", "a1"), FileOrderModel.CompareNames("a1", "a01"));
        Assert.Equal(0, FileOrderModel.CompareNames(null, ""));
    }

    [Fact]
    public void IsPermutation_accepts_any_order_of_the_same_files_ignoring_case()
    {
        string[] selection = [@"C:\in\a.pdf", @"C:\in\b.pdf", @"C:\in\c.pdf"];

        Assert.True(FileOrderModel.IsPermutation(selection, [@"C:\in\c.pdf", @"C:\in\A.PDF", @"C:\in\b.pdf"]));
        Assert.True(FileOrderModel.IsPermutation(selection, selection));
        Assert.True(FileOrderModel.IsPermutation([], []));
    }

    [Fact]
    public void IsPermutation_rejects_a_dropped_added_or_repeated_file()
    {
        string[] selection = [@"C:\in\a.pdf", @"C:\in\b.pdf"];

        Assert.False(FileOrderModel.IsPermutation(selection, [@"C:\in\a.pdf"]));
        Assert.False(FileOrderModel.IsPermutation(selection, [@"C:\in\a.pdf", @"C:\in\b.pdf", @"C:\in\c.pdf"]));
        Assert.False(FileOrderModel.IsPermutation(selection, [@"C:\in\a.pdf", @"C:\in\c.pdf"]));
        Assert.False(FileOrderModel.IsPermutation(selection, [@"C:\in\a.pdf", @"C:\in\a.pdf"]));
    }
}

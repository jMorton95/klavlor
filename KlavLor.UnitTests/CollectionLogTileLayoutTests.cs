using KlavLor.Web.Application.Features.CollectionLog;

namespace KlavLor.UnitTests;

// The rule these pin: the focus panel's tiles are sized to FILL the panel, so the column count comes
// from the item count and the panel's shape - never from an auto-fill track width. The version this
// replaced laid tiles out at a fixed 5.25rem in a wrapping flex row, which on a 2K screen put all ten
// of Duke Sucellus' items in one thin row with the rest of the panel empty. That is the regression
// worth an assertion: a count that fits on one line is exactly the case a width-driven layout gets
// wrong, and it looks plausible right up until the screen is wide.
public sealed class CollectionLogTileLayoutTests
{
    [Theory]
    // The reported case. The wide cell is about 2:1, so ten square tiles fill it as 5x2 - a single
    // row of ten would make each tile a fifth of the height it could be.
    [InlineData(10, true, 5)]
    // Nine goes 5+4 rather than 3x3: two rows of five fit a bigger square than three of three,
    // and a ragged last row costs nothing when the alternative is smaller tiles.
    [InlineData(9, true, 5)]
    [InlineData(6, true, 3)]
    [InlineData(4, true, 2)]
    public void WidePanel_spreadsItemsOverRowsRatherThanOneLine(int count, bool isWide, int expected)
    {
        Assert.Equal(expected, CollectionLogItemGrid.ColumnsFor(count, isWide));
    }

    [Theory]
    // The standard cell is TALLER than it is wide, so the same counts want the opposite shape.
    [InlineData(3, false, 1)]
    [InlineData(1, false, 1)]
    public void TallPanel_stacksRatherThanSpreads(int count, bool isWide, int expected)
    {
        Assert.Equal(expected, CollectionLogItemGrid.ColumnsFor(count, isWide));
    }

    [Fact]
    public void NoLayoutEverExceedsTheColumnCeiling_soLargeLogsScrollInsteadOfShrinking()
    {
        // Past eight columns a tile is smaller than the fixed size this replaced, which defeats the
        // point: a hundred-item log is meant to scroll, not to be rendered as a hundred specks.
        foreach (var count in new[] { 20, 50, 100, 400 })
        {
            Assert.InRange(CollectionLogItemGrid.ColumnsFor(count, true), 1, 8);
            Assert.InRange(CollectionLogItemGrid.ColumnsFor(count, false), 1, 8);
        }
    }

    [Fact]
    public void ChosenLayoutMaximisesTileSize()
    {
        // The property the search exists for, asserted independently of the search: for every count,
        // no other column count admits a larger square (within the tie tolerance the method allows).
        foreach (var isWide in new[] { true, false })
        {
            var aspect = isWide ? 2.0 : 0.62;
            for (var count = 1; count <= 40; count++)
            {
                var chosen = CollectionLogItemGrid.ColumnsFor(count, isWide);
                var chosenSide = Side(count, chosen, aspect);
                for (var columns = 1; columns <= Math.Min(count, 8); columns++)
                    Assert.True(Side(count, columns, aspect) <= chosenSide * 1.03,
                        $"count {count} (wide: {isWide}) chose {chosen} columns, but {columns} fits a bigger tile");
            }
        }

        static double Side(int count, int columns, double aspect) =>
            Math.Min(aspect / columns, 1.0 / Math.Ceiling(count / (double)columns));
    }
}

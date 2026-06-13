using System;
using Xunit;
using SanitizerKit.Core.Patching;

namespace NoBOMSuite.Tests;

public class DiffHelperTests
{
    [Fact]
    public void ComputeDiff_Should_Detect_Added_And_Deleted_Lines()
    {
        string original = "line1\nline2\nline3";
        string newText = "line1\nline4\nline3";

        var diff = DiffHelper.ComputeDiff(original, newText);

        Assert.Equal(4, diff.Count);

        Assert.Equal(DiffType.Unchanged, diff[0].Type);
        Assert.Equal("line1", diff[0].Text);

        Assert.Equal(DiffType.Deleted, diff[1].Type);
        Assert.Equal("line2", diff[1].Text);

        Assert.Equal(DiffType.Added, diff[2].Type);
        Assert.Equal("line4", diff[2].Text);

        Assert.Equal(DiffType.Unchanged, diff[3].Type);
        Assert.Equal("line3", diff[3].Text);
    }

    [Fact]
    public void ComputeDiff_Should_Handle_Empty_Inputs_Gracefully()
    {
        var diff = DiffHelper.ComputeDiff(string.Empty, "hello");
        Assert.Single(diff);
        Assert.Equal(DiffType.Added, diff[0].Type);
        Assert.Equal("hello", diff[0].Text);
    }
}

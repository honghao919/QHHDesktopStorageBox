using QHHDesktopStorageBox.App.Infrastructure;

namespace QHHDesktopStorageBox.App.Tests;

public sealed class SearchTextNormalizerTests
{
    [Theory]
    [InlineData("gzwd", true)]
    [InlineData("GONGZUO", true)]
    [InlineData("工作", true)]
    [InlineData("missing", false)]
    public void Matches_SupportsChinesePinyinAndInitials(string query, bool expected)
    {
        Assert.Equal(
            expected,
            SearchTextNormalizer.Matches(query, "工作文档.pdf"));
    }
}

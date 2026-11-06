using TopSpin4.Launcher.Core;

namespace TopSpin4.Launcher.Tests;

public sealed class FooterInfoTests
{
    [Theory]
    [InlineData("0.1.0+cd2c6fc0123456789", "0.1.0 (cd2c6fc)")]
    [InlineData("0.1.0+abc", "0.1.0 (abc)")]
    [InlineData("0.1.0", "0.1.0")]
    [InlineData(null, "dev")]
    public void The_version_shows_the_short_commit(string? informational, string expected) =>
        Assert.Equal(expected, FooterInfo.Version(informational));

    [Theory]
    [InlineData("ts4_renderer = \"native\"\n", "Native renderer")]
    [InlineData("version = 3\nts4_renderer = \"classic\"\n", "Classic renderer")]
    [InlineData("version = 2\nts4_renderer = \"classic\"\n", "Native renderer")]
    [InlineData("other = 1\n", "Native renderer")]
    [InlineData(null, "Native renderer")]
    public void The_renderer_is_the_settings_choice_or_the_games_default(string? text, string expected) =>
        Assert.Equal(expected, FooterInfo.Renderer(text));

    [Fact]
    public void The_line_leaves_out_what_is_not_known()
    {
        Assert.Equal("Version 1  ·  Native renderer  ·  Game files: D:\\TS4", FooterInfo.Line("1", "Native renderer", null, "D:\\TS4"));
    }
}

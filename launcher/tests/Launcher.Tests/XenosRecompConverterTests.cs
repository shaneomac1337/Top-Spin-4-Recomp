using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using TopSpin4.Launcher.Core;

namespace TopSpin4.Launcher.Tests;

public sealed class XenosRecompConverterTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ts4 xenos ").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static string RepoRoot([CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", ".."));

    [Fact]
    public void The_converter_id_is_the_one_the_game_build_expects()
    {
        // CMakeLists.txt and scripts/convert-shaders.ps1: the first 16 hex digits of the SHA-256 of the series' lower-case hex text.
        var series = new StringBuilder();
        foreach (var patch in Directory.EnumerateFiles(Path.Combine(RepoRoot(), "patches", "xenosrecomp"), "*.patch")
                     .Order(StringComparer.Ordinal))
            series.Append(Convert.ToHexStringLower(File.ReadAllBytes(patch)));
        var fromPatches = Convert.ToUInt64(Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(series.ToString())))[..16], 16);

        Assert.Equal(ShaderConverter.Id, fromPatches);
    }

    [Fact]
    public void A_missing_converter_is_a_plain_failure_of_the_shader_step()
    {
        TestArc.Lzx().Add(TestArc.Container(vertex: false, 4)).WriteTo(Path.Combine(_root, "gamedata", "Arena.arc"));
        var converter = new XenosRecompConverter(Path.Combine(_root, "xenosrecomp", XenosRecompConverter.ExecutableName));

        var error = Assert.Throws<ShaderPreparationException>(() =>
            new ShaderPreparation(converter, ShaderConverter.Id).Run(Path.Combine(_root, "gamedata"), Path.Combine(_root, "cache"),
                null, TestContext.Current.CancellationToken));

        Assert.Contains("shader converter is missing", error.Message);
        Assert.Contains("try again", error.Message);
    }

    [Fact]
    public void The_release_ships_it_beside_the_games_folder_or_inside_the_launchers()
    {
        var launcher = Path.Combine(_root, "install");
        Directory.CreateDirectory(launcher);

        Assert.Equal(Path.Combine(_root, "xenosrecomp", "XenosRecomp.exe"), XenosRecompConverter.ShippedWith(launcher).Executable);

        var inside = Path.Combine(launcher, "xenosrecomp", "XenosRecomp.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(inside)!);
        File.WriteAllText(inside, "x");
        Assert.Equal(inside, XenosRecompConverter.ShippedWith(launcher).Executable);

        var beside = Path.Combine(_root, "xenosrecomp", "XenosRecomp.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(beside)!);
        File.WriteAllText(beside, "x");
        Assert.Equal(beside, XenosRecompConverter.ShippedWith(launcher).Executable);
    }
}

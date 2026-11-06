using System.Buffers.Binary;
using System.Text;
using TopSpin4.Launcher.Core;

namespace TopSpin4.Launcher.Tests;

public sealed class ShaderPreparationTests : IDisposable
{
    private const ulong ConverterId = 0x1122334455667788;

    private readonly string _root = Directory.CreateTempSubdirectory("ts4 shaders ").FullName;
    private readonly FakeConverter _converter = new();
    private readonly FakeModules _modules = new();

    private static readonly byte[] PixelContainer = TestArc.Container(vertex: false, 4, seed: 1);
    private static readonly byte[] VertexContainer = TestArc.Container(vertex: true, 5, seed: 2);
    private static readonly string Pixel = ShaderContainer.Of(PixelContainer).Name;
    private static readonly string Vertex = ShaderContainer.Of(VertexContainer).Name;

    public ShaderPreparationTests()
    {
        TestArc.Lzx().Add(PixelContainer).Add(VertexContainer).WriteTo(Path.Combine(GameData, "Arena.arc"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string GameData => Path.Combine(_root, "gamedata");
    private string Cache => Path.Combine(_root, "cache");
    private string PackPath => Path.Combine(Cache, ShaderPackFile.FileName);
    private string TopUp => Path.Combine(Cache, ShaderPreparation.TopUpFolderName);

    private ShaderPreparation Preparation(bool withModules = false) => new(_converter, ConverterId, withModules ? _modules : null);

    private void Run(IProgress<ShaderProgress>? progress = null, CancellationToken? cancel = null, bool withModules = false) =>
        Preparation(withModules).Run(GameData, Cache, progress, cancel ?? TestContext.Current.CancellationToken);

    private IEnumerable<string> CacheEntries() =>
        Directory.Exists(Cache) ? Directory.EnumerateFileSystemEntries(Cache).Select(Path.GetFileName).Order()! : [];

    [Fact]
    public void Success_converts_the_archive_shaders_and_writes_a_pack_the_game_accepts()
    {
        var reports = new List<ShaderProgress>();

        Run(new SyncProgress<ShaderProgress>(reports.Add));

        Assert.Equal([Pixel + ".bin", Vertex + ".bin"], _converter.Inputs.Order());
        Assert.True(ShaderPackFile.Check(PackPath, ConverterId).Ready);
        Assert.Equal([ShaderPackFile.FileName], CacheEntries());
        Assert.Equal(new ShaderProgress(ShaderStep.Done, 1), reports[^1]);
        Assert.Contains(reports, report => report.Step == ShaderStep.Converting && report.Fraction is > 0 and < 1);
        Assert.Equal(reports.Select(report => report.Fraction).Order(), reports.Select(report => report.Fraction));
    }

    [Fact]
    public void The_modules_shaders_are_written_to_the_top_up_folder_and_converted_with_the_rest()
    {
        var module = TestArc.Container(vertex: false, 3, seed: 8);
        _modules.Containers.Add(module);

        Run(withModules: true);

        var name = ShaderContainer.Of(module).Name + ".bin";
        Assert.Equal(Cache, _modules.AskedFolder);
        // Folded into the pack: the top-up folder no longer holds it.
        Assert.False(File.Exists(Path.Combine(TopUp, name)));
        Assert.Equal(new[] { name, Pixel + ".bin", Vertex + ".bin" }.Order(), _converter.Inputs.Order());
        Assert.False(Preparation().StatusOf(Cache).NeedsPreparing);
    }

    [Fact]
    public void A_shader_both_the_archives_and_the_modules_hold_is_converted_once()
    {
        _modules.Containers.Add(PixelContainer);

        Run(withModules: true);

        Assert.Equal([Pixel + ".bin", Vertex + ".bin"], _converter.Inputs.Order());
    }

    [Fact]
    public void Modules_that_cannot_be_read_fail_the_step_in_the_players_terms()
    {
        _modules.Failure = "The game stopped before it had read the Modules.";

        var error = Assert.Throws<ShaderPreparationException>(() => Run(withModules: true));

        Assert.Contains("The game stopped before it had read the Modules", error.Message);
        Assert.Contains("try again", error.Message);
        Assert.DoesNotContain(ShaderPackFile.FileName, CacheEntries());
    }

    [Fact]
    public void Containers_the_game_left_in_the_top_up_folder_are_converted_too()
    {
        var extra = TestArc.Container(vertex: false, 2, seed: 7);
        Directory.CreateDirectory(TopUp);
        File.WriteAllBytes(Path.Combine(TopUp, ShaderContainer.Of(extra).Name + ".bin"), extra);
        File.WriteAllText(Path.Combine(TopUp, "readme.txt"), "not a container");

        Run();

        Assert.Equal(3, _converter.Inputs.Count);
        Assert.Contains(ShaderContainer.Of(extra).Name + ".bin", _converter.Inputs);
        Assert.False(Preparation().StatusOf(Cache).NeedsPreparing);
    }

    [Fact]
    public void A_converter_failure_says_the_game_runs_on_classic_and_writes_no_pack()
    {
        _converter.Failure = "XenosRecomp could not convert 2 shaders.";

        var error = Assert.Throws<ShaderPreparationException>(() => Run());

        Assert.Contains("XenosRecomp could not convert 2 shaders.", error.Message);
        Assert.Contains("try again", error.Message);
        Assert.Empty(CacheEntries());
    }

    [Fact]
    public void Shaders_the_converter_cannot_convert_are_left_out_and_the_pack_holds_the_rest()
    {
        var bad = TestArc.Container(vertex: true, 3, seed: 40);
        var good = TestArc.Container(vertex: false, 3, seed: 41);
        TestArc.Lzx().Add(PixelContainer).Add(VertexContainer).Add(bad).Add(good).WriteTo(Path.Combine(GameData, "Arena.arc"));
        _converter.Rejects.Add(ShaderContainer.Of(bad).Name + ".bin");

        var result = Preparation().Run(GameData, Cache, null, TestContext.Current.CancellationToken);

        Assert.Equal([ShaderContainer.Of(bad).Name], result.Skipped);
        Assert.Equal(3, result.Converted);
        Assert.True(ShaderPackFile.Check(PackPath, ConverterId).Ready);
        Assert.DoesNotContain(ShaderContainer.Of(bad).Name + ".bin", _converter.LastConverted);
        Assert.Equal(3, _converter.LastConverted.Count);
        // The pack's fetch slots list only what was converted: no leftovers of the failed attempt.
        Assert.Equal(3, ShaderPackFile.ConvertedShaders(PackPath).Count);
        Assert.Equal([ShaderPackFile.FileName], CacheEntries());
    }

    [Fact]
    public void A_converter_that_cannot_start_is_not_screened_shader_by_shader()
    {
        _converter.Unusable = true;

        var error = Assert.Throws<ShaderPreparationException>(() => Run());

        Assert.Contains("converter is missing", error.Message);
        Assert.Equal(1, _converter.Calls);
    }

    [Fact]
    public void A_converter_that_fails_on_most_shaders_fails_the_step()
    {
        _converter.RejectAll = true;

        var error = Assert.Throws<ShaderPreparationException>(() => Run());

        Assert.Contains("XenosRecomp", error.Message);
        Assert.Contains("try again", error.Message);
        Assert.DoesNotContain(ShaderPackFile.FileName, CacheEntries());
    }

    [Fact]
    public void Cancelling_while_the_shaders_are_screened_stops_the_step()
    {
        _converter.Rejects.Add(Pixel + ".bin");
        using var cancel = new CancellationTokenSource();
        _converter.OnScreen = cancel.Cancel;

        Assert.ThrowsAny<OperationCanceledException>(() => Run(cancel: cancel.Token));

        Assert.DoesNotContain(ShaderPackFile.FileName, CacheEntries());
    }

    [Fact]
    public void A_damaged_archive_fails_the_step_and_names_the_file()
    {
        File.WriteAllBytes(Path.Combine(GameData, "Arena.arc"), new byte[10]);

        var error = Assert.Throws<ShaderPreparationException>(() => Run());

        Assert.Contains("Arena.arc", error.Message);
        Assert.Contains("try again", error.Message);
    }

    [Fact]
    public void Game_data_without_shaders_fails_the_step()
    {
        File.Delete(Path.Combine(GameData, "Arena.arc"));

        var error = Assert.Throws<ShaderPreparationException>(() => Run());

        Assert.Contains("try again", error.Message);
        Assert.Empty(CacheEntries());
    }

    [Fact]
    public void Cancel_keeps_the_previous_pack_and_cleans_up()
    {
        Directory.CreateDirectory(Cache);
        File.WriteAllBytes(PackPath, [1, 2, 3]);
        using var cancel = new CancellationTokenSource();
        _converter.OnConvert = cancel.Cancel;

        Assert.ThrowsAny<OperationCanceledException>(() => Run(cancel: cancel.Token));

        Assert.Equal([1, 2, 3], File.ReadAllBytes(PackPath));
        Assert.Equal([ShaderPackFile.FileName], CacheEntries());
    }

    [Fact]
    public void A_missing_pack_needs_preparing()
    {
        var status = Preparation().StatusOf(Cache);

        Assert.True(status.NeedsPreparing);
        Assert.Equal(ShaderPackProblem.Missing, status.Pack.Problem);
    }

    [Fact]
    public void A_pack_from_another_converter_is_stale_and_needs_preparing()
    {
        Run();
        var later = new ShaderPreparation(_converter, ConverterId + 1);

        var status = later.StatusOf(Cache);

        Assert.True(status.NeedsPreparing);
        Assert.Equal(ShaderPackProblem.Stale, status.Pack.Problem);
        Assert.Contains("prepared again", status.Message);
    }

    [Fact]
    public void A_fresh_pack_needs_nothing()
    {
        Run();

        Assert.False(Preparation().StatusOf(Cache).NeedsPreparing);
    }

    [Fact]
    public void A_container_the_game_found_after_the_pack_was_built_is_waiting_and_added_after_the_session()
    {
        Run();
        Directory.CreateDirectory(TopUp);
        var late = TestArc.Container(vertex: false, 2, seed: 7);
        File.WriteAllBytes(Path.Combine(TopUp, ShaderContainer.Of(late).Name + ".bin"), late);

        var status = Preparation().StatusOf(Cache);

        Assert.True(status.Pack.Ready);
        Assert.True(status.NeedsPreparing);
        Assert.Equal(1, status.WaitingShaders);
        Assert.True(status.AddsAfterSession);
    }

    [Fact]
    public void Folding_after_a_session_adds_the_top_ups_to_the_pack_and_empties_the_folder()
    {
        Run();
        Directory.CreateDirectory(TopUp);
        var late = TestArc.Container(vertex: false, 2, seed: 7);
        var lateName = ShaderContainer.Of(late).Name;
        var live = TestArc.Container(vertex: true, 3, seed: 9);
        var liveName = ShaderContainer.Of(live).Name;
        File.WriteAllBytes(Path.Combine(TopUp, lateName + ".bin"), late);
        File.WriteAllBytes(Path.Combine(TopUp, liveName + ".bin"), live);
        File.WriteAllBytes(Path.Combine(TopUp, liveName + ShaderPreparation.ConvertedExtension), OneShaderPack(ConverterId));

        Run();

        var packed = ShaderPackFile.ConvertedShaders(PackPath);
        Assert.Contains(ContainerFnv(lateName), packed);
        Assert.Contains(ContainerFnv(liveName), packed);
        Assert.Empty(Directory.EnumerateFiles(TopUp));
        Assert.False(Preparation().StatusOf(Cache).NeedsPreparing);
    }

    [Fact]
    public void A_failed_fold_keeps_the_top_ups_and_the_old_pack()
    {
        Run();
        var before = File.ReadAllBytes(PackPath);
        Directory.CreateDirectory(TopUp);
        var late = TestArc.Container(vertex: false, 2, seed: 7);
        File.WriteAllBytes(Path.Combine(TopUp, ShaderContainer.Of(late).Name + ".bin"), late);
        _converter.Failure = "XenosRecomp is missing.";

        Assert.Throws<ShaderPreparationException>(() => Run());

        Assert.True(File.Exists(Path.Combine(TopUp, ShaderContainer.Of(late).Name + ".bin")));
        Assert.Equal(before, File.ReadAllBytes(PackPath));
        Assert.True(Preparation().StatusOf(Cache).AddsAfterSession);
    }

    private static ulong ContainerFnv(string name) =>
        ulong.Parse(name[3..], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A one-shader pack as the game writes beside a top-up it converted live.</summary>
    private static byte[] OneShaderPack(ulong converterId)
    {
        var pack = new byte[32 + 24];
        "WWSP"u8.CopyTo(pack);
        BinaryPrimitives.WriteUInt32LittleEndian(pack.AsSpan(4), ShaderPackFile.FormatVersion);
        BinaryPrimitives.WriteUInt64LittleEndian(pack.AsSpan(8), converterId);
        BinaryPrimitives.WriteInt32LittleEndian(pack.AsSpan(16), 1);
        return pack;
    }

    [Fact]
    public void A_shader_the_game_converted_live_is_not_waiting()
    {
        Run();
        Directory.CreateDirectory(TopUp);
        var late = TestArc.Container(vertex: false, 2, seed: 7);
        var name = ShaderContainer.Of(late).Name;
        File.WriteAllBytes(Path.Combine(TopUp, name + ".bin"), late);
        File.WriteAllBytes(Path.Combine(TopUp, name + ShaderPreparation.ConvertedExtension), OneShaderPack(ConverterId));

        var status = Preparation().StatusOf(Cache);

        Assert.Equal(0, status.WaitingShaders);
        Assert.False(status.NeedsPreparing);
    }

    /// <summary>The Modules, as the game's Module mode leaves them: containers written under their dump names.</summary>
    private sealed class FakeModules : IModuleShaders
    {
        public List<byte[]> Containers { get; } = [];
        public string? Failure { get; set; }
        public string? AskedFolder { get; private set; }

        public int Write(string cacheDirectory, CancellationToken cancel)
        {
            AskedFolder = cacheDirectory;
            var topUpFolder = Path.Combine(cacheDirectory, ShaderPreparation.TopUpFolderName);
            if (Failure is not null) throw new ShaderPreparationException(Failure);
            foreach (var container in Containers)
                File.WriteAllBytes(Path.Combine(topUpFolder, ShaderContainer.Of(container).Name + ".bin"), container);
            return Containers.Count;
        }
    }

    /// <summary>
    /// Writes XenosRecomp's output shape for whatever containers it is given: one entry per
    /// container in shader_cache.cpp and an HLSL file named after each.
    /// </summary>
    private sealed class FakeConverter : IShaderConverter
    {
        public string? Failure { get; set; }
        public Action? OnConvert { get; set; }
        /// <summary>The first conversion's inputs: the whole set; later conversions screen one shader each.</summary>
        public List<string> Inputs { get; } = [];
        /// <summary>The inputs of the last conversion that worked.</summary>
        public List<string> LastConverted { get; } = [];
        /// <summary>Container files XenosRecomp cannot convert: a conversion holding one fails, as it does for a whole folder.</summary>
        public HashSet<string> Rejects { get; } = [];
        public bool RejectAll { get; set; }
        public bool Unusable { get; set; }
        /// <summary>Called on each conversion after the first: the screening.</summary>
        public Action? OnScreen { get; set; }
        public int Calls { get; private set; }

        public void Convert(string inputDirectory, string outputDirectory, IProgress<double>? progress, CancellationToken cancel)
        {
            int call;
            lock (this) call = ++Calls;
            var files = Directory.EnumerateFiles(inputDirectory).Select(file => Path.GetFileName(file)!).Order().ToList();
            if (Unusable)
                throw new ShaderConverterException("the shader converter is missing (XenosRecomp.exe). Unzip the Launcher's release again", unusable: true);
            if (call == 1) Inputs.AddRange(files);
            else OnScreen?.Invoke();
            OnConvert?.Invoke();
            cancel.ThrowIfCancellationRequested();
            progress?.Report(0.5);
            if (Failure is not null) throw new ShaderConverterException(Failure);
            // XenosRecomp dumps each shader's HLSL before it compiles any, so a failed run leaves them behind.
            var cache = new StringBuilder("ShaderCacheEntry g_shaderCacheEntries[] = {\n");
            Directory.CreateDirectory(Path.Combine(outputDirectory, "hlsl"));
            for (var i = 0; i < files.Count; i++)
            {
                cache.Append($"\t{{ 0x{i + 1:X}, 0, 0, {i}, 1, 0 }},\n");
                File.WriteAllText(Path.Combine(outputDirectory, "hlsl", Path.ChangeExtension(files[i], ".hlsl")), "float4 main();\n");
            }
            if (RejectAll || files.Any(Rejects.Contains)) throw new ShaderConverterException("XenosRecomp could not convert 1 shaders");
            lock (this)
            {
                LastConverted.Clear();
                LastConverted.AddRange(files);
            }
            cache.Append("};\n")
                .Append("const uint8_t g_compressedSpirvCache[] = {1,2,3,};\n")
                .Append("const size_t g_spirvCacheCompressedSize = 3;\n")
                .Append($"const size_t g_spirvCacheDecompressedSize = {files.Count};\n");
            File.WriteAllText(Path.Combine(outputDirectory, "shader_cache.cpp"), cache.ToString());
            progress?.Report(1);
        }
    }
}

using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Tests;

public class PackageTests
{
    [Theory]
    [InlineData("/word/document.xml", true)]
    [InlineData("/a", true)]
    [InlineData("word/document.xml", false)]
    [InlineData("/word/", false)]
    [InlineData("/word//document.xml", false)]
    [InlineData("/word/../document.xml", false)]
    [InlineData("/./a", false)]
    [InlineData("/a\\b", false)]
    [InlineData("/a\u0001b", false)]
    [InlineData("/", false)]
    [InlineData("", false)]
    public void Part_names_are_validated(string name, bool valid)
    {
        Assert.Equal(valid, PartUri.IsValid(name));
        Assert.Equal(valid, PartUri.TryCreate(name, out _));
    }

    [Fact]
    public void Part_names_compare_ignoring_case_and_keep_their_spelling()
    {
        var a = new PartUri("/Word/Document.xml");
        var b = new PartUri("/word/document.xml");
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Equal("/Word/Document.xml", a.Value);
        Assert.Equal("/Word/", a.Directory);
        Assert.Equal(".xml", a.Extension);
    }

    [Theory]
    [InlineData("/word/document.xml", "styles.xml", "/word/styles.xml")]
    [InlineData("/word/document.xml", "../media/a.png", "/media/a.png")]
    [InlineData("/word/document.xml", "./x/../y.xml", "/word/y.xml")]
    [InlineData("/word/document.xml", "/docProps/core.xml", "/docProps/core.xml")]
    [InlineData(null, "word/document.xml", "/word/document.xml")]
    public void Relative_targets_resolve_against_the_owner(string? source, string target, string expected)
    {
        Assert.True(PartUri.TryResolve(source is null ? null : new PartUri(source), target, out PartUri resolved));
        Assert.Equal(expected, resolved.Value);
    }

    [Theory]
    [InlineData("../../x.xml")]
    [InlineData("")]
    [InlineData("a/")]
    public void Targets_that_leave_the_package_or_are_not_names_do_not_resolve(string target) =>
        Assert.False(PartUri.TryResolve(new PartUri("/word/document.xml"), target, out _));

    [Fact]
    public void Loaded_parts_start_untouched_and_load_lazily()
    {
        using var pkg = new InMemoryPackage();
        int loads = 0;
        IPackagePart part = pkg.AddLoadedPart("/word/document.xml", "application/xml", 3, () => { loads++; return new byte[] { 1, 2, 3 }; });
        Assert.True(part.IsUntouched);
        Assert.False(pkg.IsModified);
        Assert.Equal(0, loads);

        Assert.Equal(new byte[] { 1, 2, 3 }, part.GetBytes().ToArray());
        part.GetBytes();
        Assert.Equal(1, loads);
        Assert.True(part.IsUntouched);   // reading never touches
        Assert.False(pkg.IsModified);
    }

    [Fact]
    public void Writing_touches_a_part_and_the_package()
    {
        using var pkg = new InMemoryPackage();
        IPackagePart a = pkg.AddLoadedPart("/a.xml", "application/xml", 1, () => new byte[] { 1 });
        IPackagePart b = pkg.AddLoadedPart("/b.xml", "application/xml", 1, () => new byte[] { 2 });
        a.SetBytes(new byte[] { 9, 9 });
        Assert.False(a.IsUntouched);
        Assert.True(b.IsUntouched);
        Assert.True(pkg.IsModified);
        Assert.Equal(2, a.Length);

        using (Stream w = b.OpenWrite())
        {
            w.WriteByte(7);
        }

        Assert.False(b.IsUntouched);
        Assert.Equal(new byte[] { 7 }, b.GetBytes().ToArray());
    }

    [Fact]
    public void Added_parts_are_touched_and_removal_modifies()
    {
        using var pkg = new InMemoryPackage();
        pkg.AddLoadedPart("/a.xml", "application/xml", 0, () => ReadOnlyMemory<byte>.Empty);
        Assert.False(pkg.IsModified);
        Assert.False(pkg.AddPart("/b.xml", "application/xml", new byte[] { 1 }).IsUntouched);
        Assert.Throws<ArgumentException>(() => pkg.AddPart("/B.xml", "application/xml", ReadOnlyMemory<byte>.Empty));
        Assert.Throws<ArgumentException>(() => pkg.AddPart("b.xml", "application/xml", ReadOnlyMemory<byte>.Empty));
        Assert.NotNull(pkg.FindPart("/A.XML"));
        Assert.True(pkg.RemovePart("/a.xml"));
        Assert.False(pkg.RemovePart("/a.xml"));
        Assert.Single(pkg.Parts);
    }

    [Fact]
    public void Relationships_round_trip_and_mark_the_package_modified()
    {
        using var pkg = new InMemoryPackage();
        Assert.Empty(pkg.GetRelationships());
        pkg.SetRelationships(null, [new Relationship("rId1", "t", "word/document.xml")]);
        Assert.Equal("rId1", pkg.GetRelationships()[0].Id);
        Assert.Equal(RelationshipTargetMode.Internal, pkg.GetRelationships()[0].TargetMode);
        Assert.True(pkg.IsModified);
    }

    [Fact]
    public void Part_size_and_count_limits_are_typed()
    {
        using var pkg = new InMemoryPackage(new OfficeLimits { MaxPartSize = 10, MaxPartCount = 2 });
        IPackagePart big = pkg.AddLoadedPart("/big.bin", "x", 1000, () => throw new InvalidOperationException("must not load"));
        var ex = Assert.Throws<OfficeLimitException>(() => big.GetBytes());
        Assert.Equal(nameof(OfficeLimits.MaxPartSize), ex.LimitName);
        Assert.Equal("/big.bin", ex.Location.Container);
        pkg.AddPart("/b", "x", ReadOnlyMemory<byte>.Empty);
        Assert.Throws<OfficeLimitException>(() => pkg.AddPart("/c", "x", ReadOnlyMemory<byte>.Empty));
    }

    [Fact]
    public void Save_replaces_the_file_atomically()
    {
        string dir = Path.Combine(Path.GetTempPath(), "onedocument-save-" + Environment.ProcessId);
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "doc.pkg");
            File.WriteAllText(path, "original");
            using var pkg = new InMemoryPackage(filePath: path);
            pkg.AddPart("/a.xml", "application/xml", new byte[] { 1, 2, 3 });
            pkg.Save();
            Assert.StartsWith("ONEDOCUMENT-MEM1", File.ReadAllText(path), StringComparison.Ordinal);
            Assert.Single(Directory.GetFiles(dir)); // no temp file left

            using var noPath = new InMemoryPackage();
            Assert.Throws<InvalidOperationException>(() => noPath.Save());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void An_interrupted_save_leaves_the_original_intact_and_no_temp_file()
    {
        string dir = Path.Combine(Path.GetTempPath(), "onedocument-atomic-" + Environment.ProcessId);
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "doc.bin");
            File.WriteAllText(path, "original");
            Assert.Throws<InvalidOperationException>(() => AtomicFile.Write(path, s =>
            {
                s.Write("partial"u8);
                throw new InvalidOperationException("disk full");
            }));
            Assert.Equal("original", File.ReadAllText(path));
            Assert.Equal([path], Directory.GetFiles(dir));

            using var cts = new CancellationTokenSource();
            using var pkg = new InMemoryPackage(filePath: path);
            pkg.AddPart("/a.xml", "application/xml", new byte[] { 1 });
            cts.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => pkg.Save(cts.Token));
            Assert.Equal("original", File.ReadAllText(path));
            Assert.Equal([path], Directory.GetFiles(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Async_atomic_write_commits_and_rolls_back()
    {
        string dir = Path.Combine(Path.GetTempPath(), "onedocument-atomic2-" + Environment.ProcessId);
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "doc.bin");
            await AtomicFile.WriteAsync(path, async (s, ct) => await s.WriteAsync("new"u8.ToArray(), ct));
            Assert.Equal("new", File.ReadAllText(path));
            await Assert.ThrowsAsync<InvalidOperationException>(() => AtomicFile.WriteAsync(path, (_, _) => throw new InvalidOperationException()));
            Assert.Equal("new", File.ReadAllText(path));
            Assert.Equal([path], Directory.GetFiles(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

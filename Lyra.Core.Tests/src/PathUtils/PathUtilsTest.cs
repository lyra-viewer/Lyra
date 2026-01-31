using Xunit;
using static Lyra.PathUtils.PathUtils;

namespace Lyra.Core.Tests.PathUtils;

public sealed class PathUtilsTest : IDisposable
{
    private readonly string _root;

    public PathUtilsTest()
    {
        _root = Path.Combine(Path.GetTempPath(), "Lyra_PathUtilsTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            /* ignore cleanup errors */
        }
    }

    [Fact]
    public void ValidateAndNormalizePaths_Null_ReturnsEmpty()
    {
        var result = ValidateAndNormalizePaths(null);
        Assert.Empty(result);
    }

    [Fact]
    public void ValidateAndNormalizePaths_IgnoresWhitespaceAndMissing()
    {
        var dir = MakeDir("vn");
        var file = MakeFile(Path.Combine(dir, "a.txt"));
        var missing = Path.Combine(_root, "does_not_exist_" + Guid.NewGuid().ToString("N"));

        var result = ValidateAndNormalizePaths([" ", "\t", missing, file]);

        Assert.Single(result);
        Assert.Equal(Path.GetFullPath(file), result[0]);
    }

    [Fact]
    public void ValidateAndNormalizePaths_TrimsDirectoryTrailingSeparator()
    {
        var dir = MakeDir("trim");
        var withSep = dir + Path.DirectorySeparatorChar;

        var result = ValidateAndNormalizePaths([withSep]);

        Assert.Single(result);
        Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir)), result[0]);
    }

    [Fact]
    public void ValidateAndNormalizePaths_Deduplicates_UsingCommonPathComparer()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var dir = MakeDir("dedup");
        var file = MakeFile(Path.Combine(dir, "a.txt"));

        var p1 = Path.GetFullPath(file);
        var p2 = Path.Combine(Path.GetDirectoryName(p1)!, "A.TXT");

        var result = ValidateAndNormalizePaths([p1, p2]);

        Assert.Single(result);
        Assert.Equal(p1, result[0]);
    }

    [Fact]
    public void HaveSameParentDirectory_FilesInSameDirectory_IsTrue()
    {
        var dir = MakeDir("same");
        var a = MakeFile(Path.Combine(dir, "a.png"));
        var b = MakeFile(Path.Combine(dir, "b.png"));

        Assert.True(HaveSameParentDirectory([a, b]));
    }

    [Fact]
    public void HaveSameParentDirectory_DifferentDirectories_IsFalse()
    {
        var dir1 = MakeDir("d1");
        var dir2 = MakeDir("d2");
        var a = MakeFile(Path.Combine(dir1, "a.png"));
        var b = MakeFile(Path.Combine(dir2, "b.png"));

        Assert.False(HaveSameParentDirectory([a, b]));
    }

    [Fact]
    public void HaveSameParentDirectory_DirectoryAndFileInside_IsTrue()
    {
        var dir = MakeDir("mix");
        var a = MakeFile(Path.Combine(dir, "a.png"));

        Assert.True(HaveSameParentDirectory([dir, a]));
    }

    [Fact]
    public void HaveSameParentDirectory_NonExisting_Throws()
    {
        var p = Path.Combine(_root, "missing_" + Guid.NewGuid().ToString("N"));
        Assert.Throws<ArgumentException>(() => HaveSameParentDirectory([p]));
    }

    [Fact]
    public void DirectoryHasSubdirectories_EmptyOrWhitespace_Throws()
    {
        Assert.Throws<ArgumentException>(() => DirectoryHasSubdirectories(" "));
    }

    [Fact]
    public void DirectoryHasSubdirectories_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => DirectoryHasSubdirectories(null!));
    }

    [Fact]
    public void DirectoryHasSubdirectories_NoSubdirectories_IsFalse()
    {
        var dir = MakeDir("nosub");
        Assert.False(DirectoryHasSubdirectories(dir));
    }

    [Fact]
    public void DirectoryHasSubdirectories_WithSubdirectory_IsTrue()
    {
        var dir = MakeDir("withsub");
        Directory.CreateDirectory(Path.Combine(dir, "child"));
        Assert.True(DirectoryHasSubdirectories(dir));
    }

    [Fact]
    public void GetTopDirectory_Empty_ReturnsNull()
    {
        Assert.Null(GetTopDirectory([]));
    }

    [Fact]
    public void GetTopDirectory_WhitespaceOnly_ReturnsNull()
    {
        var result = GetTopDirectory([" ", "\t", "\n"]);
        Assert.Null(result);
    }

    [Fact]
    public void GetTopDirectory_SinglePath_ReturnsNormalizedItem()
    {
        var dir = MakeDir("a");
        var result = GetTopDirectory([dir]);

        // Current behavior: returns NormalizeDirectory(path) (not necessarily "directory of file")
        Assert.Equal(NormalizeDirectory(dir), result);
    }

    [Fact]
    public void GetTopDirectory_TwoItemsSameDirectory_ReturnsThatDirectory()
    {
        var dir = MakeDir("images");
        var file1 = MakeFile(Path.Combine(dir, "a.png"));
        var file2 = MakeFile(Path.Combine(dir, "b.jpg"));

        var result = GetTopDirectory([file1, file2]);

        Assert.NotNull(result);
        Assert.Equal(NormalizeDirectory(dir), result);
    }

    [Fact]
    public void GetTopDirectory_TwoSiblingsUnderTemp_ReturnsTempDirectory()
    {
        var rootA = Path.Combine(Path.GetTempPath(), "Lyra_PathUtilsTests_A_" + Guid.NewGuid().ToString("N"));
        var rootB = Path.Combine(Path.GetTempPath(), "Lyra_PathUtilsTests_B_" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(rootA);
        Directory.CreateDirectory(rootB);

        try
        {
            var f1 = Path.Combine(rootA, "a.png");
            var f2 = Path.Combine(rootB, "b.png");
            File.WriteAllText(f1, "x");
            File.WriteAllText(f2, "x");

            var result = GetTopDirectory([f1, f2]);

            Assert.NotNull(result);

            // They should share at least the temp directory.
            var expected = NormalizeDirectory(Path.GetTempPath());
            Assert.Equal(expected, result);
        }
        finally
        {
            try { Directory.Delete(rootA, recursive: true); } catch { /* ignored */ }
            try { Directory.Delete(rootB, recursive: true); } catch { /* ignored */ }
        }
    }

    [Fact]
    public void GetTopDirectory_DifferentDriveRoots_Windows_ReturnsNull()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var driveC = @"C:\";
        var driveD = @"D:\";

        if (!Directory.Exists(driveD))
            return;

        var p1 = Path.Combine(driveC, "Temp", "a.png");
        var p2 = Path.Combine(driveD, "Temp", "b.png");

        var result = GetTopDirectory([p1, p2]);
        Assert.Null(result);
    }

    [Fact]
    public void GetTopDirectory_CommonPrefixShrinksAcrossAllPaths()
    {
        // Build:
        // root/a/b/c1/x.png
        // root/a/b/c2/y.png
        // root/a/x/z.png
        var a = MakeDir("a");
        var b = MakeDir(Path.Combine("a", "b"));
        var c1 = MakeDir(Path.Combine("a", "b", "c1"));
        var c2 = MakeDir(Path.Combine("a", "b", "c2"));
        var ax = MakeDir(Path.Combine("a", "x"));

        var p1 = MakeFile(Path.Combine(c1, "x.png"));
        var p2 = MakeFile(Path.Combine(c2, "y.png"));
        var p3 = MakeFile(Path.Combine(ax, "z.png"));

        var result = GetTopDirectory([p1, p2, p3]);

        Assert.NotNull(result);
        Assert.Equal(NormalizeDirectory(a), result);
    }

    [Fact]
    public void GetTopDirectory_WhenOnlyRootIsCommon_Unix_ReturnsSlash()
    {
        if (OperatingSystem.IsWindows())
            return;

        var result = GetTopDirectory(["/usr/bin/a", "/etc/hosts"]);
        Assert.Equal("/", result);
    }

    [Fact]
    public void GetTopDirectory_WhenOnlyDriveRootIsCommon_Windows_ReturnsDriveRoot()
    {
        if (!OperatingSystem.IsWindows())
            return;

        // Use same drive as temp to keep it valid.
        var drive = Path.GetPathRoot(_root)!; // e.g. "C:\"
        var p1 = Path.Combine(drive, "Users", "me", "a.png");
        var p2 = Path.Combine(drive, "Windows", "System32", "b.dll");

        var result = GetTopDirectory([p1, p2]);

        Assert.NotNull(result);
        Assert.Equal(drive, result);
    }

    [Fact]
    public void GetCanonicalPath_FollowSymlinksFalse_ReturnsFullName()
    {
        var dir = MakeDir("real");
        var result = GetCanonicalPath(dir, followSymlinks: false);

        Assert.Equal(new DirectoryInfo(dir).FullName, result);
    }

    [Fact]
    public void GetCanonicalPath_InvalidPath_FallsBackToGetFullPath()
    {
        // relative path is still convertible by GetFullPath
        var rel = Path.Combine("some", "nonexistent", "..", "x");
        var result = GetCanonicalPath(rel, followSymlinks: true);

        Assert.Equal(Path.GetFullPath(rel), result);
    }

    [Fact]
    public void GetCanonicalPath_WhenSymlinkAndFollowTrue_ReturnsTarget()
    {
        // Creating directory symlinks on Windows can require admin or Developer Mode.
        // On macOS/Linux it should work.
        if (OperatingSystem.IsWindows())
            return;

        var target = MakeDir("target");
        var link = Path.Combine(_root, "link");

        // .NET 8/9 API
        Directory.CreateSymbolicLink(link, target);

        var result = GetCanonicalPath(link, followSymlinks: true);

        // ResolveLinkTarget(true) returns the target DirectoryInfo.
        Assert.Equal(new DirectoryInfo(target).FullName, result);
    }

    [Fact]
    public void GetCanonicalPath_WhenSymlinkAndFollowFalse_ReturnsLinkPath()
    {
        if (OperatingSystem.IsWindows())
            return;

        var target = MakeDir("target2");
        var link = Path.Combine(_root, "link2");
        Directory.CreateSymbolicLink(link, target);

        var result = GetCanonicalPath(link, followSymlinks: false);

        Assert.Equal(new DirectoryInfo(link).FullName, result);
    }

    [Fact]
    public void GetCanonicalPath_Unix_FollowSymlink_ResolvesTarget()
    {
        if (OperatingSystem.IsWindows())
            return;

        var root = Path.Combine(_root, "canon");
        Directory.CreateDirectory(root);

        var targetDir = Path.Combine(root, "targetDir");
        Directory.CreateDirectory(targetDir);

        var linkDir = Path.Combine(root, "linkDir");
        Directory.CreateSymbolicLink(linkDir, targetDir);

        var canonical = GetCanonicalPath(linkDir, followSymlinks: true);

        Assert.Equal(new DirectoryInfo(targetDir).FullName, canonical);
    }

    [Fact]
    public void IsHidden_Unix_DotFile_IsTrue()
    {
        if (OperatingSystem.IsWindows())
            return;

        var p = Path.Combine(_root, ".secret");
        File.WriteAllText(p, "x");

        Assert.True(IsHidden(p));
    }

    [Fact]
    public void IsHidden_Unix_NormalFile_IsFalse()
    {
        if (OperatingSystem.IsWindows())
            return;

        var p = Path.Combine(_root, "visible.txt");
        File.WriteAllText(p, "x");

        Assert.False(IsHidden(p));
    }

    [Fact]
    public void IsHidden_Windows_HiddenAttribute_IsTrue()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var p = Path.Combine(_root, "hidden.txt");
        File.WriteAllText(p, "x");

        File.SetAttributes(p, File.GetAttributes(p) | FileAttributes.Hidden);

        Assert.True(IsHidden(p));
    }

    [Fact]
    public void IsHidden_PathWithTrailingSeparator_UsesDirectoryName()
    {
        // Works on all OSes; on Unix, ".git/" should be hidden via dotfile rule
        var dir = Path.Combine(_root, ".git");
        Directory.CreateDirectory(dir);

        var withSep = dir + Path.DirectorySeparatorChar;
        Assert.True(IsHidden(withSep));
    }

    [Fact]
    public void IsSymlinkOrReparsePoint_NonExisting_ReturnsFalse()
    {
        var p = Path.Combine(_root, "does_not_exist_" + Guid.NewGuid().ToString("N"));
        Assert.False(IsSymlinkOrReparsePoint(p));
    }

    [Fact]
    public void IsSymlinkOrReparsePoint_Unix_SymlinkToDir_IsTrue()
    {
        if (OperatingSystem.IsWindows())
            return;

        var target = Path.Combine(_root, "targetDir");
        Directory.CreateDirectory(target);

        var link = Path.Combine(_root, "linkDir");
        Directory.CreateSymbolicLink(link, target);

        Assert.True(IsSymlinkOrReparsePoint(link));
        Assert.False(IsSymlinkOrReparsePoint(target));
    }

    [Fact]
    public void IsSymlinkOrReparsePoint_Unix_SymlinkToFile_IsTrue()
    {
        if (OperatingSystem.IsWindows())
            return;

        var target = Path.Combine(_root, "targetFile.txt");
        File.WriteAllText(target, "x");

        var link = Path.Combine(_root, "linkFile.txt");
        File.CreateSymbolicLink(link, target);

        Assert.True(IsSymlinkOrReparsePoint(link));
        Assert.False(IsSymlinkOrReparsePoint(target));
    }

    [Fact]
    public void IsSymlinkOrReparsePoint_Unix_FileSymlink_IsTrue()
    {
        if (OperatingSystem.IsWindows())
            return;

        var root = Path.Combine(Path.GetTempPath(), "Lyra_SymlinkTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var target = Path.Combine(root, "target.txt");
            File.WriteAllText(target, "x");

            var link = Path.Combine(root, "link.txt");
            File.CreateSymbolicLink(link, target);

            Assert.True(IsSymlinkOrReparsePoint(link));
            Assert.False(IsSymlinkOrReparsePoint(target));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignored */ }
        }
    }

    [Fact]
    public void IsSymlinkOrReparsePoint_Windows_ReparsePoint_IsTrue_WhenSymlinkAvailable()
    {
        if (!OperatingSystem.IsWindows())
            return;

        // Creating symlinks may require admin or Developer Mode.
        // If it fails, we skip (treat as environment constraint).
        var target = Path.Combine(_root, "winTargetDir");
        Directory.CreateDirectory(target);

        var link = Path.Combine(_root, "winLinkDir");

        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch
        {
            return; // skip
        }

        Assert.True(IsSymlinkOrReparsePoint(link));
        Assert.False(IsSymlinkOrReparsePoint(target));
    }

    // Helpers
    private string MakeDir(string relative)
    {
        var full = Path.Combine(_root, relative);
        Directory.CreateDirectory(full);
        return full;
    }

    private string MakeFile(string fullPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
        File.WriteAllText(fullPath, "x");
        return fullPath;
    }
}
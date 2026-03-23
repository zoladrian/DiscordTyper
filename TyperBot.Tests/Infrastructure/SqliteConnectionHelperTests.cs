using FluentAssertions;
using TyperBot.Infrastructure.Data;

namespace TyperBot.Tests.Infrastructure;

public class SqliteConnectionHelperTests
{
    [Fact]
    public void WithAbsoluteDataSource_NullOrEmptyConnectionString_ReturnsOriginal()
    {
        SqliteConnectionHelper.WithAbsoluteDataSource(null!, @"C:\app").Should().BeNull();
        SqliteConnectionHelper.WithAbsoluteDataSource("", @"C:\app").Should().Be("");
        SqliteConnectionHelper.WithAbsoluteDataSource("   ", @"C:\app").Should().Be("   ");
    }

    [Fact]
    public void WithAbsoluteDataSource_NullOrEmptyBaseDirectory_ReturnsOriginal()
    {
        const string cs = "Data Source=rel.db";
        SqliteConnectionHelper.WithAbsoluteDataSource(cs, null!).Should().Be(cs);
        SqliteConnectionHelper.WithAbsoluteDataSource(cs, "").Should().Be(cs);
        SqliteConnectionHelper.WithAbsoluteDataSource(cs, "   ").Should().Be(cs);
    }

    [Fact]
    public void WithAbsoluteDataSource_NoDataSourcePrefix_ReturnsOriginal()
    {
        const string cs = "Server=localhost";
        SqliteConnectionHelper.WithAbsoluteDataSource(cs, @"C:\app").Should().Be(cs);
    }

    [Fact]
    public void WithAbsoluteDataSource_RelativePath_AbsolutizesAgainstBaseDirectory()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "TyperSqliteTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDir);
        try
        {
            var result = SqliteConnectionHelper.WithAbsoluteDataSource("Data Source=typer.db", baseDir);
            var expectedPath = Path.GetFullPath(Path.Combine(baseDir, "typer.db"));
            result.Should().Be($"Data Source={expectedPath}");
        }
        finally
        {
            try
            {
                Directory.Delete(baseDir, recursive: true);
            }
            catch
            {
                // ignore cleanup failures on locked temp dirs
            }
        }
    }

    [Fact]
    public void WithAbsoluteDataSource_DataSourcePrefix_PreservesSuffixAfterSemicolon()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "TyperSqliteTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDir);
        try
        {
            var result = SqliteConnectionHelper.WithAbsoluteDataSource(
                "Data Source=app.db;Cache=Shared;Mode=ReadWrite",
                baseDir);
            var expectedPath = Path.GetFullPath(Path.Combine(baseDir, "app.db"));
            result.Should().Be($"Data Source={expectedPath};Cache=Shared;Mode=ReadWrite");
        }
        finally
        {
            try
            {
                Directory.Delete(baseDir, recursive: true);
            }
            catch
            {
                // ignore
            }
        }
    }

    [Fact]
    public void WithAbsoluteDataSource_DataSourceEqualsPrefix_Works()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "TyperSqliteTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDir);
        try
        {
            var result = SqliteConnectionHelper.WithAbsoluteDataSource("DataSource=x.db", baseDir);
            var expectedPath = Path.GetFullPath(Path.Combine(baseDir, "x.db"));
            result.Should().Be($"DataSource={expectedPath}");
        }
        finally
        {
            try
            {
                Directory.Delete(baseDir, recursive: true);
            }
            catch
            {
                // ignore
            }
        }
    }

    [Fact]
    public void WithAbsoluteDataSource_QuotedRelativeToken_StripsQuotesAndAbsolutizes()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "TyperSqliteTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDir);
        try
        {
            var result = SqliteConnectionHelper.WithAbsoluteDataSource(
                @"Data Source=""nested\a.db"";Foreign Keys=True",
                baseDir);
            var expectedPath = Path.GetFullPath(Path.Combine(baseDir, "nested", "a.db"));
            result.Should().Be($"Data Source={expectedPath};Foreign Keys=True");
        }
        finally
        {
            try
            {
                Directory.Delete(baseDir, recursive: true);
            }
            catch
            {
                // ignore
            }
        }
    }

    [Fact]
    public void WithAbsoluteDataSource_InMemory_Unchanged()
    {
        const string cs = "Data Source=:memory:";
        SqliteConnectionHelper.WithAbsoluteDataSource(cs, @"C:\any").Should().Be(cs);
    }

    [Fact]
    public void WithAbsoluteDataSource_FileUri_Unchanged()
    {
        const string cs = "Data Source=file:memdb1?mode=memory&cache=shared";
        SqliteConnectionHelper.WithAbsoluteDataSource(cs, @"C:\any").Should().Be(cs);
    }

    [Fact]
    public void WithAbsoluteDataSource_AlreadyRooted_Unchanged()
    {
        var rooted = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "already.db"));
        var cs = $"Data Source={rooted}";
        SqliteConnectionHelper.WithAbsoluteDataSource(cs, @"C:\other").Should().Be(cs);
    }

    [Fact]
    public void WithAbsoluteDataSource_TrimsOuterConnectionStringWhitespace()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "TyperSqliteTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDir);
        try
        {
            var result = SqliteConnectionHelper.WithAbsoluteDataSource("  Data Source=z.db  ", baseDir);
            var expectedPath = Path.GetFullPath(Path.Combine(baseDir, "z.db"));
            result.Should().Be($"Data Source={expectedPath}");
        }
        finally
        {
            try
            {
                Directory.Delete(baseDir, recursive: true);
            }
            catch
            {
                // ignore
            }
        }
    }
}

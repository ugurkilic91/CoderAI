using DevAgents.Domain.Files;

namespace DevAgents.Domain.UnitTests.Files;

public class RelativePathTests
{
    [TestCase("src/Foo.cs", "src/Foo.cs")]
    [TestCase("  `./src/Foo.cs`  ", "src/Foo.cs")]
    [TestCase("\"src\\Orders\\Foo.cs\"", "src/Orders/Foo.cs")]
    [TestCase("**/src/Foo.cs**", "src/Foo.cs")]
    [TestCase("././Foo.cs", "Foo.cs")]
    public void Normalize_CleansUpThePath(string raw, string expected)
    {
        Assert.That(RelativePath.Normalize(raw), Is.EqualTo(expected));
    }

    [TestCase("../evil.cs")]
    [TestCase("src/../../evil.cs")]
    [TestCase("C:/Windows/evil.cs")]
    [TestCase("https://example.com/a.cs")]
    [TestCase("   ")]
    [TestCase("``")]
    [TestCase(null)]
    public void Normalize_UnsafeOrEmptyPath_ReturnsNull(string? raw)
    {
        Assert.That(RelativePath.Normalize(raw), Is.Null);
    }
}

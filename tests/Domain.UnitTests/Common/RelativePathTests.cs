using DevAgents.Domain.Common;
using NUnit.Framework;
using Shouldly;

namespace DevAgents.Domain.UnitTests.Common;

public class RelativePathTests
{
    [TestCase("src/Foo.cs", "src/Foo.cs")]
    [TestCase("  `./src/Foo.cs`  ", "src/Foo.cs")]
    [TestCase("\"src\\Orders\\Foo.cs\"", "src/Orders/Foo.cs")]
    [TestCase("**/src/Foo.cs**", "src/Foo.cs")]
    [TestCase("././Foo.cs", "Foo.cs")]
    public void ShouldCleanUpThePath(string raw, string expected)
    {
        RelativePath.Normalize(raw).ShouldBe(expected);
    }

    [TestCase("../evil.cs")]
    [TestCase("src/../../evil.cs")]
    [TestCase("C:/Windows/evil.cs")]
    [TestCase("https://example.com/a.cs")]
    [TestCase("   ")]
    [TestCase("``")]
    [TestCase(null)]
    public void ShouldRejectUnsafeOrEmptyPaths(string? raw)
    {
        RelativePath.Normalize(raw).ShouldBeNull();
    }
}

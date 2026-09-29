using PcbExpo.App;

namespace PcbExpo.Tests;

public class ApplicationPathsTests
{
    [Fact]
    public void DefaultTemplateUsesWorkingDirectoryBeforeExecutableDirectory()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"pcbexpo-paths-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var template = Path.Combine(folder, "150x100.cxdlpv4");
        try
        {
            File.WriteAllText(template, "test");
            Assert.Equal(template, ApplicationPaths.ResolveDefaultTemplate(folder, "application"));
            Assert.Equal(Path.Combine("application", "150x100.cxdlpv4"),
                ApplicationPaths.ResolveDefaultTemplate(Path.Combine(folder, "missing"), "application"));
        }
        finally
        {
            File.Delete(template);
            Directory.Delete(folder);
        }
    }
}

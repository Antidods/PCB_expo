namespace PcbExpo.App;

internal static class ApplicationPaths
{
    public static string DefaultTemplate => ResolveDefaultTemplate(Environment.CurrentDirectory, AppContext.BaseDirectory);

    public static string ResolveDefaultTemplate(string workingDirectory, string applicationDirectory)
    {
        var workingTemplate = Path.Combine(workingDirectory, "150x100.cxdlpv4");
        return File.Exists(workingTemplate) ? workingTemplate : Path.Combine(applicationDirectory, "150x100.cxdlpv4");
    }
}

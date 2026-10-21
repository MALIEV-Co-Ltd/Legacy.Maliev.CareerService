using System.Xml.Linq;

namespace Legacy.Maliev.CareerService.Tests.Documentation;

/// <summary>Checks compiler-produced XML documentation and its isolation from project source roots.</summary>
public sealed class CareerGeneratedDocumentationIsolationTests
{
    /// <summary>Checks actual assembly documentation, a meaningful member summary, and source-root isolation.</summary>
    /// <param name="assemblyName">The Career assembly name used to locate its compiler-generated XML file in build output and check that no matching XML file exists in its project source root.</param>
    /// <param name="documentedMember">The XML documentation member identifier to locate in that assembly file and verify has a nonblank summary.</param>
    [Theory]
    [InlineData("Legacy.Maliev.CareerService.Api", "T:Program")]
    [InlineData("Legacy.Maliev.CareerService.Application", "T:Legacy.Maliev.CareerService.Application.Interfaces.ICareerService")]
    [InlineData("Legacy.Maliev.CareerService.Data", "T:Legacy.Maliev.CareerService.Data.CareerDbContext")]
    [InlineData("Legacy.Maliev.CareerService.Domain", "T:Legacy.Maliev.CareerService.Domain.JobOffer")]
    public void CompilerDocumentation_IsAvailableInOutputWithoutSourceRootArtifact(string assemblyName, string documentedMember)
    {
        var output = Path.Combine(AppContext.BaseDirectory, assemblyName + ".xml");
        Assert.True(File.Exists(output), "Build the actual production projects before documentation acceptance.");

        var document = XDocument.Load(output);
        Assert.Equal(assemblyName, document.Root?.Element("assembly")?.Element("name")?.Value);
        var member = Assert.Single(document.Descendants("member"), element =>
            string.Equals((string?)element.Attribute("name"), documentedMember, StringComparison.Ordinal));
        Assert.False(string.IsNullOrWhiteSpace(member.Element("summary")?.Value));

        var sourceArtifact = Path.Combine(FindRepositoryRoot(), assemblyName, assemblyName + ".xml");
        Assert.False(File.Exists(sourceArtifact), "Generated assembly documentation must remain outside the project source root.");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.CareerService.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Career repository root.");
    }
}

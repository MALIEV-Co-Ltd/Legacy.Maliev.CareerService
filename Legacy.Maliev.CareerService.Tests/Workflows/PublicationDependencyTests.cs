using YamlDotNet.RepresentationModel;

namespace Legacy.Maliev.CareerService.Tests.Workflows;

public sealed class PublicationDependencyTests
{
    [Fact]
    public void Publisher_ProvidesPinnedDockerDependencies_WithoutOpeningDeploymentGate()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Legacy.Maliev.CareerService.slnx")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        var yaml = new YamlStream();
        yaml.Load(new StringReader(File.ReadAllText(Path.Combine(root.FullName, ".github", "workflows", "publish-image.yml"))));
        var document = (YamlMappingNode)yaml.Documents[0].RootNode;
        var jobs = (YamlMappingNode)document.Children[new YamlScalarNode("jobs")];
        var publish = (YamlMappingNode)jobs.Children[new YamlScalarNode("publish")];
        Assert.Equal("vars.LEGACY_DEPLOY_ENABLED == 'true'", Value(publish, "if"));
        Assert.Equal("MALIEV-Co-Ltd/Legacy.Maliev.Workflows/.github/workflows/publish-image.yml@503e8846390a597c267d2889b33a9c26863389b3", Value(publish, "uses"));
        var permissions = (YamlMappingNode)publish.Children[new YamlScalarNode("permissions")];
        Assert.Equal(3, permissions.Children.Count);
        Assert.Equal("read", Value(permissions, "contents"));
        Assert.Equal("read", Value(permissions, "actions"));
        Assert.Equal("write", Value(permissions, "id-token"));
        var inputs = (YamlMappingNode)publish.Children[new YamlScalarNode("with")];
        Assert.Equal("8f4f5f27b226ffe406c4c79b1903742e8c2e7dd3", Value(inputs, "legacy-service-defaults-ref"));
        Assert.Equal("78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7", Value(inputs, "compatibility-contracts-ref"));
        Assert.Equal(".", Value(inputs, "context"));
        Assert.Equal("legacy-production", Value(inputs, "environment"));
    }

    private static string? Value(YamlMappingNode mapping, string key) =>
        mapping.Children.TryGetValue(new YamlScalarNode(key), out var value) ? ((YamlScalarNode)value).Value : null;
}

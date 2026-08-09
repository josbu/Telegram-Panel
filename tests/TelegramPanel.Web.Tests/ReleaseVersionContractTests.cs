using System.Xml.Linq;
using Xunit;

namespace TelegramPanel.Web.Tests;

public sealed class ReleaseVersionContractTests
{
    [Fact]
    public void 项目版本字段保持一致且发布包写入信息版本()
    {
        var root = TestRepositoryRoot.Find();
        var props = XDocument.Load(Path.Combine(root, "Directory.Build.props"));
        var values = new[]
        {
            ReadProperty(props, "Version"),
            ReadProperty(props, "InformationalVersion"),
            RemoveRevisionComponent(ReadProperty(props, "AssemblyVersion")),
            RemoveRevisionComponent(ReadProperty(props, "FileVersion"))
        };

        Assert.All(values, value => Assert.Equal(values[0], value));
        Assert.Equal(
            "false",
            ReadProperty(props, "IncludeSourceRevisionInInformationalVersion"));

        var webProject = File.ReadAllText(
            Path.Combine(root, "src", "TelegramPanel.Web", "TelegramPanel.Web.csproj"));
        Assert.Contains("Lines=\"$(InformationalVersion)\"", webProject, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("docker.yml")]
    [InlineData("release.yml")]
    public void 正式标签工作流校验项目版本(string workflowName)
    {
        var root = TestRepositoryRoot.Find();
        var workflow = File.ReadAllText(
            Path.Combine(root, ".github", "workflows", workflowName));

        Assert.Contains("Validate release tag version", workflow, StringComparison.Ordinal);
        Assert.Contains("Directory.Build.props", workflow, StringComparison.Ordinal);
        Assert.Contains("PROJECT_VERSION", workflow, StringComparison.Ordinal);
        Assert.Contains("TAG_VERSION", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Docker拉取请求覆盖前端与版本文件()
    {
        var root = TestRepositoryRoot.Find();
        var workflow = File.ReadAllText(
            Path.Combine(root, ".github", "workflows", "docker.yml"));

        Assert.Contains("- \"frontend/**\"", workflow, StringComparison.Ordinal);
        Assert.Contains("- \"Directory.Build.props\"", workflow, StringComparison.Ordinal);
    }

    private static string ReadProperty(XDocument document, string propertyName) =>
        document.Descendants(propertyName).Select(x => x.Value.Trim()).FirstOrDefault()
        ?? throw new InvalidOperationException($"Directory.Build.props 缺少 {propertyName}");

    private static string RemoveRevisionComponent(string version) =>
        version.EndsWith(".0", StringComparison.Ordinal) ? version[..^2] : version;

}

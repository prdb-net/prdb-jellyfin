namespace Jellyfin.Plugin.Prdb.Tests;

public class PluginTests
{
    [Fact]
    public void The_configuration_page_is_embedded_where_the_plugin_says_it_is()
    {
        var assembly = typeof(Plugin).Assembly;

        using var stream = assembly.GetManifestResourceStream("Jellyfin.Plugin.Prdb.Configuration.configPage.html");

        Assert.NotNull(stream);
    }

    [Fact]
    public void The_plugin_id_is_stable()
    {
        Assert.Equal(Guid.Parse("74e0efe4-afea-43e4-8b19-1cf114c7755b"), Plugin.PluginId);
    }
}

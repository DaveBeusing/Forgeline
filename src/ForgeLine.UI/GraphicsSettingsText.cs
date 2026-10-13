using System.Globalization;
using System.Resources;

namespace ForgeLine.UI;

public static class GraphicsSettingsText
{
    private static readonly ResourceManager Resources = new("ForgeLine.UI.GraphicsSettingsText", typeof(GraphicsSettingsText).Assembly);
    public static string Get(string key) => Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;
}

using System.Reflection;
using Spectre.Console;
using Yamoh.Infrastructure.Configuration;
using Yamoh.Ui;

namespace Yamoh.Infrastructure.Extensions;

public static class YamohConfigurationExtensions
{
    public static Panel PrintObjectPropertyValues(this IEnumerable<object> objects)
    {
        var table = SpectreConsoleHelper.CreateTable();
        table.AddColumn("category");
        table.AddColumn("property");
        table.AddColumn("value");
        table.Expand();

        foreach (var o in objects)
        {
            var props = o.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance);

            var positionProperty = o.GetType().GetProperty("Position", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            var position = positionProperty?.GetValue(o)?.ToString();
            foreach (var prop in props)
            {
                var value = prop.GetValue(o)?.ToString() ?? "<null>";
                table.AddRow(position ?? "Unknown", prop.Name, value);
            }
        }

        var panel = SpectreConsoleHelper.CreatePanel(table);

        panel.Header = new PanelHeader("Overlay Configuration Used", Justify.Center);
        return panel;
    }
}

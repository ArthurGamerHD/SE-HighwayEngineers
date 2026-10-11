using System;
using System.Collections.Generic;
using VRageMath;

namespace HighwayEngineer.Client.Gui.Styling
{
    /// <summary>
    /// What the IDE preview resolves {DynamicResource} and {StaticResource} against: the theme colors and value
    /// converters MinimapStyles attaches to the planner's roots in game (Fluent palette, default faction accent).
    /// Named by <c>AcruxPreviewDesignResources</c> in HighwayEngineer.csproj; never created by the mod itself (a
    /// *.debug.cs file: compiled for the preview, not packed by MDK).
    /// </summary>
    public sealed class DesignThemeResources : Acrux.Styling.IResourceProvider
    {
        readonly KeyValuePair<string, object>[] _converters = MinimapStyles.CreateConverters();

        public event Action<string> ResourceChanged
        {
            add { }
            remove { }
        }

        public bool TryGetResource<T>(string key, out T value)
        {
            if (typeof(T) == typeof(Color))
            {
                foreach (KeyValuePair<string, Func<Color>> entry in MinimapStyles.ColorResources)
                {
                    if (!string.Equals(entry.Key, key, StringComparison.Ordinal))
                        continue;
                    value = (T)(object)entry.Value();
                    return true;
                }
            }
            else if (typeof(T) == typeof(string) && string.Equals(key, "textFont", StringComparison.Ordinal))
            {
                value = (T)(object)MinimapStyles.TEXT_FONT;
                return true;
            }
            else
            {
                foreach (KeyValuePair<string, object> entry in _converters)
                {
                    if (!string.Equals(entry.Key, key, StringComparison.Ordinal) || !(entry.Value is T))
                        continue;
                    value = (T)entry.Value;
                    return true;
                }
            }
            value = default(T);
            return false;
        }
    }
}

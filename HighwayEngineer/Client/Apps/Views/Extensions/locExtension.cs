using System;
using HighwayEngineer.Common.Helpers;

namespace HighwayEngineer.Client.Apps.Views.Extensions
{
    /// <summary>Resolves a localization key when XAML creates a view: <c>Text="{ext:loc HighwayEngineer_Common_Cancel}"</c>.</summary>
    public sealed class locExtension
    {
        public locExtension(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("A localization key is required.", nameof(key));
            Key = key;
        }

        public string Key { get; }

        public string ProvideValue()
        {
            return LocHelper.GetLoc(Key);
        }
    }
}

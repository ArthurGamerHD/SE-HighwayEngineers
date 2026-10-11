using System;
using System.Collections.Generic;
using System.Globalization;
using Acrux.Data.Converters;
using Acrux.Styling;
using VRageMath;

namespace HighwayEngineer.Client.Apps.Views
{
    /// <summary>Icon texture name to mask brush for <c>Border.OpacityMask</c> (resource "iconMask").</summary>
    internal sealed class IconMaskConverter : IValueConverter
    {
        readonly Dictionary<string, ImageBrush> _brushes = new Dictionary<string, ImageBrush>();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string icon = value as string;
            if (string.IsNullOrWhiteSpace(icon))
                return null;
            ImageBrush brush;
            if (!_brushes.TryGetValue(icon, out brush))
                _brushes[icon] = brush = new ImageBrush(icon) { Stretch = Stretch.Uniform };
            return brush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>Rotation in radians to <c>RenderTransform</c> (resource "iconRotation").</summary>
    internal sealed class RadiansRotationConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return new RotateTransform(value is float ? MathHelper.ToDegrees((float)value) : 0f);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>Icon texture name to image brush for a <c>Background</c> (resource "iconImage").</summary>
    internal sealed class IconImageConverter : IValueConverter
    {
        readonly Dictionary<string, ImageBrush> _brushes = new Dictionary<string, ImageBrush>();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string icon = value as string;
            if (string.IsNullOrWhiteSpace(icon))
                return null;
            ImageBrush brush;
            if (!_brushes.TryGetValue(icon, out brush))
                _brushes[icon] = brush = new ImageBrush(icon) { Stretch = Stretch.Uniform };
            return brush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}

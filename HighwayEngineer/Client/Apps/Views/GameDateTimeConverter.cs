using System;
using System.Globalization;
using Acrux.Data.Converters;
using Sandbox.Game;
using Sandbox.ModAPI;

namespace HighwayEngineer.Client.Apps.Views
{
    /// <summary>
    /// Session game time to the in-game calendar shown to players, where one sun rotation is one
    /// 24-hour day (as the LCD mod's clock). A <see cref="DateTime"/> from
    /// <c>IMySession.GameDateTime</c> becomes days since 2081-01-01 scaled by the world's day
    /// length; ConverterParameter is the format (default "yyyy-MM-dd HH:mm"). Resource "gameDateTime".
    /// </summary>
    internal sealed class GameDateTimeConverter : IValueConverter
    {
        const string DefaultFormat = "yyyy-MM-dd HH:mm";
        static readonly DateTime GameDateEpoch = new DateTime(2081, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is DateTime)) return string.Empty;
            string format = parameter as string;
            return ToSolarDateTime((DateTime)value).ToString(
                string.IsNullOrWhiteSpace(format) ? DefaultFormat : format, culture ?? CultureInfo.CurrentCulture);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        /// <summary>Game time with each sun rotation stretched or squeezed to 24 hours.</summary>
        public static DateTime ToSolarDateTime(DateTime gameDateTime)
        {
            double dayLength = SunDayLengthSeconds();
            double elapsed = Math.Max(0d, (gameDateTime - GameDateEpoch).TotalSeconds);
            double days = elapsed / dayLength;
            double day = Math.Floor(days);
            int seconds = (int)Math.Floor((days - day) * 86400d);
            return GameDateEpoch.AddDays(day).AddSeconds(Math.Max(0, Math.Min(86399, seconds)));
        }

        static double SunDayLengthSeconds()
        {
            try
            {
                return Math.Max(1d, MyVisualScriptLogicProvider.SunRotationGetDayLength());
            }
            catch
            {
                return 86400d;
            }
        }
    }
}

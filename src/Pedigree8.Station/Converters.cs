using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Pedigree8.Core;

namespace Pedigree8.Station
{
    public sealed class LevelBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var key = value is CareLevel level
                ? level switch
                {
                    CareLevel.Good => "Green",
                    CareLevel.Due => "Yellow",
                    CareLevel.Attention => "Orange",
                    CareLevel.Overdue => "Red",
                    CareLevel.Paused => "Purple",
                    _ => "Grey",
                }
                : "Grey";
            return Application.Current.Resources[key];
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}

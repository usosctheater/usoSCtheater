using System.Globalization;
using System.Windows.Data;

namespace SCTheaterEditor.Converters;

/// <summary>
/// 값 == 파라미터 → true (라디오 버튼용). 되돌릴 때 true면 파라미터 값을 대상 형식으로 바꿔 돌려줌, false면 바꾸지 않음
/// 예: IsChecked="{Binding SelectedTrack, Converter={StaticResource Equals}, ConverterParameter=2}"
/// </summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true || parameter == null) return Binding.DoNothing;
        var type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (type.IsEnum) return Enum.Parse(type, parameter.ToString()!, ignoreCase: true);   // 추가(6회차): 열거형 (오디오 구분)
        return System.Convert.ChangeType(parameter.ToString(), type, CultureInfo.InvariantCulture);
    }
}

/// <summary>null이 아니면 true</summary>
public sealed class NotNullToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value != null;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

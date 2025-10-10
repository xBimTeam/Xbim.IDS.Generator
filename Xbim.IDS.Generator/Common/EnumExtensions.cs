using System.ComponentModel;
using System.Reflection;

namespace Xbim.IDS.Generator.Common
{
    public static class EnumExtensions
    {
        public static string ToDescription<TEnum>(this TEnum EnumValue) where TEnum : struct
        {
            return GetEnumDescription((Enum)(object)((TEnum)EnumValue));
        }

        public static string GetEnumDescription(Enum value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            FieldInfo fi = value.GetType().GetField(value.ToString())!;

            if (fi.GetCustomAttributes(typeof(DescriptionAttribute), false) is DescriptionAttribute[] attributes && attributes.Any())
            {
                return attributes.First().Description;
            }

            return value.ToString();
        }
    }
}

using System.ComponentModel;
using System.Reflection;

namespace Lyra.Common.SystemExtensions;

public static class EnumExtensions
{
    public static string Description(this Enum value)
    {
        return value
                   .GetType()
                   .GetField(value.ToString())?
                   .GetCustomAttribute<DescriptionAttribute>()?
                   .Description
               ?? value.ToDisplayString();
    }

    public static string Alias(this Enum value)
    {
        return value
                   .GetType()
                   .GetField(value.ToString())?
                   .GetCustomAttribute<AliasAttribute>()?
                   .Alias
               ?? value.ToDisplayString();
    }

    public static bool HasAttribute<TAttribute>(this Enum value) where TAttribute : Attribute
        => value
            .GetType()
            .GetField(value.ToString())?
            .GetCustomAttribute<TAttribute>() is not null;

    public static string ToDisplayString(this Enum value)
    {
        return System.Text.RegularExpressions.Regex
            .Replace(value.ToString(), "(\\B[A-Z])", " $1");
    }

    public static bool TryParseByAlias<TEnum>(string alias, out TEnum value) where TEnum : struct, Enum
    {
        value = default;

        if (string.IsNullOrWhiteSpace(alias))
            return false;

        foreach (var field in typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var aliasAttr = field.GetCustomAttribute<AliasAttribute>();
            if (aliasAttr != null && string.Equals(aliasAttr.Alias, alias, StringComparison.OrdinalIgnoreCase))
            {
                value = (TEnum)field.GetValue(null)!;
                return true;
            }
        }

        return false;
    }
}

[AttributeUsage(AttributeTargets.Field)]
public class AliasAttribute(string aliasValue) : Attribute
{
    public string Alias => AliasValue;
    private string AliasValue { get; set; } = aliasValue;
}
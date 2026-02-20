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

    public static string ToDisplayString(this Enum value)
    {
        return System.Text.RegularExpressions.Regex
            .Replace(value.ToString(), "(\\B[A-Z])", " $1");
    }
}

[AttributeUsage(AttributeTargets.Field)]
public class AliasAttribute(string aliasValue) : Attribute
{
    public string Alias => AliasValue;
    private string AliasValue { get; set; } = aliasValue;
}
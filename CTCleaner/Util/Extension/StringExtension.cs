using System.Linq;

namespace CTCleaner.Util.Extension;

public static class StringExtension
{
    public static bool IsBase64String(this string value)
    {
        if (value == null || value.Length == 0 || value.Length % 4 != 0
            || value.Contains(' ') || value.Contains('\t') || value.Contains('\r') || value.Contains('\n'))
            return false;
        var index = value.Length - 1;
        if (value[index] == '=')
            index--;
        if (value[index] == '=')
            index--;
        for (var i = 0; i <= index; i++)
            if (IsInvalid(value[i]))
                return false;
        return true;
    }

    private static bool IsInvalid(char value)
    {
        var intValue = (int) value;
        if (intValue >= 48 && intValue <= 57)
            return false;
        if (intValue >= 65 && intValue <= 90)
            return false;
        if (intValue >= 97 && intValue <= 122)
            return false;
        return intValue != 43 && intValue != 47;
    }
}
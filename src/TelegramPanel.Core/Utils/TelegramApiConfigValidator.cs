namespace TelegramPanel.Core.Utils;

public static class TelegramApiConfigValidator
{
    public static bool TryValidateApiIdApiHash(int apiId, string? apiHash, out string? reason)
    {
        if (apiId <= 0)
        {
            reason = "ApiId 无效（必须为正整数）";
            return false;
        }

        if (!TryNormalizeApiHash(apiHash, out _, out reason))
            return false;

        reason = null;
        return true;
    }

    public static bool TryNormalizeApiHash(string? apiHash, out string normalized, out string? reason)
    {
        normalized = (apiHash ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            reason = "ApiHash 不能为空";
            return false;
        }

        if (normalized.Length != 32)
        {
            reason = "ApiHash 格式错误：长度应为 32 位十六进制字符串（0-9a-f）";
            return false;
        }

        for (var i = 0; i < normalized.Length; i++)
        {
            var ch = normalized[i];
            var isHex =
                (ch >= '0' && ch <= '9') ||
                (ch >= 'a' && ch <= 'f') ||
                (ch >= 'A' && ch <= 'F');

            if (!isHex)
            {
                reason = "ApiHash 格式错误：必须为十六进制字符串（0-9a-f）";
                return false;
            }
        }

        reason = null;
        return true;
    }
}


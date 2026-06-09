using System;
using System.Text;
using System.Text.RegularExpressions;

namespace SanitizerKit.Core.Scanners;

public class HardcodedPasswordScanner : IScanner
{
    public bool HasIssue(ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty) return false;

        try
        {
            string text = Encoding.UTF8.GetString(content);
            var regex = new Regex(@"(password|passwd|pass|secret)\s*[:=]\s*(['""])(.*?)\2", RegexOptions.IgnoreCase);
            return regex.IsMatch(text);
        }
        catch
        {
            return false;
        }
    }
}
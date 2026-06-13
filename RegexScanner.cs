using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SanitizerKit.Core.Scanners;

/// <summary>
/// .bomconfig dosyasındaki CustomRules (Özel Kurallar) bölümünü okuyup
/// dosya içeriklerinde Regex tabanlı tarama ve onarım yapan tarayıcı.
/// </summary>
public class RegexScanner
{
    private readonly Dictionary<string, string> _rules;

    public RegexScanner(Dictionary<string, string> customRules)
    {
        _rules = customRules ?? new Dictionary<string, string>();
    }

    public string? GetFirstViolation(string content)
    {
        if (_rules.Count == 0) return null;

        foreach (var rule in _rules)
        {
            if (Regex.IsMatch(content, rule.Key))
            {
                return rule.Key; // İhlal edilen kuralın desenini döndür
            }
        }
        return null;
    }

    public string ApplyFixes(string content)
    {
        if (_rules.Count == 0) return content;

        string fixedContent = content;
        foreach (var rule in _rules)
        {
            fixedContent = Regex.Replace(fixedContent, rule.Key, rule.Value);
        }
        return fixedContent;
    }
}
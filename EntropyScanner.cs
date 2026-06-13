using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace SanitizerKit.Core.Scanners;

public class EntropyScanner : IScanner
{
    private static readonly Regex TokenRegex = new(@"[a-zA-Z0-9+/_\-\.=]{16,128}", RegexOptions.Compiled);
    private static readonly Regex HexRegex = new(@"^[a-fA-F0-9]+$", RegexOptions.Compiled);

    public bool HasIssue(ReadOnlySpan<byte> content)
    {
        try
        {
            string text = Encoding.UTF8.GetString(content.ToArray());
            
            var matches = TokenRegex.Matches(text);
            foreach (Match match in matches)
            {
                string token = match.Value;
                
                if (IsSpamToken(token)) continue;

                double entropy = CalculateEntropy(token);

                if (HexRegex.IsMatch(token))
                {
                    if (token.Length >= 32 && entropy >= 3.0)
                    {
                        return true; 
                    }
                }
                else
                {
                    if (token.Length >= 20 && entropy >= 4.5)
                    {
                        return true; 
                    }
                }
            }
        }
        catch
        {
            // Hata durumunda false dön
        }
        return false;
    }

    public static double CalculateEntropy(string str)
    {
        var freqs = new Dictionary<char, int>();
        foreach (char c in str)
        {
            if (freqs.ContainsKey(c)) freqs[c]++;
            else freqs[c] = 1;
        }

        double entropy = 0.0;
        double len = str.Length;

        foreach (var freq in freqs.Values)
        {
            double p = freq / len;
            entropy -= p * Math.Log2(p);
        }

        return entropy;
    }

    private static bool IsSpamToken(string token)
    {
        if (token.Length > 0)
        {
            char first = token[0];
            bool allSame = true;
            for (int i = 1; i < token.Length; i++)
            {
                if (token[i] != first)
                {
                    allSame = false;
                    break;
                }
            }
            if (allSame) return true;
        }

        if (token.Contains("---") || token.Contains("===") || token.Contains("___"))
        {
            return true;
        }

        return false;
    }
}

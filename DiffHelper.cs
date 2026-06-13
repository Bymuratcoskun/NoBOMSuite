using System;
using System.Collections.Generic;

namespace SanitizerKit.Core.Patching;

public enum DiffType
{
    Unchanged,
    Added,
    Deleted
}

public class DiffLine
{
    public DiffType Type { get; }
    public string Text { get; }

    public DiffLine(DiffType type, string text)
    {
        Type = type;
        Text = text;
    }
}

public static class DiffHelper
{
    public static List<DiffLine> ComputeDiff(string originalText, string newText)
    {
        originalText ??= string.Empty;
        newText ??= string.Empty;

        // Split by newlines, handling \r\n and \n
        string[] originalLines = string.IsNullOrEmpty(originalText) ? Array.Empty<string>() : originalText.Replace("\r", "").Split('\n');
        string[] newLines = string.IsNullOrEmpty(newText) ? Array.Empty<string>() : newText.Replace("\r", "").Split('\n');

        int n = originalLines.Length;
        int m = newLines.Length;

        int[,] opt = new int[n + 1, m + 1];

        for (int i = n - 1; i >= 0; i--)
        {
            for (int j = m - 1; j >= 0; j--)
            {
                if (originalLines[i] == newLines[j])
                {
                    opt[i, j] = opt[i + 1, j + 1] + 1;
                }
                else
                {
                    opt[i, j] = Math.Max(opt[i + 1, j], opt[i, j + 1]);
                }
            }
        }

        var diff = new List<DiffLine>();
        int x = 0;
        int y = 0;

        while (x < n && y < m)
        {
            if (originalLines[x] == newLines[y])
            {
                diff.Add(new DiffLine(DiffType.Unchanged, originalLines[x]));
                x++;
                y++;
            }
            else if (opt[x + 1, y] >= opt[x, y + 1])
            {
                diff.Add(new DiffLine(DiffType.Deleted, originalLines[x]));
                x++;
            }
            else
            {
                diff.Add(new DiffLine(DiffType.Added, newLines[y]));
                y++;
            }
        }

        while (x < n)
        {
            diff.Add(new DiffLine(DiffType.Deleted, originalLines[x]));
            x++;
        }

        while (y < m)
        {
            diff.Add(new DiffLine(DiffType.Added, newLines[y]));
            y++;
        }

        return diff;
    }
}

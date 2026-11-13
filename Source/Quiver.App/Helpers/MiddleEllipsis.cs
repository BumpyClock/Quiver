using System;
using System.Globalization;

namespace Quiver.App.Helpers;

internal static class MiddleEllipsis
{
    public static string Fit(string text, double availableWidth, Func<string, double> measure)
    {
        if (measure(text) <= availableWidth) return text;
        int[] starts = StringInfo.ParseCombiningCharacters(text);
        bool IsSpace(int index) => string.IsNullOrWhiteSpace(StringInfo.GetNextTextElement(text, starts[index]));
        int suffixStart = starts.Length;
        while (suffixStart > 0 && !IsSpace(suffixStart - 1)) suffixStart--;
        bool wholeSuffix = suffixStart > 0 && suffixStart < starts.Length
            && measure("\u2026" + text[starts[suffixStart]..]) <= availableWidth;
        int low = 0;
        int high = wholeSuffix ? suffixStart - 1 : starts.Length - 1;
        string fitted = "\u2026";
        while (low <= high)
        {
            int kept = low + (high - low) / 2;
            int head = wholeSuffix ? kept : (kept + 1) / 2;
            int tail = wholeSuffix ? starts.Length - suffixStart : kept / 2;
            if (head > 0 && !IsSpace(head))
            {
                int boundary = head;
                while (boundary > 0 && !IsSpace(boundary - 1)) boundary--;
                if (boundary > 0) head = boundary;
            }
            if (tail > 0 && !IsSpace(starts.Length - tail - 1))
            {
                int boundary = starts.Length - tail;
                while (boundary < starts.Length && !IsSpace(boundary)) boundary++;
                if (boundary < starts.Length) tail = starts.Length - boundary;
            }
            while (head > 0 && IsSpace(head - 1)) head--;
            while (tail > 0 && IsSpace(starts.Length - tail)) tail--;
            string candidate = text[..starts[head]] + "\u2026"
                + (tail == 0 ? string.Empty : text[starts[^tail]..]);
            if (measure(candidate) <= availableWidth)
            {
                fitted = candidate;
                low = kept + 1;
            }
            else high = kept - 1;
        }
        return fitted;
    }
}

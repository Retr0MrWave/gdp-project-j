using System;
using System.Globalization;
using System.Text.RegularExpressions;

public static class JsonlOrderBookParser
{
    private static readonly Regex SymbolRegex =
        new Regex(@"""symbol"":""(?<v>[^""]+)""", RegexOptions.Compiled);

    private static readonly Regex CapturedAtRegex =
        new Regex(@"""captured_at_ms"":(?<v>\d+)", RegexOptions.Compiled);

    private static readonly Regex PairRegex =
        new Regex(@"\[""(?<p>[^""]+)"",""(?<q>[^""]+)""\]", RegexOptions.Compiled);

    public static bool TryParseLine(string line, out OrderBookSnapshot snapshot)
    {
        snapshot = null;

        if (string.IsNullOrWhiteSpace(line))
            return false;

        try
        {
            OrderBookSnapshot s = new OrderBookSnapshot();

            Match symbolMatch = SymbolRegex.Match(line);
            s.symbol = symbolMatch.Success ? symbolMatch.Groups["v"].Value : string.Empty;

            Match capturedAtMatch = CapturedAtRegex.Match(line);
            s.capturedAtMs = capturedAtMatch.Success
                ? long.Parse(capturedAtMatch.Groups["v"].Value, CultureInfo.InvariantCulture)
                : 0L;

            string bidsSegment = ExtractArraySegment(line, "\"bids\":");
            string asksSegment = ExtractArraySegment(line, "\"asks\":");

            if (!string.IsNullOrEmpty(bidsSegment))
            {
                MatchCollection bidMatches = PairRegex.Matches(bidsSegment);
                for (int i = 0; i < bidMatches.Count; i++)
                {
                    double price = double.Parse(bidMatches[i].Groups["p"].Value, CultureInfo.InvariantCulture);
                    double qty = double.Parse(bidMatches[i].Groups["q"].Value, CultureInfo.InvariantCulture);
                    s.bids.Add(new OrderBookLevel(price, qty));
                }
            }

            if (!string.IsNullOrEmpty(asksSegment))
            {
                MatchCollection askMatches = PairRegex.Matches(asksSegment);
                for (int i = 0; i < askMatches.Count; i++)
                {
                    double price = double.Parse(askMatches[i].Groups["p"].Value, CultureInfo.InvariantCulture);
                    double qty = double.Parse(askMatches[i].Groups["q"].Value, CultureInfo.InvariantCulture);
                    s.asks.Add(new OrderBookLevel(price, qty));
                }
            }

            snapshot = s;
            return true;
        }
        catch
        {
            snapshot = null;
            return false;
        }
    }

    private static string ExtractArraySegment(string input, string key)
    {
        int keyIndex = input.IndexOf(key, StringComparison.Ordinal);
        if (keyIndex < 0)
            return null;

        int start = input.IndexOf('[', keyIndex);
        if (start < 0)
            return null;

        int depth = 0;
        for (int i = start; i < input.Length; i++)
        {
            char c = input[i];
            if (c == '[')
            {
                depth++;
            }
            else if (c == ']')
            {
                depth--;
                if (depth == 0)
                    return input.Substring(start, i - start + 1);
            }
        }

        return null;
    }
}

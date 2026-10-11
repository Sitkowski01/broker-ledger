using System.Globalization;
using BrokerLedger.Domain;

namespace BrokerLedger.Api.Accounts;

/// <summary>
/// Kwoty w JSON-ie to tekst ("250.40"), nie liczba — klient w JS nie zgubi groszy w double.
/// Tylko cyfry i kropka: bez znaku, wykladnika i separatora tysiecy.
/// </summary>
internal static class Amounts
{
    public static bool TryParse(string? text, out decimal amount) =>
        decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount);

    public static string Format(decimal amount, Asset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return amount.ToString("F" + asset.Scale.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }
}

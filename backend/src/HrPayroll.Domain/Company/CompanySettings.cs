using System.Globalization;
using HrPayroll.Domain.Common;
using HrPayroll.Domain.Employees;

namespace HrPayroll.Domain.Company;

// Where the company is: what its national IDs look like and which public holidays are offered.
public enum Country
{
    Egypt = 1,
    SaudiArabia = 2,
}

// Which side of an amount the currency is written on: "L.E 1,400.00" or "1,400.00 SAR".
public enum CurrencyPosition
{
    Before = 1,
    After = 2,
}

// An amount as the company writes it, wherever one is shown: the screens, the Excel sheets and
// the API's own messages alike. With no symbol, the number alone.
public sealed record MoneyFormat(string Symbol, CurrencyPosition Position)
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static MoneyFormat Plain { get; } = new(string.Empty, CurrencyPosition.Before);

    // "L.E 1,400.00"; whole amounts too: "L.E 100,000" with "N0".
    public string Format(decimal amount, string numberFormat = "N2")
    {
        var number = amount.ToString(numberFormat, Invariant);
        if (Symbol.Length == 0)
            return number;
        return Position == CurrencyPosition.Before ? $"{Symbol} {number}" : $"{number} {Symbol}";
    }
}

// The company this system runs for (إعدادات الشركة): one row, kept by a manager. Its name and logo,
// how money is written, the country, the time zone and the hour its workday starts, and its
// usual weekly days off.
//
// The weekends are only what a new employee starts with: changing them never changes anyone's
// days off already set, so no pay already worked out moves. The time zone moves how new punches
// fall into workdays from the moment it is saved; an attendance device takes the new zone when
// it next starts.
public sealed class CompanySettings
{
    public const int SingletonId = 1;
    public const int MaxNameLength = 200;
    public const int MaxCurrencySymbolLength = 8;
    public const int MaxLogoFileNameLength = 200;
    public const int MaxLogoBytes = 1024 * 1024;

    // A logo is shown in the page: a picture, never an SVG, which could carry script.
    public static readonly IReadOnlySet<string> LogoTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "image/png", "image/jpeg", "image/webp" };

    private CompanySettings() { }

    public CompanySettings(
        string name, string currencySymbol, CurrencyPosition currencyPosition, Country country, string timeZoneId, int workdayStartsAtHour,
        WeekDays weekends)
    {
        Update(name, currencySymbol, currencyPosition, country, timeZoneId, workdayStartsAtHour, weekends, updatedBy: null);
    }

    public int Id { get; private init; } = SingletonId;
    public string Name { get; private set; } = string.Empty;
    public string CurrencySymbol { get; private set; } = string.Empty;
    public CurrencyPosition CurrencyPosition { get; private set; } = CurrencyPosition.Before;
    public Country Country { get; private set; } = Country.Egypt;
    // IANA id, as the server's own clock knows it: "Africa/Cairo", "Asia/Riyadh".
    public string TimeZoneId { get; private set; } = string.Empty;
    public int WorkdayStartsAtHour { get; private set; }
    public WeekDays Weekends { get; private set; }
    public string? LogoFileName { get; private set; }
    public string? LogoContentType { get; private set; }
    public int? LogoBytes { get; private set; }
    public DateTime? LogoUpdatedAtUtc { get; private set; }
    public string? UpdatedBy { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public bool HasLogo => LogoFileName is not null;

    public MoneyFormat Money => new(CurrencySymbol, CurrencyPosition);

    public void Update(
        string name, string currencySymbol, CurrencyPosition currencyPosition, Country country, string timeZoneId, int workdayStartsAtHour,
        WeekDays weekends, string? updatedBy)
    {
        var trimmedName = name?.Trim() ?? string.Empty;
        if (trimmedName.Length is 0 or > MaxNameLength)
            throw new DomainException($"Enter the company's name: {MaxNameLength} characters at most.");
        var symbol = currencySymbol?.Trim() ?? string.Empty;
        if (symbol.Length > MaxCurrencySymbolLength)
            throw new DomainException($"Keep the currency symbol to {MaxCurrencySymbolLength} characters: L.E, $, SAR.");
        if (!Enum.IsDefined(currencyPosition))
            throw new DomainException("Choose whether the currency goes before the amount or after it.");
        if (!Enum.IsDefined(country))
            throw new DomainException("Choose the country: Egypt or Saudi Arabia.");
        var zone = timeZoneId?.Trim() ?? string.Empty;
        if (!IsTimeZone(zone))
            throw new DomainException($"\"{zone}\" is not a time zone this server knows: choose one from the list.");
        if (workdayStartsAtHour is < 0 or > 12)
            throw new DomainException("The workday starts between midnight (0) and noon (12).");
        if (weekends == WeekDaysExtensions.All || (weekends & ~WeekDaysExtensions.All) != 0)
            throw new DomainException("Leave at least one working day in the week.");

        Name = trimmedName;
        CurrencySymbol = symbol;
        CurrencyPosition = currencyPosition;
        Country = country;
        TimeZoneId = zone;
        WorkdayStartsAtHour = workdayStartsAtHour;
        Weekends = weekends;
        if (updatedBy is not null)
        {
            UpdatedBy = updatedBy;
            UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    // A PNG, JPEG or WebP of up to a megabyte, replacing the one there was.
    public void AttachLogo(string fileName, string contentType, int bytes, string updatedBy)
    {
        if (!LogoTypes.Contains(contentType))
            throw new DomainException("A logo is a PNG, JPEG or WebP picture.");
        if (bytes is <= 0 or > MaxLogoBytes)
            throw new DomainException("A logo is a megabyte at most: save a smaller picture.");
        var name = Path.GetFileName(fileName?.Trim() ?? string.Empty);
        LogoFileName = name.Length is > 0 and <= MaxLogoFileNameLength ? name : "logo";
        LogoContentType = contentType.ToLowerInvariant();
        LogoBytes = bytes;
        LogoUpdatedAtUtc = DateTime.UtcNow;
        UpdatedBy = updatedBy;
        UpdatedAtUtc = LogoUpdatedAtUtc;
    }

    public void RemoveLogo(string updatedBy)
    {
        LogoFileName = null;
        LogoContentType = null;
        LogoBytes = null;
        LogoUpdatedAtUtc = DateTime.UtcNow;
        UpdatedBy = updatedBy;
        UpdatedAtUtc = LogoUpdatedAtUtc;
    }

    public static bool IsTimeZone(string timeZoneId)
    {
        if (timeZoneId.Length == 0)
            return false;
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }
}

// What a national ID is in each country, beside a passport's number (its letters and digits),
// which is taken anywhere.
public static class NationalIds
{
    // What is wrong with one already cleaned to its digits and capitals; null when it is right.
    public static string? Problem(string number, Country country)
    {
        if (number.All(char.IsAsciiDigit))
        {
            return country switch
            {
                Country.SaudiArabia => number.Length == 10 && number[0] is '1' or '2'
                    ? null
                    : "A national ID or Iqama is 10 digits, starting 1 (a citizen's) or 2 (a resident's); a passport's number, its letters and digits.",
                _ => number.Length == 14 ? null : "A national ID is the card's 14 digits; a passport's number, its letters and digits.",
            };
        }

        return number.Length is >= 5 and <= Employee.MaxNationalIdLength && number.All(char.IsAsciiLetterOrDigit)
            ? null
            : "A passport's number is its letters and digits, 5 to 20 of them.";
    }
}

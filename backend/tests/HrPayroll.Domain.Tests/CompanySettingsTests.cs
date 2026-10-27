using HrPayroll.Domain.Common;
using HrPayroll.Domain.Company;
using HrPayroll.Domain.Employees;
using HrPayroll.Domain.Performance;

namespace HrPayroll.Domain.Tests;

// The company's settings: how money is written, and what a manager may set.
public class CompanySettingsTests
{
    private static CompanySettings Egypt() =>
        new("Acme", "L.E", CurrencyPosition.Before, Country.Egypt, "Africa/Cairo", 4, WeekDays.Friday);

    [Fact]
    public void Money_is_written_with_the_currency_before_or_after_or_alone()
    {
        Assert.Equal("L.E 1,400.00", new MoneyFormat("L.E", CurrencyPosition.Before).Format(1_400m));
        Assert.Equal("1,400.50 SAR", new MoneyFormat("SAR", CurrencyPosition.After).Format(1_400.5m));
        Assert.Equal("$ 100,000", new MoneyFormat("$", CurrencyPosition.Before).Format(100_000m, "N0"));
        Assert.Equal("1,400.00", MoneyFormat.Plain.Format(1_400m));
        Assert.Equal("L.E 1,400.00", Egypt().Money.Format(1_400m));
    }

    [Fact]
    public void A_manager_sets_the_name_currency_country_time_zone_workday_and_weekends()
    {
        var settings = Egypt();
        settings.Update(" Acme KSA ", " SAR ", CurrencyPosition.After, Country.SaudiArabia, "Asia/Riyadh", 5, WeekDays.Friday | WeekDays.Saturday, "Omar");

        Assert.Equal(("Acme KSA", "SAR", CurrencyPosition.After, Country.SaudiArabia, "Asia/Riyadh", 5, WeekDays.Friday | WeekDays.Saturday, "Omar"),
            (settings.Name, settings.CurrencySymbol, settings.CurrencyPosition, settings.Country, settings.TimeZoneId, settings.WorkdayStartsAtHour,
                settings.Weekends, settings.UpdatedBy));

        void Refused(string why, Action update) => Assert.Contains(why, Assert.Throws<DomainException>(update).Message);
        Refused("company's name", () => settings.Update(" ", "SAR", CurrencyPosition.After, Country.SaudiArabia, "Asia/Riyadh", 4, WeekDays.Friday, "Omar"));
        Refused("8 characters", () => settings.Update("Acme", "RIYALS-SA", CurrencyPosition.After, Country.SaudiArabia, "Asia/Riyadh", 4, WeekDays.Friday, "Omar"));
        Refused("not a time zone", () => settings.Update("Acme", "SAR", CurrencyPosition.After, Country.SaudiArabia, "Mars/Olympus", 4, WeekDays.Friday, "Omar"));
        Refused("between midnight", () => settings.Update("Acme", "SAR", CurrencyPosition.After, Country.SaudiArabia, "Asia/Riyadh", 13, WeekDays.Friday, "Omar"));
        Refused("one working day", () => settings.Update("Acme", "SAR", CurrencyPosition.After, Country.SaudiArabia, "Asia/Riyadh", 4, WeekDaysExtensions.All, "Omar"));
        Refused("the country", () => settings.Update("Acme", "SAR", CurrencyPosition.After, (Country)9, "Asia/Riyadh", 4, WeekDays.Friday, "Omar"));
        // Refused, nothing changed.
        Assert.Equal("Acme KSA", settings.Name);
    }

    [Fact]
    public void A_logo_is_a_picture_of_a_megabyte_at_most()
    {
        var settings = Egypt();
        settings.AttachLogo("../acme.png", "image/png", 2_048, "Omar");
        Assert.Equal((true, "acme.png", "image/png", 2_048), (settings.HasLogo, settings.LogoFileName, settings.LogoContentType, settings.LogoBytes));

        Assert.Throws<DomainException>(() => settings.AttachLogo("acme.svg", "image/svg+xml", 2_048, "Omar"));
        Assert.Throws<DomainException>(() => settings.AttachLogo("big.png", "image/png", CompanySettings.MaxLogoBytes + 1, "Omar"));

        settings.RemoveLogo("Omar");
        Assert.False(settings.HasLogo);
    }

    [Fact]
    public void A_penalty_deductions_limit_is_said_in_the_companys_money()
    {
        var refused = Assert.Throws<DomainException>(() => new DisciplinaryAction(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 12), "Absence", SeverityLevel.PenaltyDeduction, null, 100_001m, "u", "Omar",
            new MoneyFormat("SAR", CurrencyPosition.After)));
        Assert.Equal("A penalty deduction is at most 100,000 SAR.", refused.Message);
    }
}

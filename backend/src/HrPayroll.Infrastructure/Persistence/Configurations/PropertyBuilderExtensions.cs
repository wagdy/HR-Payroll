using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HrPayroll.Infrastructure.Persistence.Configurations;

internal static class PropertyBuilderExtensions
{
    // Stored by name: a row stays readable, and adding an enum value can never change what
    // existing rows mean.
    public static PropertyBuilder<TEnum> IsEnumName<TEnum>(this PropertyBuilder<TEnum> property) =>
        property.HasConversion<string>().HasMaxLength(32);
}

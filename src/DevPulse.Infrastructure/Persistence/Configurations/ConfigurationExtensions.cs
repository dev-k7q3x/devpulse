using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevPulse.Infrastructure.Persistence.Configurations;

internal static class ConfigurationExtensions
{
    public const int EnumMaxLength = 16;

    /// <summary>Enum jako text + CHECK z listą nazw – czytelne w SQL, dodanie wartości to zmiana CHECK.</summary>
    public static PropertyBuilder<TEnum> HasEnumAsText<TEnum>(this PropertyBuilder<TEnum> property)
        where TEnum : struct, Enum =>
        property.HasConversion<string>().HasMaxLength(EnumMaxLength);

    public static string EnumCheckSql<TEnum>(string column)
        where TEnum : struct, Enum =>
        $"{column} IN ({string.Join(", ", Enum.GetNames<TEnum>().Select(name => $"'{name}'"))})";

    /// <summary>
    /// Default w bazie (dla ręcznego SQL), ale EF zawsze zapisuje wartość z encji. Bez tego wartość CLR-owa domyślna
    /// (np. godzina 0, <c>false</c>) byłaby uznana za „nieustawioną” i zastąpiona defaultem z bazy.
    /// </summary>
    public static PropertyBuilder<T> HasDatabaseDefault<T>(this PropertyBuilder<T> property, T value) =>
        property.HasDefaultValue(value).ValueGeneratedNever();

    public static PropertyBuilder<T> HasDatabaseDefaultSql<T>(this PropertyBuilder<T> property, string sql) =>
        property.HasDefaultValueSql(sql).ValueGeneratedNever();
}

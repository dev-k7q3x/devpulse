namespace DevPulse.Application.DigestHistory;

/// <summary>
/// Stałe kody zapisywane w <c>digests.error</c> zamiast treści wyjątku (plan: pole trafia do użytkownika).
/// <c>SendDigestJob</c> (E5A) zapisuje wyłącznie te wartości; historia tłumaczy je na opis dla użytkownika.
/// </summary>
public static class DigestErrorCodes
{
    /// <summary>SendGrid zwrócił błąd albo nie odpowiedział.</summary>
    public const string EmailDeliveryFailed = "email-delivery-failed";

    /// <summary>Brak klucza lub adresu nadawcy – błąd trwały, bez ponawiania.</summary>
    public const string EmailNotConfigured = "email-not-configured";

    /// <summary>Użytkownik nie ma potwierdzonego adresu email.</summary>
    public const string NoRecipient = "no-recipient";

    /// <summary>Nie udało się złożyć albo wyrenderować treści.</summary>
    public const string ContentFailed = "content-failed";
}

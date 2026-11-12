// Wymagania dla NOWEGO hasła (zakładanie konta, zmiana własnego, ustawienie przez administratora).
// Istniejące hasła — także domyślne admin/admin — działają dalej, dopóki ktoś ich nie zmieni;
// reguła nie wylogowuje nikogo i nie wymusza zmiany przy logowaniu.
static class PasswordPolicy
{
    public const int MinLength = 8;

    // Zwraca opis błędu albo null, gdy hasło spełnia wymagania.
    public static string? Validate(string? password) =>
        string.IsNullOrEmpty(password) || password.Length < MinLength
            ? $"The password must be at least {MinLength} characters long."
            : null;
}

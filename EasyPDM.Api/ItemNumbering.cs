using System.Globalization;
using Npgsql;

// Numer elementu w bazie (items.item_number) jest zawsze samą liczbą. To, co widzi
// użytkownik, składa się z dwóch rzeczy: prefiksu rodzaju (items.item_number_prefix,
// ZAMROŻONEGO na elemencie przy jego tworzeniu) i tej liczby dopełnionej zerami do
// minimalnej szerokości z ustawień (system_state.item_number_digits).
//
// OBIE części są zamrażane na elemencie przy jego tworzeniu i nigdy nieprzeliczane wstecz.
// Dopełnienie było początkowo formatem wyświetlania (liczonym z bieżącego ustawienia), ale
// to psuło rzecz najważniejszą: makro CAD zapisuje plik na dysku pod nazwą zbudowaną z tego
// numeru, więc element zapisany jako "5 (Wspornik).A" po włączeniu dopełniania pokazywałby
// się jako "0005", podczas gdy plik -- i wgrany już załącznik -- dalej nazywałby się "5".
// Numer elementu i nazwa jego pliku muszą być tym samym, więc format musi być tak samo
// trwały jak sam numer.
//
// Jedno miejsce tutaj, a odpowiedzi API dorzucają gotowe "itemNumberLabel" obok
// "itemNumber"/"itemNumberPrefix" — dokładnie tym samym wzorcem, co RevisionLabeling
// dorzuca "revisionLabel". Bez tego cztery klienty (frontend web i trzy makra CAD)
// składałyby tę samą nazwę każdy po swojemu, a makra robiły to dotąd pomijając prefiks,
// przez co plik na dysku nazywał się inaczej niż element w bazie.
static class ItemNumbering
{
    // digits: wartość ZAMROŻONA NA ELEMENCIE (items.item_number_digits), nie bieżące
    // ustawienie — null albo 0 oznacza "bez dopełniania" (dotychczasowe 1, 2, 3). Numer
    // DŁUŻSZY niż digits nie jest przycinany: to minimalna szerokość, nie format o stałej
    // długości, więc przekroczenie 9999 przy ustawieniu 4 daje po prostu 10000.
    public static string? Label(int? number, string? prefix, int? digits)
    {
        if (number is null) return null;

        var text = number.Value.ToString(CultureInfo.InvariantCulture);
        if (digits is > 0)
            text = text.PadLeft(digits.Value, '0');
        return (prefix ?? "") + text;
    }

    // Pełna "nazwa rekordu": numer i -- opcjonalnie -- nazwa elementu w nawiasie. To jest to,
    // co widać w drzewku i co makro CAD nadaje plikowi na dysku (dokładając jeszcze literę
    // rewizji i rozszerzenie). Folder/Plik nie mają numeru, więc zostaje sama nazwa.
    //
    // withName == false odcina nazwę: "C0005" zamiast "C0005(płyta)". NULL znaczy "wchodzi"
    // -- tak zachowują się wszystkie elementy sprzed wprowadzenia tej opcji.
    public static string RecordName(int? number, string? prefix, int? digits, bool? withName, string fileName)
    {
        var label = Label(number, prefix, digits);
        if (label is null) return fileName;
        return withName == false ? label : $"{label}({fileName})";
    }

    // Jedno zapytanie o jednowierszową tabelę. system_state bywa pusta aż do pierwszego
    // zasiania przykładowego projektu, a migracja 054 wstawia ten wiersz — ale brak wiersza
    // (np. baza sprzed migracji w testach) musi dawać 0, a nie wyjątek.
    public static async Task<int> GetDigitsAsync(NpgsqlConnection conn, NpgsqlTransaction? tx = null)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT item_number_digits FROM system_state WHERE id = true;", conn, tx);
        var value = await cmd.ExecuteScalarAsync();
        return value is int digits ? digits : 0;
    }

    // Jak wyżej, dla drugiego ustawienia globalnego. Brak wiersza = true, czyli zachowanie
    // sprzed wprowadzenia tej opcji.
    public static async Task<bool> GetWithNameAsync(NpgsqlConnection conn, NpgsqlTransaction? tx = null)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT item_number_with_name FROM system_state WHERE id = true;", conn, tx);
        var value = await cmd.ExecuteScalarAsync();
        return value is not bool withName || withName;
    }
}

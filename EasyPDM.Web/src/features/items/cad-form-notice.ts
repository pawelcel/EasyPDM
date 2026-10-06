// Powiadomienie systemowe, gdy makro CAD podaje tej karcie formularz, a człowiek patrzy
// akurat gdzie indziej.
//
// Po co: prośbę podejmuje karta, która JUŻ jest otwarta — nic się więc nie otwiera i nic
// samo nie wyciąga przeglądarki na wierzch. Makro FreeCAD przestało wprawdzie zabierać fokus
// swoim oknem oczekiwania, ale to nie pokrywa wszystkiego: przeglądarka bywa zminimalizowana
// albo na innym pulpicie wirtualnym, a pod Wayland żadna aplikacja nie ma prawa wyciągnąć
// okna innej. Kliknięcie w powiadomienie to jedyna droga, która działa WSZĘDZIE — bo to
// użytkownik przełącza się sam, a system tylko mu to podsuwa.
//
// Nic tu nie jest krytyczne: bez zgody na powiadomienia (albo w przeglądarce, która ich nie
// ma) wszystko działa dokładnie jak dotąd, tylko bez podpowiedzi.

const PERMISSION_ASKED_KEY = "pdm_cad_notice_asked"

// O zgodę pytamy TYLKO z obsługi kliknięcia — przeglądarki wymagają świeżej interakcji
// użytkownika i odrzucają pytanie zadane "ot tak", w tle. Stąd wywołanie przy przyciskach
// okna z prośbą z makra: to pierwsze kliknięcie w całym przepływie CAD-owym, a jednocześnie
// moment, w którym widać, po co ta zgoda ma być.
//
// Pytamy RAZ na przeglądarkę. Odmowa jest odpowiedzią i nie ma być powtarzana przy każdym
// kolejnym komponencie.
export function askForCadFormNotices() {
  try {
    if (typeof Notification === "undefined") return
    if (Notification.permission !== "default") return
    if (localStorage.getItem(PERMISSION_ASKED_KEY)) return
    localStorage.setItem(PERMISSION_ASKED_KEY, "1")
    void Notification.requestPermission()
  } catch {
    // Prywatne okno, zablokowane dane stron, starsza przeglądarka — nie nasz problem.
  }
}

export function showCadFormNotice(title: string, body: string) {
  try {
    if (typeof Notification === "undefined" || Notification.permission !== "granted") return
    // Karta ma fokus, czyli człowiek już na nią patrzy — powiadomienie byłoby wtedy samym
    // hałasem. Tak jest przy pierwszym komponencie biegu, który sam otwiera tę kartę.
    if (document.hasFocus()) return

    // Jeden "tag" na wszystkie: przy złożeniu na kilkadziesiąt części liczy się ostatnia
    // prośba, a nie stos powiadomień o tych, które już obsłużono.
    const notice = new Notification(title, { body, tag: "pdm-cad-form" })
    notice.onclick = () => {
      window.focus()
      notice.close()
    }
  } catch {
    // j.w.
  }
}

# EasyPDM add-on for FreeCAD: an "EasyPDM" menu in every workbench with Log in, Log out,
# Upload and Download, instead of running the macros by hand.
#
# Upload and Download are the macros themselves (EasyPDMUpload.FCMacro, EasyPDMDownload.FCMacro
# next to this file): each menu click loads the macro afresh into its own namespace and calls its
# entry point, exactly as Macro > Execute runs it, so the add-on and the macros can never drift
# apart and nothing of the live-tested upload/download logic is copied here. They share the
# macros' settings (User parameter:BaseApp/EasyPDM: ApiBaseUrl, SessionToken, SavedDisplayName),
# so a login here is the macros' login too.
#
# Log in approves the sign-in in the browser (POST /api/auth/device/start, DeviceLoginEndpoints
# on the server): FreeCAD never sees the password, and gets its own macro token, listed and
# revocable under Settings > Users. "Log in with password" falls back to the macros' own login
# dialog, for servers older than 0.9.

import json
import os
import urllib.error
import urllib.request
import webbrowser

import FreeCAD as App
import FreeCADGui as Gui

try:
    from PySide import QtCore, QtGui
    QtWidgets = QtGui
except ImportError:  # pragma: no cover
    from PySide6 import QtCore, QtGui, QtWidgets  # type: ignore

if not hasattr(QtWidgets, "QDialog"):
    from PySide import QtWidgets  # FreeCAD's PySide shim keeps widgets there on Qt 6

PARAM_GROUP = "User parameter:BaseApp/EasyPDM"
HERE = os.path.dirname(os.path.abspath(__file__))
CLIENT_NAME = "EasyPDM for FreeCAD"


# Menu texts in FreeCAD's own language (Preferences > General > Language), as the macros do.
_TEXTS = {
    "pl": {"Upload": "Wyślij (Upload)", "Download": "Pobierz (Download)", "Log in…": "Zaloguj…", "Log out": "Wyloguj"},
    "de": {"Upload": "Hochladen", "Download": "Herunterladen", "Log in…": "Anmelden…", "Log out": "Abmelden"},
}


def _tr(text):
    lang = App.ParamGet("User parameter:BaseApp/Preferences/General").GetString("Language", "")
    # Empty means FreeCAD follows the system language.
    code = {"Polish": "pl", "German": "de"}.get(lang, lang[:2].lower()) if lang else QtCore.QLocale.system().name()[:2]
    return _TEXTS.get(code, {}).get(text, text)


def _params():
    return App.ParamGet(PARAM_GROUP)


def _base_url():
    return _params().GetString("ApiBaseUrl", "http://localhost:5000/api").rstrip("/")


def _server_url():
    base = _base_url()
    return base[:-4] if base.endswith("/api") else base


def _token():
    return _params().GetString("SessionToken", "") or None


def _signed_in_as():
    return _params().GetString("SavedDisplayName", "") or None


def _post(url, body, token=None):
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Cookie"] = "pdm_session=" + token
    req = urllib.request.Request(url, data=json.dumps(body).encode("utf-8"), method="POST", headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=15) as r:
            raw = r.read()
            return r.status, (json.loads(raw) if raw else None)
    except urllib.error.HTTPError as e:
        raw = e.read()
        try:
            return e.code, json.loads(raw) if raw else None
        except ValueError:
            return e.code, {"detail": raw.decode("utf-8", "replace")}


def load_macro(file_name):
    """The macro as a module: its globals, without running it (its guard needs __main__)."""
    path = os.path.join(HERE, file_name)
    with open(path, encoding="utf-8") as f:
        source = f.read()
    namespace = {"__name__": "easypdm_" + os.path.splitext(file_name)[0].lower(), "__file__": path}
    exec(compile(source, path, "exec"), namespace)
    return namespace


class DeviceLoginDialog(QtWidgets.QDialog):
    """Server address, then the browser: shows the code, polls until the person allows it."""

    def __init__(self, parent=None):
        super().__init__(parent or Gui.getMainWindow())
        self.setWindowTitle("EasyPDM – Log in")
        self.setMinimumWidth(460)
        layout = QtWidgets.QVBoxLayout(self)
        layout.addWidget(QtWidgets.QLabel("EasyPDM server address:"))
        self.server = QtWidgets.QLineEdit(_server_url())
        layout.addWidget(self.server)
        self.info = QtWidgets.QLabel(
            "EasyPDM opens in the browser. Check that it shows the same code as here and click Allow.")
        self.info.setWordWrap(True)
        layout.addWidget(self.info)
        self.code = QtWidgets.QLabel("")
        font = self.code.font()
        font.setPointSize(font.pointSize() + 6)
        font.setBold(True)
        self.code.setFont(font)
        self.code.setAlignment(QtCore.Qt.AlignCenter)
        layout.addWidget(self.code)
        buttons = QtWidgets.QHBoxLayout()
        self.password_button = QtWidgets.QPushButton("Log in with password…")
        self.start_button = QtWidgets.QPushButton("Log in in the browser")
        self.start_button.setDefault(True)
        cancel = QtWidgets.QPushButton("Cancel")
        buttons.addWidget(self.password_button)
        buttons.addStretch(1)
        buttons.addWidget(cancel)
        buttons.addWidget(self.start_button)
        layout.addLayout(buttons)
        self.start_button.clicked.connect(self.start)
        self.password_button.clicked.connect(self.with_password)
        cancel.clicked.connect(self.reject)
        self.timer = QtCore.QTimer(self)
        self.timer.timeout.connect(self.poll)
        self.device_code = None
        self.ticks = 0

    def _server(self):
        url = self.server.text().strip().rstrip("/")
        if url and "://" not in url:
            url = "http://" + url
        return url[:-4] if url.endswith("/api") else url

    def start(self):
        server = self._server()
        if not server:
            return
        try:
            status, body = _post(server + "/api/auth/device/start", {"client": CLIENT_NAME})
        except Exception as e:
            self.info.setText("No connection to %s: %s" % (server, e))
            return
        if status == 404:
            self.info.setText("This EasyPDM server is older than 0.9. Use “Log in with password…”.")
            return
        if status != 200 or not body:
            self.info.setText("The server refused the sign-in (%s)." % status)
            return
        self.device_code = body["deviceCode"]
        self.code.setText(body["userCode"])
        self.info.setText("Waiting for you to allow the sign-in in the browser…")
        self.start_button.setEnabled(False)
        self.server.setEnabled(False)
        self.ticks = 0
        webbrowser.open(server + body["verificationPath"])
        self.timer.start(2000)

    def poll(self):
        self.ticks += 1
        if self.ticks > 300:
            self.timer.stop()
            self.info.setText("The sign-in was not allowed in time. Try again.")
            self.start_button.setEnabled(True)
            self.server.setEnabled(True)
            return
        server = self._server()
        try:
            status, body = _post(server + "/api/auth/device/token", {"deviceCode": self.device_code})
        except Exception:
            return  # a passing network hiccup; the next tick tries again
        if status == 202:
            return
        self.timer.stop()
        if status == 200 and body and body.get("sessionToken"):
            p = _params()
            p.SetString("ApiBaseUrl", server + "/api")
            p.SetString("SessionToken", body["sessionToken"])
            p.SetString("SavedDisplayName", body.get("displayName") or body.get("username") or "")
            self.accept()
            return
        self.info.setText((body or {}).get("detail") or "The sign-in did not go through (%s)." % status)
        self.start_button.setEnabled(True)
        self.server.setEnabled(True)

    def with_password(self):
        server = self._server()
        if server:
            _params().SetString("ApiBaseUrl", server + "/api")
        self.timer.stop()
        macro = load_macro("EasyPDMUpload.FCMacro")
        if macro["LoginDialog"]().exec() == QtWidgets.QDialog.Accepted:
            self.accept()


def log_in():
    dialog = DeviceLoginDialog()
    if dialog.exec() == QtWidgets.QDialog.Accepted:
        App.Console.PrintMessage("EasyPDM: logged in as %s\n" % (_signed_in_as() or "?"))
        QtWidgets.QMessageBox.information(Gui.getMainWindow(), "EasyPDM",
                                          "Logged in to %s as %s." % (_server_url(), _signed_in_as() or "?"))


def log_out():
    token = _token()
    if token:
        try:
            _post(_base_url() + "/auth/logout", {}, token)
        except Exception:
            pass  # signed out here anyway; the token can be revoked under Settings > Users
    _params().SetString("SessionToken", "")
    _params().SetString("SavedDisplayName", "")
    QtWidgets.QMessageBox.information(Gui.getMainWindow(), "EasyPDM", "Logged out of EasyPDM.")


class _Command:
    def __init__(self, text, tip, action, needs_document=False):
        self.text, self.tip, self.action, self.needs_document = text, tip, action, needs_document

    def GetResources(self):
        return {"MenuText": _tr(self.text), "ToolTip": self.tip,
                "Pixmap": os.path.join(HERE, "icons", "EasyPDM.svg")}

    def IsActive(self):
        return App.ActiveDocument is not None if self.needs_document else True

    def Activated(self):
        try:
            self.action()
        except Exception as e:  # never let a click take FreeCAD down
            App.Console.PrintError("EasyPDM: %s\n" % e)
            QtWidgets.QMessageBox.critical(Gui.getMainWindow(), "EasyPDM", str(e))


COMMANDS = [
    ("EasyPDM_Upload", _Command("Upload", "Send the active document to EasyPDM",
                                lambda: load_macro("EasyPDMUpload.FCMacro")["run"](), needs_document=True)),
    ("EasyPDM_Download", _Command("Download", "Download an item from EasyPDM with its components",
                                  lambda: load_macro("EasyPDMDownload.FCMacro")["main"]())),
    ("EasyPDM_LogIn", _Command("Log in…", "Sign in to EasyPDM by approving it in the browser", log_in)),
    ("EasyPDM_LogOut", _Command("Log out", "Sign out of EasyPDM", log_out)),
]


def register_commands():
    for name, command in COMMANDS:
        Gui.addCommand(name, command)


# The menu: FreeCAD rebuilds the menu bar whenever the workbench changes, so the EasyPDM menu is
# put back after every switch, at the end of the bar, before Help when there is one.
def _ensure_menu(*_):
    mw = Gui.getMainWindow()
    bar = mw.menuBar()
    for action in bar.actions():
        if action.objectName() == "EasyPDM_Menu" or action.text().replace("&", "") == "EasyPDM":
            return
    menu = QtWidgets.QMenu("&EasyPDM", mw)
    menu.menuAction().setObjectName("EasyPDM_Menu")
    for name, command in COMMANDS:
        if name == "EasyPDM_LogIn":
            menu.addSeparator()
        action = menu.addAction(_tr(command.text))
        action.setToolTip(command.tip)
        action.triggered.connect(lambda checked=False, n=name: Gui.runCommand(n))
    help_action = next((a for a in bar.actions() if a.text().replace("&", "").lower() in ("help", "pomoc", "hilfe")), None)
    if help_action is not None:
        bar.insertMenu(help_action, menu)
    else:
        bar.addMenu(menu)


def install_menu():
    mw = Gui.getMainWindow()
    mw.workbenchActivated.connect(lambda *_: QtCore.QTimer.singleShot(0, _ensure_menu))
    QtCore.QTimer.singleShot(0, _ensure_menu)

# EasyPDM add-on: loaded by FreeCAD at start-up with the GUI. See easypdm_addon.py.
import FreeCAD as App

try:
    import easypdm_addon

    easypdm_addon.register_commands()
    easypdm_addon.install_menu()
except Exception as e:  # an add-on must never stop FreeCAD from starting
    App.Console.PrintError("EasyPDM add-on failed to load: %s\n" % e)

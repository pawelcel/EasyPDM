# EasyPDM add-on: loaded by FreeCAD at start-up with the GUI. The add-on uses FreeCAD's
# namespaced layout (freecad/EasyPDM/, assembled by build_addon.sh), so FreeCAD imports this
# module itself -- no InitGui.py and no changes to sys.path. See easypdm_addon.py.
import FreeCAD as App
import FreeCADGui as Gui

try:
    from . import easypdm_addon

    easypdm_addon.register_commands()
    Gui.addWorkbench(easypdm_addon.EasyPDMWorkbench())
    easypdm_addon.install_menu()
except Exception as e:  # an add-on must never stop FreeCAD from starting
    App.Console.PrintError("EasyPDM add-on failed to load: %s\n" % e)

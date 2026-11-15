// EasyPDM add-in for SOLIDWORKS: an EasyPDM menu and toolbar with Upload, Download, Log in and
// Log out.
//
// The commands run the EasyPDM macros themselves -- EasyPDMUpload.swp and EasyPDMDownload.swp,
// placed next to this DLL -- through ISldWorks.RunMacro2, the same way Tools > Macro > Run does.
// None of the upload or download logic is copied here, so the add-in and the live-tested macros
// cannot drift apart (the FreeCAD add-on works the same way). Log in runs the Upload macro's
// LoginInBrowser (sign-in approved in the browser), Log out its Logout.
//
// Registration (install.cmd, as administrator): regasm /codebase registers the COM class, and the
// ComRegisterFunction below adds the keys SOLIDWORKS reads its add-in list from.

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;

namespace EasyPDM.SolidWorksAddin
{
    [ComVisible(true)]
    [Guid("91ca2960-0c65-4524-8727-c0fc38207edf")]
    [ProgId("EasyPDM.SolidWorksAddin")]
    public class EasyPdmAddin : ISwAddin
    {
        private const int GroupId = 0x45504D; // "EPM"
        private const string Title = "EasyPDM";

        private ISldWorks _sw;
        private ICommandManager _commands;
        private int _cookie;

        // The four commands: menu text, tooltip, callback method (called by SOLIDWORKS by name, so
        // these methods must stay public), icon letter and colour.
        private static readonly (string Text, string Tip, string Callback, string Letter, Color Colour)[] Commands =
        {
            ("Upload", "Send the active document to EasyPDM", nameof(OnUpload), "U", Color.FromArgb(29, 78, 216)),
            ("Download", "Download an item from EasyPDM with its components", nameof(OnDownload), "D", Color.FromArgb(21, 128, 61)),
            ("Log in", "Sign in to EasyPDM by approving it in the browser", nameof(OnLogIn), "I", Color.FromArgb(71, 85, 105)),
            ("Log out", "Sign out of EasyPDM", nameof(OnLogOut), "O", Color.FromArgb(185, 28, 28)),
        };

        public bool ConnectToSW(object ThisSW, int Cookie)
        {
            _sw = (ISldWorks)ThisSW;
            _cookie = Cookie;
            _sw.SetAddinCallbackInfo2(0, this, Cookie);
            _commands = _sw.GetCommandManager(Cookie);
            try
            {
                CreateCommands();
            }
            catch (Exception e)
            {
                _sw.SendMsgToUser2("EasyPDM add-in: could not create its menu: " + e.Message,
                    (int)swMessageBoxIcon_e.swMbWarning, (int)swMessageBoxBtn_e.swMbOk);
            }
            return true;
        }

        public bool DisconnectFromSW()
        {
            try { _commands?.RemoveCommandGroup2(GroupId, true); } catch { /* SOLIDWORKS is closing */ }
            if (_commands != null) Marshal.ReleaseComObject(_commands);
            _commands = null;
            _sw = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            return true;
        }

        private void CreateCommands()
        {
            int errors = 0;
            // A group left from an earlier version with other commands is rebuilt, not reused.
            var ids = new int[Commands.Length];
            for (var i = 0; i < ids.Length; i++) ids[i] = i + 1;
            object registryIds;
            var changed = _commands.GetGroupDataFromRegistry(GroupId, out registryIds)
                && !IdsMatch(registryIds as int[], ids);

            var group = _commands.CreateCommandGroup2(GroupId, Title, Title, Title, -1, changed, ref errors);
            var icons = Icons.Write();
            group.IconList = icons.Strips;
            group.MainIconList = icons.Main;

            var where = (int)(swCommandItemType_e.swMenuItem | swCommandItemType_e.swToolbarItem);
            for (var i = 0; i < Commands.Length; i++)
            {
                var c = Commands[i];
                group.AddCommandItem2(c.Text, -1, c.Tip, c.Tip, i, c.Callback, "", ids[i], where);
            }
            group.HasToolbar = true;
            group.HasMenu = true;
            group.Activate();
        }

        private static bool IdsMatch(int[] a, int[] b)
        {
            if (a == null || a.Length != b.Length) return false;
            for (var i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        public void OnUpload() => RunMacro("EasyPDMUpload.swp", "EasyPDMUpload", "main");
        public void OnDownload() => RunMacro("EasyPDMDownload.swp", "EasyPDMDownload", "main");
        public void OnLogIn() => RunMacro("EasyPDMUpload.swp", "EasyPDMUpload", "LoginInBrowser");
        public void OnLogOut() => RunMacro("EasyPDMUpload.swp", "EasyPDMUpload", "Logout");

        private void RunMacro(string file, string module, string procedure)
        {
            var path = Path.Combine(Path.GetDirectoryName(typeof(EasyPdmAddin).Assembly.Location) ?? "", file);
            if (!File.Exists(path))
            {
                _sw.SendMsgToUser2(
                    "EasyPDM: " + file + " is missing next to the add-in (" + path + "). " +
                    "Put the compiled EasyPDM macros from the release there.",
                    (int)swMessageBoxIcon_e.swMbStop, (int)swMessageBoxBtn_e.swMbOk);
                return;
            }
            int error;
            var ok = _sw.RunMacro2(path, module, procedure, (int)swRunMacroOption_e.swRunMacroUnloadAfterRun, out error);
            if (!ok)
                _sw.SendMsgToUser2("EasyPDM: the macro " + file + " (" + procedure + ") did not run, error " + error + ".",
                    (int)swMessageBoxIcon_e.swMbStop, (int)swMessageBoxBtn_e.swMbOk);
        }

        [ComRegisterFunction]
        public static void Register(Type t)
        {
            using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\SolidWorks\Addins\{" + t.GUID + "}"))
            {
                key.SetValue(null, 1);
                key.SetValue("Title", Title);
                key.SetValue("Description", "Upload and download documents to and from an EasyPDM server");
            }
            // Load at start-up for the user who installs it; others tick it in Tools > Add-Ins.
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\SolidWorks\AddInsStartup\{" + t.GUID + "}"))
                key.SetValue(null, 1);
        }

        [ComUnregisterFunction]
        public static void Unregister(Type t)
        {
            Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\SolidWorks\Addins\{" + t.GUID + "}", false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\SolidWorks\AddInsStartup\{" + t.GUID + "}", false);
        }
    }

    // Toolbar icons drawn at start-up into %LOCALAPPDATA%\EasyPDM\SolidWorks: one horizontal strip
    // per size SOLIDWORKS asks for (20 to 128 px), a coloured square with a letter per command.
    internal static class Icons
    {
        private static readonly int[] Sizes = { 20, 32, 40, 64, 96, 128 };

        public static (string[] Strips, string[] Main) Write()
        {
            var dir = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "EasyPDM", "SolidWorks");
            Directory.CreateDirectory(dir);
            var strips = new string[Sizes.Length];
            var main = new string[Sizes.Length];
            for (var s = 0; s < Sizes.Length; s++)
            {
                strips[s] = Draw(dir, Sizes[s], all: true);
                main[s] = Draw(dir, Sizes[s], all: false);
            }
            return (strips, main);
        }

        private static string Draw(string dir, int size, bool all)
        {
            var path = Path.Combine(dir, (all ? "commands_" : "main_") + size + ".png");
            var letters = all ? new[] { "U", "D", "I", "O" } : new[] { "E" };
            var colours = all
                ? new[] { Color.FromArgb(29, 78, 216), Color.FromArgb(21, 128, 61), Color.FromArgb(71, 85, 105), Color.FromArgb(185, 28, 28) }
                : new[] { Color.FromArgb(29, 78, 216) };
            using (var bmp = new Bitmap(size * letters.Length, size, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp))
            using (var font = new Font("Segoe UI", size * 0.5f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var white = new SolidBrush(Color.White))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);
                var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                for (var i = 0; i < letters.Length; i++)
                {
                    var pad = Math.Max(1, size / 10);
                    var rect = new Rectangle(i * size + pad, pad, size - 2 * pad, size - 2 * pad);
                    using (var fill = new SolidBrush(colours[i])) g.FillRectangle(fill, rect);
                    g.DrawString(letters[i], font, white, rect, format);
                }
                bmp.Save(path, ImageFormat.Png);
            }
            return path;
        }
    }
}

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("WinRAR 智能解压卸载程序")]
[assembly: AssemblyDescription("完全删除 WinRAR 智能解压菜单及安装文件")]
[assembly: AssemblyCompany("OpenAI Codex")]
[assembly: AssemblyProduct("WinRAR 智能解压")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

internal static class UninstallerProgram
{
    private const string Title = "WinRAR 智能解压 - 卸载";
    private const string AssociationRoot = @"Software\Classes\SystemFileAssociations";
    private const string MenuKeyName = "WinRARSmartExtract";
    private const string LegacyMenuKeyPath = @"Software\Classes\SystemFileAssociations\compressed\shell\WinRARSmartExtract";

    private static readonly string[] ArchiveExtensions =
    {
        ".zip", ".rar", "7z", ".tar", ".gz", ".tgz",
        ".jar", ".apk", ".epub", ".cbz",
        ".bz2", ".xz", ".zst", ".lz", ".lzma"
    };

    [STAThread]
    private static int Main(string[] args)
    {
        bool silent = HasArgument(args, "--silent");
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            string installDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinRARSmartExtract");

            RemoveInstalledFiles(installDirectory);
            RemoveRegisteredMenus();
            NotifyShell();

            if (!silent)
            {
                MessageBox.Show(
                    "卸载完成。\r\n\r\n右键菜单、助手程序及本工具创建的所有安装痕迹已清除。",
                    Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return 0;
        }
        catch (Exception ex)
        {
            if (!silent)
                MessageBox.Show("卸载未能完成：\r\n" + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static void RemoveRegisteredMenus()
    {
        foreach (string extension in ArchiveExtensions)
        {
            string menuKeyPath = AssociationRoot + "\\" + extension + "\\shell\\" + MenuKeyName;
            Registry.CurrentUser.DeleteSubKeyTree(menuKeyPath, false);
        }

        // Also clean installations made by releases that registered only the
        // generic "compressed" perceived type.
        Registry.CurrentUser.DeleteSubKeyTree(LegacyMenuKeyPath, false);
    }

    private static void RemoveInstalledFiles(string installDirectory)
    {
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinRARSmartExtract");

        if (!String.Equals(
            Path.GetFullPath(installDirectory).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(expected).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("卸载目录校验失败，已停止删除。");

        if (!Directory.Exists(installDirectory)) return;

        foreach (string file in Directory.GetFiles(installDirectory))
            File.Delete(file);
        foreach (string directory in Directory.GetDirectories(installDirectory))
            Directory.Delete(directory, true);
        Directory.Delete(installDirectory, false);
    }

    private static bool HasArgument(string[] args, string expected)
    {
        if (args == null) return false;
        foreach (string arg in args)
            if (String.Equals(arg, expected, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

    private static void NotifyShell()
    {
        SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
    }
}

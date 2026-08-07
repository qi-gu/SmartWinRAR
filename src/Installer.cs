using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("WinRAR 智能解压安装程序")]
[assembly: AssemblyDescription("当前用户级 WinRAR 智能解压菜单安装程序")]
[assembly: AssemblyCompany("OpenAI Codex")]
[assembly: AssemblyProduct("WinRAR 智能解压")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

internal static class InstallerProgram
{
    private const string Title = "WinRAR 智能解压 - 安装";
    private const string ResourceName = "WinRARSmartExtract.Payload";
    private const string AssociationRoot = @"Software\Classes\SystemFileAssociations";
    private const string MenuKeyName = "WinRARSmartExtract";
    private const string LegacyMenuKeyPath = @"Software\Classes\SystemFileAssociations\compressed\shell\WinRARSmartExtract";

    // Explorer only uses the last suffix when matching a compound extension, so
    // .tar.gz is covered by .gz. Registering each suffix also makes the menu
    // independent of the file association's optional PerceivedType value.
    private static readonly string[] ArchiveExtensions =
    {
        ".zip", ".rar", ".tar", ".gz", ".tgz",
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
            string winRarPath = FindWinRar();
            if (winRarPath == null)
                throw new InvalidOperationException("未找到 WinRAR。请先安装 WinRAR，再运行本安装程序。");

            string installDirectory = GetInstallDirectory();
            string helperPath = Path.Combine(installDirectory, "WinRARSmartExtract.exe");
            InstallPayload(helperPath);
            RegisterMenu(helperPath, winRarPath);
            NotifyShell();

            if (!silent)
            {
                MessageBox.Show(
                    "安装成功。\r\n\r\n" +
                    "在压缩包上右键，选择“智能解压”即可。\r\n" +
                    "Windows 11 中该项目位于“显示更多选项”菜单内。\r\n\r\n" +
                    "本安装仅影响当前用户，不需要管理员权限。",
                    Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return 0;
        }
        catch (Exception ex)
        {
            if (!silent)
                MessageBox.Show("安装失败：\r\n" + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static void InstallPayload(string helperPath)
    {
        string directory = Path.GetDirectoryName(helperPath);
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, "WinRARSmartExtract.new-" + Guid.NewGuid().ToString("N") + ".exe");

        try
        {
            using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
            {
                if (resource == null) throw new InvalidDataException("安装包内的助手程序丢失。");
                using (FileStream output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    resource.CopyTo(output);
            }

            if (File.Exists(helperPath)) File.Delete(helperPath);
            File.Move(temporary, helperPath);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch { }
        }
    }

    private static void RegisterMenu(string helperPath, string winRarPath)
    {
        foreach (string extension in ArchiveExtensions)
            RegisterMenuForExtension(extension, helperPath, winRarPath);

        // Remove the key used by older releases. Keeping it would create a
        // duplicate item for extensions whose PerceivedType is "compressed".
        Registry.CurrentUser.DeleteSubKeyTree(LegacyMenuKeyPath, false);
    }

    private static void RegisterMenuForExtension(string extension, string helperPath, string winRarPath)
    {
        string menuKeyPath = AssociationRoot + "\\" + extension + "\\shell\\" + MenuKeyName;
        using (RegistryKey menu = Registry.CurrentUser.CreateSubKey(menuKeyPath))
        {
            if (menu == null) throw new InvalidOperationException("无法写入当前用户的右键菜单。");
            menu.SetValue(null, "智能解压", RegistryValueKind.String);
            menu.SetValue("Icon", "\"" + winRarPath + "\",0", RegistryValueKind.String);
            menu.SetValue("MultiSelectModel", "Single", RegistryValueKind.String);
            using (RegistryKey command = menu.CreateSubKey("command"))
            {
                if (command == null) throw new InvalidOperationException("无法写入右键菜单命令。");
                command.SetValue(null, "\"" + helperPath + "\" \"%1\"", RegistryValueKind.String);
            }
        }
    }

    private static string GetInstallDirectory()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinRARSmartExtract");
    }

    private static string FindWinRar()
    {
        string path = ReadAppPath(Registry.CurrentUser);
        if (IsFile(path)) return path;
        path = ReadAppPath(Registry.LocalMachine);
        if (IsFile(path)) return path;

        string[] roots =
        {
            Environment.GetEnvironmentVariable("ProgramW6432"),
            Environment.GetEnvironmentVariable("ProgramFiles"),
            Environment.GetEnvironmentVariable("ProgramFiles(x86)")
        };
        foreach (string root in roots)
        {
            if (String.IsNullOrEmpty(root)) continue;
            path = Path.Combine(root, "WinRAR", "WinRAR.exe");
            if (File.Exists(path)) return path;
        }

        path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "WinRAR", "WinRAR.exe");
        return File.Exists(path) ? path : null;
    }

    private static string ReadAppPath(RegistryKey hive)
    {
        try
        {
            using (RegistryKey key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\WinRAR.exe"))
                return key == null ? null : key.GetValue(null) as string;
        }
        catch { return null; }
    }

    private static bool IsFile(string path)
    {
        return !String.IsNullOrWhiteSpace(path) && File.Exists(path);
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

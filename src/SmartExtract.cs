using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("WinRAR 智能解压")]
[assembly: System.Reflection.AssemblyDescription("根据压缩包根目录内容自动选择解压目录")]
[assembly: System.Reflection.AssemblyCompany("OpenAI Codex")]
[assembly: System.Reflection.AssemblyProduct("WinRAR 智能解压")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]

internal static class SmartExtractProgram
{
    private const string Title = "WinRAR 智能解压";

    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            if (args == null || args.Length != 1 || String.IsNullOrWhiteSpace(args[0]))
            {
                ShowError("请在压缩包上右键选择“智能解压”。");
                return 64;
            }

            string archivePath = Path.GetFullPath(args[0]);
            if (!File.Exists(archivePath))
            {
                ShowError("压缩包不存在：\r\n" + archivePath);
                return 2;
            }

            string winRarPath = FindWinRar();
            if (winRarPath == null)
            {
                ShowError("未找到 WinRAR。请先安装 WinRAR，或修复 WinRAR 安装。");
                return 3;
            }

            InspectionResult inspection = InspectArchive(archivePath, winRarPath);
            if (!inspection.Success)
            {
                ShowError(
                    "无法读取压缩包目录，未执行解压。\r\n\r\n" +
                    "压缩包可能已加密、已损坏，或格式不受当前系统的目录探测器支持。\r\n" +
                    "可改用 WinRAR 原有解压菜单处理。" +
                    FormatDetails(inspection.Error));
                return 4;
            }

            if (inspection.RootCount == 0)
            {
                MessageBox.Show("压缩包为空，无需解压。", Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }

            string parent = Path.GetDirectoryName(archivePath);
            if (String.IsNullOrEmpty(parent))
                parent = Environment.CurrentDirectory;

            string destination = inspection.RootCount == 1
                ? parent
                : Path.Combine(parent, GetArchiveBaseName(archivePath));
            destination = EnsureTrailingSeparator(destination);

            return ExtractWithWinRar(winRarPath, archivePath, destination);
        }
        catch (Exception ex)
        {
            ShowError("智能解压未能完成：\r\n" + ex.Message);
            return 1;
        }
    }

    private static InspectionResult InspectArchive(string archivePath, string winRarPath)
    {
        InspectionResult result;
        string tarPath = FindWindowsTar();
        if (tarPath != null && TryInspectWithWindowsTar(tarPath, archivePath, out result))
            return result;

        string extension = Path.GetExtension(archivePath).ToLowerInvariant();

        if ((extension == ".rar" || IsRarVolumeExtension(extension)) &&
            TryInspectRarFallback(winRarPath, archivePath, out result))
            return result;

        if (IsZipFamily(extension) && TryInspectZipFallback(archivePath, out result))
            return result;

        if (IsTarFamily(archivePath) && TryInspectTarFallback(archivePath, out result))
            return result;

        if (IsSingleStreamCompression(extension))
            return InspectionResult.Ok(1);

        return InspectionResult.Fail("目录探测命令返回错误。");
    }

    private static bool TryInspectWithWindowsTar(string tarPath, string archivePath, out InspectionResult result)
    {
        HashSet<string> roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        StringBuilder errors = new StringBuilder();
        object sync = new object();

        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = tarPath;
            startInfo.Arguments = "-tf " + Quote(archivePath);
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;

            using (Process process = new Process())
            {
                process.StartInfo = startInfo;
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data == null) return;
                    lock (sync) AddRoot(roots, e.Data);
                };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data == null) return;
                    lock (sync)
                    {
                        if (errors.Length < 1000) errors.AppendLine(e.Data);
                    }
                };

                if (!process.Start())
                {
                    result = InspectionResult.Fail("无法启动 tar.exe。");
                    return false;
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    result = InspectionResult.Fail(errors.ToString().Trim());
                    return false;
                }
            }

            lock (sync) result = InspectionResult.Ok(Math.Min(roots.Count, 2));
            return true;
        }
        catch (Exception ex)
        {
            result = InspectionResult.Fail(ex.Message);
            return false;
        }
    }

    private static bool TryInspectRarFallback(string winRarPath, string archivePath, out InspectionResult result)
    {
        string rarPath = Path.Combine(Path.GetDirectoryName(winRarPath), "Rar.exe");
        if (!File.Exists(rarPath))
        {
            result = InspectionResult.Fail("Rar.exe 不存在。");
            return false;
        }

        string logPath = Path.Combine(Path.GetTempPath(), "WinRARSmartExtract-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = rarPath;
            startInfo.Arguments = "lb -cfg- -c- -p- " + Quote("-logfu=" + logPath) + " " + Quote(archivePath);
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;

            int exitCode;
            using (Process process = Process.Start(startInfo))
            {
                process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                exitCode = process.ExitCode;
                if (exitCode != 0)
                {
                    result = InspectionResult.Fail(error.Trim());
                    return false;
                }
            }

            if (!File.Exists(logPath))
            {
                result = InspectionResult.Fail("Rar.exe 未生成目录清单。");
                return false;
            }

            HashSet<string> roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(logPath, Encoding.Unicode))
                AddRoot(roots, line);

            result = InspectionResult.Ok(Math.Min(roots.Count, 2));
            return true;
        }
        catch (Exception ex)
        {
            result = InspectionResult.Fail(ex.Message);
            return false;
        }
        finally
        {
            try { if (File.Exists(logPath)) File.Delete(logPath); }
            catch { }
        }
    }

    private static bool TryInspectZipFallback(string archivePath, out InspectionResult result)
    {
        try
        {
            HashSet<string> roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (ZipArchive archive = ZipFile.OpenRead(archivePath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    AddRoot(roots, entry.FullName);
                    if (roots.Count > 1) break;
                }
            }
            result = InspectionResult.Ok(Math.Min(roots.Count, 2));
            return true;
        }
        catch (Exception ex)
        {
            result = InspectionResult.Fail(ex.Message);
            return false;
        }
    }

    private static bool TryInspectTarFallback(string archivePath, out InspectionResult result)
    {
        try
        {
            using (FileStream file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Stream stream = file;
                if (IsGZipTar(archivePath))
                    stream = new GZipStream(file, CompressionMode.Decompress, false);

                using (stream == file ? null : stream)
                {
                    result = InspectTarStream(stream);
                    return result.Success;
                }
            }
        }
        catch (Exception ex)
        {
            result = InspectionResult.Fail(ex.Message);
            return false;
        }
    }

    private static InspectionResult InspectTarStream(Stream stream)
    {
        byte[] header = new byte[512];
        HashSet<string> roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string pendingLongName = null;
        string pendingPaxPath = null;
        bool sawHeader = false;

        while (true)
        {
            int headerBytes = ReadBlock(stream, header, 0, header.Length);
            if (headerBytes == 0) break;
            if (headerBytes != header.Length) throw new InvalidDataException("TAR 文件头不完整。");
            if (IsZeroBlock(header)) break;
            sawHeader = true;

            long size = ParseTarNumber(header, 124, 12);
            char type = (char)header[156];
            string name = ReadTarName(header);

            if (type == 'L')
            {
                pendingLongName = ReadTarText(stream, size).TrimEnd('\0', '\r', '\n');
                SkipTarPadding(stream, size);
                continue;
            }

            if (type == 'x')
            {
                string pax = ReadTarText(stream, size);
                pendingPaxPath = ReadPaxPath(pax);
                SkipTarPadding(stream, size);
                continue;
            }

            if (!String.IsNullOrEmpty(pendingPaxPath)) name = pendingPaxPath;
            else if (!String.IsNullOrEmpty(pendingLongName)) name = pendingLongName;
            pendingPaxPath = null;
            pendingLongName = null;

            if (type != 'g' && type != 'K')
            {
                AddRoot(roots, name);
                if (roots.Count > 1) return InspectionResult.Ok(2);
            }

            SkipExactly(stream, size);
            SkipTarPadding(stream, size);
        }

        if (!sawHeader) throw new InvalidDataException("不是有效的 TAR 压缩包。");
        return InspectionResult.Ok(Math.Min(roots.Count, 2));
    }

    private static int ExtractWithWinRar(string winRarPath, string archivePath, string destination)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo();
        startInfo.FileName = winRarPath;
        startInfo.Arguments = "x -cfg- " + Quote(archivePath) + " " + Quote(destination);
        startInfo.UseShellExecute = false;
        startInfo.WorkingDirectory = Path.GetDirectoryName(archivePath);

        using (Process process = Process.Start(startInfo))
        {
            process.WaitForExit();
            int exitCode = process.ExitCode;
            if (exitCode != 0 && exitCode != 255)
            {
                ShowError("WinRAR 解压未能完成（错误码 " + exitCode + "）。");
            }
            return exitCode;
        }
    }

    private static void AddRoot(HashSet<string> roots, string entryName)
    {
        if (roots.Count > 1 || String.IsNullOrEmpty(entryName)) return;

        string name = entryName.Replace('\\', '/');
        while (name.StartsWith("./", StringComparison.Ordinal)) name = name.Substring(2);
        name = name.TrimStart('/');
        if (name.Length == 0 || name == ".") return;

        int slash = name.IndexOf('/');
        string root = slash < 0 ? name : name.Substring(0, slash);
        if (root.Length > 0 && root != ".") roots.Add(root);
    }

    private static string GetArchiveBaseName(string archivePath)
    {
        string fileName = Path.GetFileName(archivePath);
        string[] compoundExtensions =
        {
            ".tar.gz", ".tar.bz2", ".tar.xz", ".tar.zst", ".tar.lz", ".tar.lzma", ".tar.br"
        };

        foreach (string extension in compoundExtensions)
        {
            if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                string compoundBase = fileName.Substring(0, fileName.Length - extension.Length);
                return compoundBase.Length == 0 ? "archive" : compoundBase;
            }
        }

        string simpleBase = Path.GetFileNameWithoutExtension(fileName);
        return String.IsNullOrEmpty(simpleBase) ? "archive" : simpleBase;
    }

    private static string FindWinRar()
    {
        string path = ReadAppPath(Registry.CurrentUser);
        if (IsFile(path)) return path;
        path = ReadAppPath(Registry.LocalMachine);
        if (IsFile(path)) return path;

        string[] candidates =
        {
            CombineEnvironmentFolder("ProgramW6432", "WinRAR", "WinRAR.exe"),
            CombineEnvironmentFolder("ProgramFiles", "WinRAR", "WinRAR.exe"),
            CombineEnvironmentFolder("ProgramFiles(x86)", "WinRAR", "WinRAR.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "WinRAR", "WinRAR.exe")
        };
        foreach (string candidate in candidates)
            if (IsFile(candidate)) return candidate;
        return null;
    }

    private static string ReadAppPath(RegistryKey hive)
    {
        try
        {
            using (RegistryKey key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\WinRAR.exe"))
            {
                return key == null ? null : key.GetValue(null) as string;
            }
        }
        catch { return null; }
    }

    private static string FindWindowsTar()
    {
        string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string candidate = Path.Combine(system, "tar.exe");
        if (File.Exists(candidate)) return candidate;

        string windows = Environment.GetEnvironmentVariable("SystemRoot");
        if (!String.IsNullOrEmpty(windows))
        {
            candidate = Path.Combine(windows, "System32", "tar.exe");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string CombineEnvironmentFolder(string variable, params string[] parts)
    {
        string root = Environment.GetEnvironmentVariable(variable);
        if (String.IsNullOrEmpty(root)) return null;
        string result = root;
        foreach (string part in parts) result = Path.Combine(result, part);
        return result;
    }

    private static bool IsFile(string path)
    {
        return !String.IsNullOrWhiteSpace(path) && File.Exists(path);
    }

    private static bool IsZipFamily(string extension)
    {
        return extension == ".zip" || extension == ".jar" || extension == ".apk" ||
               extension == ".epub" || extension == ".cbz";
    }

    private static bool IsTarFamily(string path)
    {
        string lower = path.ToLowerInvariant();
        return lower.EndsWith(".tar") || lower.EndsWith(".tar.gz") || lower.EndsWith(".tgz");
    }

    private static bool IsGZipTar(string path)
    {
        string lower = path.ToLowerInvariant();
        return lower.EndsWith(".tar.gz") || lower.EndsWith(".tgz");
    }

    private static bool IsSingleStreamCompression(string extension)
    {
        return extension == ".gz" || extension == ".bz2" || extension == ".xz" ||
               extension == ".zst" || extension == ".lz" || extension == ".lzma";
    }

    private static bool IsRarVolumeExtension(string extension)
    {
        return extension.Length == 4 && extension[1] == 'r' && Char.IsDigit(extension[2]) && Char.IsDigit(extension[3]);
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
            path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)) return path;
        return path + Path.DirectorySeparatorChar;
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static string FormatDetails(string details)
    {
        if (String.IsNullOrWhiteSpace(details)) return String.Empty;
        details = details.Trim();
        if (details.Length > 500) details = details.Substring(0, 500) + "…";
        return "\r\n\r\n详细信息：" + details;
    }

    private static void ShowError(string message)
    {
        MessageBox.Show(message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private static int ReadBlock(Stream stream, byte[] buffer, int offset, int count)
    {
        int total = 0;
        while (total < count)
        {
            int read = stream.Read(buffer, offset + total, count - total);
            if (read == 0) break;
            total += read;
        }
        return total;
    }

    private static bool IsZeroBlock(byte[] block)
    {
        for (int i = 0; i < block.Length; i++) if (block[i] != 0) return false;
        return true;
    }

    private static string ReadTarName(byte[] header)
    {
        string name = DecodeTarString(header, 0, 100);
        string prefix = DecodeTarString(header, 345, 155);
        return String.IsNullOrEmpty(prefix) ? name : prefix + "/" + name;
    }

    private static string DecodeTarString(byte[] bytes, int offset, int length)
    {
        int count = 0;
        while (count < length && bytes[offset + count] != 0) count++;
        if (count == 0) return String.Empty;
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes, offset, count);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Default.GetString(bytes, offset, count);
        }
    }

    private static long ParseTarNumber(byte[] header, int offset, int length)
    {
        if ((header[offset] & 0x80) != 0)
        {
            long value = header[offset] & 0x7F;
            for (int i = 1; i < length; i++) value = checked((value << 8) | header[offset + i]);
            return value;
        }

        long result = 0;
        int end = offset + length;
        int index = offset;
        while (index < end && (header[index] == 0 || header[index] == (byte)' ')) index++;
        while (index < end && header[index] >= (byte)'0' && header[index] <= (byte)'7')
        {
            result = checked((result << 3) + (header[index] - (byte)'0'));
            index++;
        }
        return result;
    }

    private static string ReadTarText(Stream stream, long size)
    {
        if (size < 0 || size > Int32.MaxValue) throw new InvalidDataException("TAR 扩展头过大。");
        byte[] data = new byte[(int)size];
        if (ReadBlock(stream, data, 0, data.Length) != data.Length)
            throw new EndOfStreamException();
        try { return new UTF8Encoding(false, true).GetString(data); }
        catch (DecoderFallbackException) { return Encoding.Default.GetString(data); }
    }

    private static string ReadPaxPath(string pax)
    {
        int position = 0;
        string path = null;
        while (position < pax.Length)
        {
            int space = pax.IndexOf(' ', position);
            if (space < 0) break;
            int recordLength;
            if (!Int32.TryParse(pax.Substring(position, space - position), out recordLength) || recordLength <= 0) break;
            int end = Math.Min(pax.Length, position + recordLength);
            string record = pax.Substring(space + 1, end - space - 1).TrimEnd('\n');
            int equals = record.IndexOf('=');
            if (equals > 0 && record.Substring(0, equals) == "path") path = record.Substring(equals + 1);
            position += recordLength;
        }
        return path;
    }

    private static void SkipTarPadding(Stream stream, long size)
    {
        long padding = (512 - (size % 512)) % 512;
        SkipExactly(stream, padding);
    }

    private static void SkipExactly(Stream stream, long count)
    {
        byte[] buffer = new byte[81920];
        while (count > 0)
        {
            int wanted = (int)Math.Min(buffer.Length, count);
            int read = stream.Read(buffer, 0, wanted);
            if (read == 0) throw new EndOfStreamException();
            count -= read;
        }
    }

    private sealed class InspectionResult
    {
        public bool Success { get; private set; }
        public int RootCount { get; private set; }
        public string Error { get; private set; }

        public static InspectionResult Ok(int rootCount)
        {
            return new InspectionResult { Success = true, RootCount = rootCount, Error = null };
        }

        public static InspectionResult Fail(string error)
        {
            return new InspectionResult { Success = false, RootCount = 0, Error = error };
        }
    }
}

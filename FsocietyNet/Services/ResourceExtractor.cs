using System.IO;
using System.Reflection;

namespace FsocietyNet.Services;

/// <summary>
/// Извлекает бинарники (amneziawg.exe, wintun.dll) из embedded resources
/// в %AppData%\FsocietyNet\ при первом запуске или при обновлении приложения.
/// </summary>
public static class ResourceExtractor
{
    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FsocietyNet");

    public static string AmneziawgPath => Path.Combine(AppDataDir, "amneziawg.exe");
    public static string WintunPath    => Path.Combine(AppDataDir, "wintun.dll");

    /// <summary>
    /// Вызывать один раз при старте приложения.
    /// Извлекает файлы если их нет или если размер изменился (новая версия).
    /// </summary>
    public static void EnsureExtracted()
    {
        Directory.CreateDirectory(AppDataDir);

        ExtractIfNeeded("FsocietyNet.Resources.amneziawg.exe", AmneziawgPath);
        ExtractIfNeeded("FsocietyNet.Resources.wintun.dll",    WintunPath);
    }

    private static void ExtractIfNeeded(string resourceName, string targetPath)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(resourceName);

        // Ресурс не найден — значит dev-сборка без вшитых бинарников, пропускаем
        if (stream == null) return;

        // Перезаписываем если файла нет или размер отличается (обновление приложения)
        if (!File.Exists(targetPath) || new FileInfo(targetPath).Length != stream.Length)
        {
            using var file = File.Create(targetPath);
            stream.CopyTo(file);
        }
    }
}

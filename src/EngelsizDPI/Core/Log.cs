namespace EngelsizDPI.Core;

/// <summary>
/// Sorun giderme günlüğü: C:\ProgramData\EngelsizDPI\logs\engelsizdpi.log. Kullanıcılar hata bildirirken bu dosyayı ekler.
/// </summary>
public static class Log
{
    private const long MaxSize = 1024 * 1024;
    private static readonly Lock Sync = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EngelsizDPI", "logs", "engelsizdpi.log");

    public static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxSize)
                    File.Move(FilePath, FilePath + ".1", overwrite: true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // Günlük yazılamaması uygulamayı etkilememeli.
        }
    }
}

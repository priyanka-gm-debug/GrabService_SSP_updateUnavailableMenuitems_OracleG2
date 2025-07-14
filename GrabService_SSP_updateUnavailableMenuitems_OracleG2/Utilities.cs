using System;
using System.IO;

public static class Utilities
{
    private static readonly string logFilePath = @"C:\Temp\STAGE_SSP_Unavailable_MenuItems.txt";

    public static void WriteLog(string message)
    {
        try
        {
            if (!Directory.Exists(Path.GetDirectoryName(logFilePath)))
                Directory.CreateDirectory(Path.GetDirectoryName(logFilePath));

            using (StreamWriter sw = new StreamWriter(logFilePath, true))
            {
                sw.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {message}");
            }
        }
        catch (Exception ex)
        {
            // Fails silently or log elsewhere
            Console.WriteLine($"Log write failed: {ex.Message}");
        }
    }
}

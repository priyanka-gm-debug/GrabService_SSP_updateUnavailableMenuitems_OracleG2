using System;
using System.IO;

namespace OracleG2MenuitemsUpdate
{
    public static class Utilities
    {
        public static void WriteLog(string message)
        {
            try
            {
                string path = @"C:\Temp\POS_SSP_Unavailable_MenuItems.txt";
                File.AppendAllText(path, DateTime.Now + ": " + message + Environment.NewLine);
            }
            catch { /* Ignore logging failures */ }
        }
    }
}

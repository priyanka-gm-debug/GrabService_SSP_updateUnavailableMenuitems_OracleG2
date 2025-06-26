using System;
using System.Data;
using System.Data.SqlClient;
using System.Configuration;
using GrabService_SSP_updateUnavailableMenuitems_OracleG2;

namespace OracleG2MenuitemsUpdate
{
    class Program
    {
        static void Main(string[] args)
        {
            string task = args.Length > 0 ? args[0].ToLower() : "At every 20 min";

            Utilities.WriteLog($"Starting {task} task...");
            try
            {
                DataAccess dataAccess = new DataAccess();
                OracleG2MenuitemsUpdate MenuitemsUpdate = new OracleG2MenuitemsUpdate();
                MenuitemsUpdate.update_menuItems_SSP();
                Utilities.WriteLog($"{task} task completed.");
            }
            catch (Exception ex)
            { Utilities.WriteLog($"{task} task completed." + ex.Message.ToString()); }
            //Console.WriteLine($"{task} task completed.");

        }
    }
}
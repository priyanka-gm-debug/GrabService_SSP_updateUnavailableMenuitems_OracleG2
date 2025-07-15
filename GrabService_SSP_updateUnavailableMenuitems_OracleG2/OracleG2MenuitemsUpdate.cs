using System;
using System.Net;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using Newtonsoft.Json.Linq;
using System.Configuration;
using Newtonsoft.Json;
using System.Text;
using GrabService_SSP_updateUnavailableMenuitems_OracleG2;
using System.Linq;
using System.Diagnostics;
using System.Threading;

namespace OracleG2MenuitemsUpdate
{
    public class OracleG2MenuitemsUpdate
    {
        public void update_menuItems_SSP()
        {
            Stopwatch totalWatch = Stopwatch.StartNew();
            DataAccess dalog = new DataAccess();
            DataTable dtBatchEnable = CreateEnableBatchDataTable();
            DataTable dtBatchDisable = CreateDisableBatchDataTable();

            // Fetch config
            var config = dalog.ExecuteSelectQuery("SELECT TOP 1 BatchSize, DelayMS FROM tb_Cursus_MenuUpdate_Config WITH (NOLOCK)").Tables[0].Rows[0];
            int batchSize = Convert.ToInt32(config["BatchSize"]);
            int delay = Convert.ToInt32(config["DelayMS"]);

            try
            {
                Utilities.WriteLog("Connected to database.");

               string enableQuery = @"SELECT * FROM tb_Cursus_OracleG2_StoreLoadFilterJoin WITH (NOLOCK)
                                    WHERE EXISTS (
                                        SELECT 1 FROM tb_Cursus_InventoryUpdate_Log l WITH (NOLOCK)
                                        JOIN tb_Cursus_StoreInventoryMainV2 m WITH (NOLOCK) ON l.InventoryItemId = m.InventoryItemId
                                        WHERE l.NewValue = 0 AND m.InventoryItemavailable = 0 AND l.Storewaypointid = Grabstorewaypointid)";


                var dsEnable = dalog.ExecuteSelectQuery(enableQuery);

                if (dsEnable.Tables.Count > 0)
                {
                    foreach (DataRow drCred in dsEnable.Tables[0].Rows)
                    {
                        ProcessStoreMenuItems(drCred, dalog, dtBatchEnable);
                    }

                    if (dtBatchEnable.Rows.Count > 0)
                    {
                        Utilities.WriteLog($"Enable batch size: {dtBatchEnable.Rows.Count}");

                        foreach (var batch in dtBatchEnable.AsEnumerable().Batch(batchSize))
                        {
                            Stopwatch spWatch = Stopwatch.StartNew();
                            DataTable batchTable = dtBatchEnable.Clone();
                            foreach (var row in batch) batchTable.ImportRow(row);

                            SqlParameter param = new SqlParameter("@MenuItemsToEnable", SqlDbType.Structured)
                            {
                                TypeName = "dbo.EnableLogTVPType",
                                Value = batchTable
                            };

                            var result = dalog.ExecuteSelectDataTable("sp_Batch_EnableMenuItems", cmd => cmd.Parameters.Add(param));
                            int updatedRows = Convert.ToInt32(result.Rows[0]["RowsUpdated"]);

                            Utilities.WriteLog($"Batch of {batchTable.Rows.Count} enabled ({updatedRows} actually updated) in {spWatch.ElapsedMilliseconds}ms");
                            Thread.Sleep(delay);
                        }
                    }
                    else
                    {
                        Utilities.WriteLog("✅ No menu items to enable.");
                    }
                }


                string disableQuery = @"SELECT GrabStoreWaypointID,orgShortName,organizationName,locRef,rvcRef,urlAPI,urlOAuth FROM tb_Cursus_OracleG2_StoreLoadFilterJoin WITH (NOLOCK)
                                    WHERE EXISTS (
                                        SELECT 1 FROM Fetch_Waypoints_InventoryUpdate w WITH (NOLOCK)
                                        WHERE GrabStoreWaypointID = w.WaypointId)";

                var dsDisable = dalog.ExecuteSelectQuery(disableQuery);

                if (dsDisable.Tables[0].Rows.Count > 0)
                {
                    foreach (DataRow drCred in dsDisable.Tables[0].Rows)
                    {
                        ProcessDisableMenuItems(drCred, dalog, dtBatchDisable);
                    }

                    if (dtBatchDisable.Rows.Count > 0)
                    {
                        Utilities.WriteLog($"Disable batch size: {dtBatchDisable.Rows.Count}");

                        foreach (var batch in dtBatchDisable.AsEnumerable().Batch(batchSize))
                        {
                            Stopwatch spWatch = Stopwatch.StartNew();
                            DataTable batchTable = dtBatchDisable.Clone();
                            foreach (var row in batch) batchTable.ImportRow(row);

                            SqlParameter param = new SqlParameter("@MenuItemsToDisable", SqlDbType.Structured)
                            {
                                TypeName = "dbo.DisableLogTVPType",
                                Value = batchTable
                            };

                            var result = dalog.ExecuteSelectDataTable("sp_Batch_DisableMenuItems", cmd => cmd.Parameters.Add(param));
                            int updatedRows = Convert.ToInt32(result.Rows[0]["RowsUpdated"]);

                            Utilities.WriteLog($"Batch of {batchTable.Rows.Count} disabled ({updatedRows} actually updated) in {spWatch.ElapsedMilliseconds}ms");
                            Thread.Sleep(delay);
                        }
                    }
                    else
                    {
                        Utilities.WriteLog("✅ No menu items to disable.");
                    }
                }

                Utilities.WriteLog($"✅ Total menu update process completed in {totalWatch.Elapsed.TotalSeconds} seconds");
            }
            catch (Exception ex)
            {
                Utilities.WriteLog($"❌ Error in update_menuItems_SSP: {ex.Message}");
            }
        }

        private void ProcessStoreMenuItems(DataRow drCred, DataAccess dalog, DataTable dtBatch)
        {
            string storewaypointId = drCred["grabstorewaypointid"].ToString().Trim();
            string orgShortName = drCred["orgShortName"].ToString().Trim();
            string locRef = drCred["locRef"].ToString().Trim();
            string rvcRef = drCred["rvcRef"].ToString().Trim();
            string urlAPI = drCred["urlAPI"].ToString().Trim();

            string id_token = new OracleG2().LoginOracleG2_TokenCache(drCred, null);
            if (string.IsNullOrEmpty(id_token)) return;

            using (WebClient webClient = new WebClient())
            {
                webClient.Headers["Content-Type"] = "application/json";
                webClient.Headers[HttpRequestHeader.Authorization] = "Bearer " + id_token;

                var sResponse = webClient.DownloadString($"{urlAPI}menus/items/unavailable?orgShortName={orgShortName}&locRef={locRef}&rvcRef={rvcRef}");
                var jsonObj = JObject.Parse(sResponse);

                var allIdSet = jsonObj["items"] != null ? new HashSet<string>(
                    jsonObj["items"].SelectMany(item => item["definitions"]
                        .Select(def => $"{item["menuItemId"]}:{def["definitionSequence"]}"))) : new HashSet<string>();


                var dsLog = dalog.ExecuteSelectQuery($@"
                    SELECT l.StorewaypointId, l.InventoryItemId, l.InventoryItemName, l.menuitemId,
                           LEFT(l.MenuItemId_DefSeq_PriceSeq, 11) AS MenuItemIdSeq
                    FROM tb_Cursus_InventoryUpdate_Log l WITH (NOLOCK)
                    JOIN tb_Cursus_StoreInventoryMainV2 m WITH (NOLOCK) ON l.InventoryItemId = m.InventoryItemId
                    WHERE l.NewValue = 0 AND m.InventoryItemavailable = 0 AND l.StorewaypointId = '{storewaypointId}'");


                foreach (DataRow row in dsLog.Tables[0].Rows)
                {
                    var defSeq = row["MenuItemIdSeq"].ToString();
                    if (!allIdSet.Contains(defSeq))
                    {
                        dtBatch.Rows.Add(
                            Convert.ToInt32(row["StorewaypointId"]),
                            Convert.ToInt32(row["InventoryItemId"]),
                            Convert.ToInt32(row["menuitemId"]),
                            defSeq,
                            row["InventoryItemName"].ToString()
                        );
                    }
                }
            }
        }

        private void ProcessDisableMenuItems(DataRow drCred, DataAccess dalog, DataTable dtBatch)
        {
            string storewaypointId = drCred["grabstorewaypointid"].ToString().Trim();
            string orgShortName = drCred["orgShortName"].ToString().Trim();
            string locRef = drCred["locRef"].ToString().Trim();
            string rvcRef = drCred["rvcRef"].ToString().Trim();
            string urlAPI = drCred["urlAPI"].ToString().Trim();

            string id_token = new OracleG2().LoginOracleG2_TokenCache(drCred, null);
            if (string.IsNullOrEmpty(id_token)) return;

            using (WebClient webClient = new WebClient())
            {
                webClient.Headers["Content-Type"] = "application/json";
                webClient.Headers[HttpRequestHeader.Authorization] = "Bearer " + id_token;

                var sResponse = webClient.DownloadString($"{urlAPI}menus/items/unavailable?orgShortName={orgShortName}&locRef={locRef}&rvcRef={rvcRef}");
                var jsonObj = JObject.Parse(sResponse);

                foreach (var item in jsonObj["items"] ?? new JArray())
                {
                    string menuId = item["menuItemId"].ToString();
                    foreach (var def in item["definitions"])
                    {
                        string defSeq = def["definitionSequence"].ToString();
                        dtBatch.Rows.Add(Convert.ToInt32(storewaypointId), $"{menuId}:{defSeq}");
                    }
                }
            }
        }

        private DataTable CreateEnableBatchDataTable()
        {
            DataTable dt = new DataTable();
            dt.Columns.Add("StorewaypointId", typeof(int));
            dt.Columns.Add("InventoryItemId", typeof(int));
            dt.Columns.Add("MenuItemId", typeof(int));
            dt.Columns.Add("MenuItemId_DefSeq_PriceSeq", typeof(string));
            dt.Columns.Add("InventoryItemName", typeof(string));
            return dt;
        }

        private DataTable CreateDisableBatchDataTable()
        {
            DataTable dt = new DataTable();
            dt.Columns.Add("StorewaypointId", typeof(int));
            dt.Columns.Add("MenuItemId_DefSeq", typeof(string));
            return dt;
        }
    }

    public static class EnumerableExtensions
    {
        public static IEnumerable<IEnumerable<T>> Batch<T>(this IEnumerable<T> source, int size)
        {
            T[] bucket = null;
            int count = 0;
            foreach (var item in source)
            {
                if (bucket == null) bucket = new T[size];
                bucket[count++] = item;
                if (count != size) continue;
                yield return bucket;
                bucket = null;
                count = 0;
            }
            if (bucket != null && count > 0)
                yield return bucket.Take(count);
        }
    }
}

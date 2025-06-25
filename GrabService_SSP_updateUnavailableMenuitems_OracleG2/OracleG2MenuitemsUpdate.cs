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

namespace OracleG2MenuitemsUpdate
{
    public class OracleG2MenuitemsUpdate
    {
        public void update_menuItems_SSP()
        {
            DataAccess dalog = new DataAccess();
            DataTable dtBatchEnable = CreateEnableBatchDataTable();
            DataTable dtBatchDisable = CreateDisableBatchDataTable();

            try
            {
                Utilities.WriteLog("Connected to database.");

                string enableQuery = @"SELECT * FROM tb_Cursus_OracleG2_StoreLoadFilterJoin 
                                WHERE Grabstorewaypointid IN (
                                    SELECT storewaypointid 
                                    FROM tb_Cursus_InventoryUpdate_Log 
                                    WHERE NewValue = 0 
                                      AND Inventoryitemid IN (
                                          SELECT inventoryitemid 
                                          FROM tb_Cursus_StoreInventoryMainV2 
                                          WHERE InventoryItemavailable = 0))";

                DataSet dsEnable = dalog.ExecuteSelectQuery(enableQuery);
                Utilities.WriteLog("Connected to SQL server for enabled items check.");

                if (dsEnable.Tables.Count > 0 && dsEnable.Tables[0].Rows.Count > 0)
                {
                    foreach (DataRow drCred in dsEnable.Tables[0].Rows)
                    {
                        ProcessStoreMenuItems(drCred, dalog, dtBatchEnable);
                    }

                    if (dtBatchEnable.Rows.Count > 0)
                    {
                        SqlParameter param = new SqlParameter("@MenuItemsToEnable", SqlDbType.Structured)
                        {
                            TypeName = "dbo.EnableLogTVPType",
                            Value = dtBatchEnable
                        };

                        int updated = dalog.ExecuteStoredProcedureforupdate("sp_Batch_EnableMenuItems", cmd =>
                        {
                            cmd.Parameters.Add(param);
                        });

                        Utilities.WriteLog($"Updated {updated} items as available via batch across stores.");
                    }
                }
                else
                {
                    Utilities.WriteLog("No items found to enable.");
                }

                string disableQuery = @"SELECT * FROM tb_Cursus_OracleG2_StoreLoadFilterJoin 
                                 WHERE GrabStoreWaypointID IN (
                                     SELECT WaypointId FROM Fetch_Waypoints_InventoryUpdate)";

                DataSet dsCredsDisable = dalog.ExecuteSelectQuery(disableQuery);

                if (dsCredsDisable.Tables.Count > 0 && dsCredsDisable.Tables[0].Rows.Count > 0)
                {
                    foreach (DataRow drCred in dsCredsDisable.Tables[0].Rows)
                    {
                        ProcessDisableMenuItems(drCred, dalog, dtBatchDisable);
                    }

                    if (dtBatchDisable.Rows.Count > 0)
                    {
                        SqlParameter param = new SqlParameter("@MenuItemsToDisable", SqlDbType.Structured)
                        {
                            TypeName = "dbo.DisableLogTVPType",
                            Value = dtBatchDisable
                        };

                        int disabled = dalog.ExecuteStoredProcedureforupdate("sp_Batch_DisableMenuItems", cmd =>
                        {
                            cmd.Parameters.Add(param);
                        });

                        Utilities.WriteLog($"Disabled {disabled} menu items across stores via batch.");
                    }
                }
                else
                {
                    Utilities.WriteLog("No specific stores found to disable items.");
                }
            }
            catch (Exception ex)
            {
                Utilities.WriteLog($"❌ An error occurred in update_menuItems_SSP: {ex.Message}");
            }
        }

        private void ProcessStoreMenuItems(DataRow drCred, DataAccess dalog, DataTable dtBatch)
        {
            string storewaypointId = drCred["grabstorewaypointid"].ToString().Trim();

            try
            {
                string orgShortName = drCred["orgShortName"].ToString().Trim();
                string locRef = drCred["locRef"].ToString().Trim();
                string rvcRef = drCred["rvcRef"].ToString().Trim();
                string urlAPI = drCred["urlAPI"].ToString().Trim();

                OracleG2 oOracleG2 = new OracleG2();
                string id_token = oOracleG2.LoginOracleG2_TokenCache(drCred, null);

                if (string.IsNullOrEmpty(id_token)) return;

                using (WebClient webClient = new WebClient())
                {
                    webClient.Headers["Content-Type"] = "application/json";
                    webClient.Headers[HttpRequestHeader.Authorization] = "Bearer " + id_token;

                    string sResponse = webClient.DownloadString(
                        $"{urlAPI}menus/items/unavailable?orgShortName={orgShortName}&locRef={locRef}&rvcRef={rvcRef}");

                    JObject jsonObj = JObject.Parse(sResponse);
                    HashSet<string> allIdSet = jsonObj["items"] != null ? new HashSet<string>(
                        jsonObj["items"].SelectMany(item =>
                            item["definitions"].Select(def => $"{item["menuItemId"]}:{def["definitionSequence"]}"))
                        ) : new HashSet<string>();

                    DataSet dsLog = dalog.ExecuteSelectQuery($@"
                        SELECT StorewaypointId, InventoryItemId, InventoryItemName, menuitemId,
                               LEFT(MenuItemId_DefSeq_PriceSeq, 11) AS MenuItemIdSeq
                        FROM tb_Cursus_InventoryUpdate_Log
                        WHERE NewValue = 0
                          AND Inventoryitemid IN (
                              SELECT InventoryItemId
                              FROM tb_Cursus_StoreInventoryMainV2
                              WHERE InventoryItemavailable = 0)
                          AND StorewaypointId = '{storewaypointId}'");

                    foreach (DataRow row in dsLog.Tables[0].Rows)
                    {
                        string defSeq = row["MenuItemIdSeq"].ToString();
                        if (!allIdSet.Contains(defSeq))
                        {
                            dtBatch.Rows.Add(
      Convert.ToInt32(row["StorewaypointId"]),
      Convert.ToInt32(row["InventoryItemId"]),
      Convert.ToInt32(row["menuitemId"]),
      defSeq,  // MenuItemId_DefSeq_PriceSeq
      row["InventoryItemName"].ToString());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Utilities.WriteLog($"❌ An error occurred in ProcessStoreMenuItems: {ex.Message}");
            }
        }

        private void ProcessDisableMenuItems(DataRow drCred, DataAccess dalog, DataTable dtBatchDisable)
        {
            string storewaypointId = drCred["grabstorewaypointid"].ToString().Trim();

            try
            {
                string orgShortName = drCred["orgShortName"].ToString().Trim();
                string locRef = drCred["locRef"].ToString().Trim();
                string rvcRef = drCred["rvcRef"].ToString().Trim();
                string urlAPI = drCred["urlAPI"].ToString().Trim();

                OracleG2 oOracleG2 = new OracleG2();
                string id_token = oOracleG2.LoginOracleG2_TokenCache(drCred, null);

                if (string.IsNullOrEmpty(id_token)) return;

                using (WebClient webClient = new WebClient())
                {
                    webClient.Headers["Content-Type"] = "application/json";
                    webClient.Headers[HttpRequestHeader.Authorization] = "Bearer " + id_token;

                    string sResponse = webClient.DownloadString(
                        $"{urlAPI}menus/items/unavailable?orgShortName={orgShortName}&locRef={locRef}&rvcRef={rvcRef}");

                    JObject jsonObj = JObject.Parse(sResponse);

                    if (jsonObj["items"] != null)
                    {
                        foreach (var item in jsonObj["items"])
                        {
                            var menuId = item["menuItemId"].ToString();
                            foreach (var def in item["definitions"])
                            {
                                var defSeq = def["definitionSequence"].ToString();
                                dtBatchDisable.Rows.Add(Convert.ToInt32(storewaypointId), $"{menuId}:{defSeq}");
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Log handled in caller
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
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;
using System.Security.Principal;
using System.Configuration;
using System.Net;
using System.Net.Http;
using System.Web;
using System.Security.Cryptography;
using System.Globalization;
using System.Diagnostics;

namespace GrabService_SSP_updateUnavailableMenuitems_OracleG2
{
    public class OracleG2
    {
        string sCodeVerifier = OracleG2.GenerateNonce();
        private void SaveTokenInformation(string grabStoreWaypointID, JObject jsonToken)
        {
            {
                string sServer = System.Environment.MachineName.ToUpper();

                DataAccess daSKY = new DataAccess();
                bool bDataAccessOnAU = true;
                try
                {
                    string sql = "Select count(*) from tb_AAA_AU_Datacenter where DataAccessOn = 1";
                    DataSet dsDataAccessAU = daSKY.ExecuteSelectQuery(sql);
                    if (Convert.ToInt16(dsDataAccessAU.Tables[0].Rows[0][0]) == 0)
                        bDataAccessOnAU = false;
                }
                catch { }

                DataSet dsTokenInformation = daSKY.ExecuteStoredProcedure("sp_Cursus_Get_OracleG2_TokenInformation", cmd =>
                {
                    cmd.Parameters.Add(new SqlParameter("@GrabStoreWaypointID", grabStoreWaypointID));
                });
                bool bWaypointHasValidToken = false;
                try
                {
                    JObject jsonTokenInformationDatabase = JObject.Parse(dsTokenInformation.Tables[0].Rows[0]["jsonTokenInformation"].ToString());
                    string id_token = jsonTokenInformationDatabase["id_token"].ToString();
                    bWaypointHasValidToken = true;
                }
                catch { }

                if (!bWaypointHasValidToken)
                {

                    try
                    {

                        bool bResult = daSKY.ExecuteStoredProcedurecheck("sp_Cursus_Save_OracleG2_TokenInformation", cmd =>
                        {
                            cmd.Parameters.Add(new SqlParameter("@GrabStoreWaypointID", grabStoreWaypointID.Trim()));
                            cmd.Parameters.Add(new SqlParameter("@jsonTokenInformation", jsonToken.ToString(Formatting.None)));
                        });

                    }
                    catch { }

                    try
                    {

                        if (bDataAccessOnAU)
                        {
                            bool bResult = daSKY.ExecuteStoredProcedurecheck("sp_Cursus_Save_OracleG2_TokenInformation", cmd =>
                            {
                                cmd.Parameters.Add(new SqlParameter("@GrabStoreWaypointID", grabStoreWaypointID.Trim()));
                                cmd.Parameters.Add(new SqlParameter("@jsonTokenInformation", jsonToken.ToString(Formatting.None)));
                            });
                        }
                    }
                    catch { }

                    try
                    {
                        daSKY.ExecuteStoredProcedure("sp_Cursus_Save_OracleG2_TokenInformation", cmd =>
                        {
                            cmd.Parameters.Add(new SqlParameter("@GrabStoreWaypointID", grabStoreWaypointID.Trim()));
                            cmd.Parameters.Add(new SqlParameter("@jsonTokenInformation", jsonToken.ToString(Formatting.None)));
                        });
                    }
                    catch { }

                    try
                    {
                        if (sServer == "SKYC0C7")
                        {
                            daSKY.ExecuteStoredProcedure("sp_Cursus_Save_OracleG2_TokenInformation", cmd =>
                            {
                                cmd.Parameters.Add(new SqlParameter("@GrabStoreWaypointID", grabStoreWaypointID.Trim()));
                                cmd.Parameters.Add(new SqlParameter("@jsonTokenInformation", jsonToken.ToString(Formatting.None)));
                            });
                        }
                    }
                    catch { }
                }
            }
        }

        internal string LoginOracleG2_TokenCache(DataRow drCredentials, Action<string> callbackResult)
        {
            string id_token = "";
            string refresh_token = "";
            bool needsTokenRefresh = false;

            try
            {
                // 09/26/24 (JSV): Changing this to attempt to use the refresh token method to get a new token id instead of the user/pw method.
                // The new stored proc returns the most recent entry in the token table for the waypoint along with a flag that determines
                // if we need to get a new token id.
                DataAccess daThisServer = new DataAccess();
                string waypointID = drCredentials["grabStoreWaypointID"].ToString();
                DataSet dsTokenInformation = daThisServer.ExecuteStoredProcedure("sp_Cursus_Get_OracleG2_TokenInformationWithRefresh", cmd =>
                {
                    cmd.Parameters.Add(new SqlParameter("@GrabStoreWaypointID", waypointID));
                });
                if (dsTokenInformation == null || dsTokenInformation.Tables.Count == 0 || dsTokenInformation.Tables[0].Rows.Count == 0)
                {
                    throw new Exception("Stored procedure returned no data or failed to execute.");
                }
                JObject jsonTokenInformationDatabase = JObject.Parse(dsTokenInformation.Tables[0].Rows[0]["jsonTokenInformation"].ToString());

                if (dsTokenInformation.Tables[0].Columns.Contains("NeedsTokenRefresh"))
                {
                    needsTokenRefresh = Convert.ToBoolean(dsTokenInformation.Tables[0].Rows[0]["NeedsTokenRefresh"]);
                }

                id_token = jsonTokenInformationDatabase["id_token"].ToString();
                refresh_token = jsonTokenInformationDatabase["refresh_token"].ToString();
            }
            catch { }

            // 09/26/24 (JSV): If the refresh flag returns true and the refresh token from the previous call exists, we call the token refresh method.
            // otherwise, we fallback to the user/pw login method.  We also fallback if the refresh token method fails.
            if (needsTokenRefresh && refresh_token.Length > 0)
            {
                id_token = string.Empty;

                try
                {
                    id_token = TokenRefreshOracleG2(drCredentials, refresh_token, callbackResult);
                }
                catch { }

                if (String.IsNullOrEmpty(id_token))
                {
                    id_token = LoginOracleG2(drCredentials, callbackResult);
                }
            }
            else if (id_token.Length == 0)
                id_token = LoginOracleG2(drCredentials, callbackResult);
            return id_token;
        }

        private string TokenRefreshOracleG2(DataRow drCredentials, string refreshToken, Action<string> callbackResult)
        {
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
            string client_id = drCredentials["clientID"].ToString().Trim();
            string sURL_Token = drCredentials["urlOAuth"].ToString().Trim() + "token";
            string failureReason = "";
            JObject jsonToken = new JObject();

            var arrParametersToken = new List<KeyValuePair<string, string>>
                    {
                        new KeyValuePair<string, string>("grant_type", "refresh_token"),
                                    new KeyValuePair<string, string>("scope", "openid"),
                                    new KeyValuePair<string, string>("client_id", client_id),
                                    new KeyValuePair<string, string>("refresh_token",refreshToken),
                                    new KeyValuePair<string, string>("redirect_uri", "apiaccount://callback")
                    };
            failureReason = "";
            String sResponse = PostOracleG2(sURL_Token, ref failureReason, arrParametersToken);
            if (failureReason.Length > 0)
            {
                try
                {
                    JObject jsonResponse = JObject.Parse(failureReason);
                }
                catch
                {
                }
            }
            else
            {
                JObject jsonResponse = JObject.Parse(sResponse);
                jsonToken = JObject.Parse(sResponse);

                bool bSaveToken = true;
                try
                {
                    JToken jStatus = jsonToken.SelectToken("status");

                    if (jStatus != null && jStatus.Value<int>() >= 400)
                        //if (jsonToken["code"].ToString().Trim().ToUpper() == "VALIDATION_ERRORS" || jsonToken["code"].ToString().Trim().ToUpper() == "KEY_EXPIRED")
                        bSaveToken = false;
                }
                catch { }
                try
                {
                    if (bSaveToken)
                        SaveTokenInformation(drCredentials["grabStoreWaypointID"].ToString().Trim(), jsonToken);
                }
                catch { }
            }

            return jsonToken["id_token"].ToString();
        }

        private string LoginOracleG2(DataRow drCredentials, Action<string> callbackResult)
        {
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
            string sServer = System.Environment.MachineName.ToUpper();
            //string sCodeVerifier = GenerateNonce();
            //string sCodeChallenge = GenerateCodeChallenge(sCodeVerifier);
            string client_id = drCredentials["clientID"].ToString().Trim();
            //string sURL_Authorize = drCredentials["urlOAuth"].ToString().Trim() + @"authorize?scope=openid&response_type=code&client_id=" + client_id + "&redirect_uri=apiaccount://callback&code_challenge=" + sCodeChallenge + "&code_challenge_method=S256";
            string sURL_SignIn_Basic = drCredentials["urlOAuth"].ToString().Trim() + "signin";
            string sURL_Token = drCredentials["urlOAuth"].ToString().Trim() + "token";
            string username = drCredentials["username"].ToString().Trim();
            string password = drCredentials["password"].ToString().Trim();
            string orgshortname = drCredentials["orgshortname"].ToString().Trim();

            JObject jsonCredentials = new JObject();
            string failureReason = "";
            string callBackCode = "";
            JObject jsonToken = new JObject();
            JObject jsonOrganization = new JObject();
            JObject jsonLocation = new JObject();


            //Auth
            CookieContainer cookieContainer = new CookieContainer();
            //var request = (HttpWebRequest)WebRequest.Create(sURL_Authorize);
            //request.CookieContainer = cookieContainer;
            StringBuilder sbCookie = new StringBuilder();
            try
            {
                //request.Method = WebRequestMethods.Http.Get;
                //HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                //var arrCookies = cookieContainer.GetCookies(response.ResponseUri);
                string debug = "";
            }
            catch (WebException ex)
            {
            }


            //SignIn
            var arrParametersSignIn = new List<KeyValuePair<string, string>>
                    {
                        new KeyValuePair<string, string>("username", username),
                                    new KeyValuePair<string, string>("password", password),
                                    new KeyValuePair<string, string>("orgname", orgshortname)
                    };
            //string sResponse = PostOracleG2(sURL_SignIn_Basic, cookieContainer, ref failureReason, arrParametersSignIn);
            if (failureReason.Length > 0)
            {
                try
                {
                    JObject jsonResponse = JObject.Parse(failureReason);
                }
                catch
                {
                }
            }

            //Token
            cookieContainer = new CookieContainer();
            var arrParametersToken = new List<KeyValuePair<string, string>>
                    {
                        new KeyValuePair<string, string>("grant_type", "authorization_code"),
                                    new KeyValuePair<string, string>("scope", "openid"),
                                    new KeyValuePair<string, string>("client_id", client_id),
                                    new KeyValuePair<string, string>("code",callBackCode),
                                    new KeyValuePair<string, string>("code_verifier", sCodeVerifier),                                    
                                    //new KeyValuePair<string, string>("refresh_token", ""),
                                    new KeyValuePair<string, string>("redirect_uri", "apiaccount://callback")
                    };
            failureReason = "";
            //sResponse = PostOracleG2(sURL_Token, ref failureReason, arrParametersToken);
            if (failureReason.Length > 0)
            {
                try
                {
                    JObject jsonResponse = JObject.Parse(failureReason);
                }
                catch
                {
                }
            }

            return jsonToken["id_token"].ToString();
        }

        public string PostOracleG2(string url, ref string failureReason, List<KeyValuePair<string, string>> arrParameters)
        {
            string sResponse = "";
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/x-www-form-urlencoded"));
                    client.DefaultRequestHeaders.Add("Accept", "*/*");

                    var Request = new HttpRequestMessage(HttpMethod.Post, url)
                    {
                        Content = new FormUrlEncodedContent(arrParameters)
                    };

                    var vResult = client.SendAsync(Request).Result.Content.ReadAsStringAsync();
                    sResponse = vResult.Result.ToString();
                }
            }
            catch (WebException wex)
            {
                var resp = new System.IO.StreamReader(wex.Response.GetResponseStream()).ReadToEnd();
                failureReason = resp.ToString();
                return "";
            }
            catch (Exception ex)
            {
                failureReason = ex.Message;
                return "";
            }
            return sResponse;
        }

        internal static string GenerateNonce()
        {
            const string chars = "abcdefghijklmnopqrstuvwxyz123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-._~";
            var random = new Random();
            var nonce = new char[128];
            for (int i = 0; i < nonce.Length; i++)
            {
                nonce[i] = chars[random.Next(chars.Length)];
            }

            return new string(nonce);
        }

    }
}

using System;
using System.Data;
using System.Data.SqlClient;
using System.Configuration;
using System.Collections.Generic;

namespace GrabService_SSP_updateUnavailableMenuitems_OracleG2
{
    public class DataAccess
    {

        private readonly string _connectionString;

        public DataAccess()
        {
            _connectionString = ConfigurationManager.ConnectionStrings["SqlConnectionString"].ConnectionString;
        }
        public bool ExecuteStoredProcedurecheck(string storedProcName, Action<SqlCommand> addParameters)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            using (SqlCommand cmd = new SqlCommand(storedProcName, conn))
            {
                cmd.CommandType = CommandType.StoredProcedure;
                addParameters(cmd);

                conn.Open();
                int rowsAffected = cmd.ExecuteNonQuery();
                return rowsAffected > 0;
            }
        }
        public DataSet ExecuteStoredProcedure(string storedProcName, Action<SqlCommand> configureParameters)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            using (SqlCommand cmd = new SqlCommand(storedProcName, conn))
            using (SqlDataAdapter da = new SqlDataAdapter(cmd))
            {
                cmd.CommandType = CommandType.StoredProcedure;

                configureParameters?.Invoke(cmd); // Let caller add parameters

                DataSet ds = new DataSet();
                conn.Open();
                da.Fill(ds);
                return ds;
            }
        }

        public int ExecuteStoredProcedureforupdate(string storedProcName, Action<SqlCommand> configureParameters)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            using (SqlCommand cmd = new SqlCommand(storedProcName, conn))
            using (SqlDataAdapter da = new SqlDataAdapter(cmd))
            {
                cmd.CommandType = CommandType.StoredProcedure;

                configureParameters?.Invoke(cmd); // Let caller add parameters

                conn.Open();
                int rowsAffected = cmd.ExecuteNonQuery();
                return rowsAffected;
            }
        }
        public DataSet ExecuteSelectQuery(string sqlQuery)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            using (SqlCommand cmd = new SqlCommand(sqlQuery, conn))
            using (SqlDataAdapter da = new SqlDataAdapter(cmd))
            {
                DataSet ds = new DataSet();
                conn.Open();
                da.Fill(ds);
                return ds;
            }
        }
        public DataSet ExecuteSelectQuerywithParam(string sqlQuery, Dictionary<string, object> parameters)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            using (SqlCommand cmd = new SqlCommand(sqlQuery, conn))
            {
                foreach (var param in parameters)
                {
                    cmd.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
                }

                using (SqlDataAdapter da = new SqlDataAdapter(cmd))
                {
                    DataSet ds = new DataSet();
                    conn.Open();
                    da.Fill(ds);
                    return ds;
                }
            }
        }
        public int ExecuteNonQuery(string query, SqlParameter[] parameters)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString)) // use your actual connection string variable
            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                if (parameters != null)
                {
                    cmd.Parameters.AddRange(parameters);
                }

                conn.Open();
                return cmd.ExecuteNonQuery(); // returns number of affected rows
            }
        }

        public DataTable ExecuteSelectDataTable(string storedProcedure, Action<SqlCommand> addParameters = null)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            using (SqlCommand cmd = new SqlCommand(storedProcedure, conn))
            {
                cmd.CommandType = CommandType.StoredProcedure;
                addParameters?.Invoke(cmd);

                using (SqlDataAdapter adapter = new SqlDataAdapter(cmd))
                {
                    DataTable dt = new DataTable();
                    adapter.Fill(dt);
                    return dt;
                }

            }
        }

    }

}

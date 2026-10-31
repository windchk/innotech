using System.Data;
using System.Data.SqlClient;

namespace Innotech.Sap.Api.Models
{
    public class DIHelper
    {
        public static Dictionary<string, int> BaseType;
        public static Dictionary<int, string> BaseTable;
        public static SAPbobsCOM.Company oCompany;
        public static int retCode;
        public static string retMsg;

        public static IConfiguration Configuration;
        public static string constrDB_UAT_2;
        public static string constrtemplog;

        public static void Connect()
        {
            oCompany = new SAPbobsCOM.Company();
            oCompany.Server = Configuration["Sap:Server"];
            oCompany.SLDServer = Configuration["Sap:SldServer"];
            oCompany.DbServerType = (SAPbobsCOM.BoDataServerTypes)Enum.Parse(typeof(SAPbobsCOM.BoDataServerTypes), Configuration["Sap:DbServerType"]);
            oCompany.CompanyDB = Configuration["Sap:CompanyDb"];
            oCompany.UserName = Configuration["Sap:UserName"];
            oCompany.Password = Configuration["Sap:Password"];
            oCompany.language = (SAPbobsCOM.BoSuppLangs)Enum.Parse(typeof(SAPbobsCOM.BoSuppLangs), Configuration["Sap:Language"]);
            oCompany.UseTrusted = Convert.ToBoolean(Configuration["Sap:UseTrusted"]);
            oCompany.DbUserName = Configuration["Sap:DbUserName"];
            oCompany.DbPassword = Configuration["Sap:DbPassword"];
            oCompany.LicenseServer = Configuration["Sap:LicenseServer"];
            retCode = oCompany.Connect();
            oCompany.GetLastError(out retCode, out retMsg);
            if (retCode != 0)
            {
                Console.WriteLine("Error : " + retMsg);
            }
            else
            {
                Console.WriteLine(oCompany.CompanyDB + " Connected");
            }
        }

        public static void RunDicTionry()
        {
            BaseType = new Dictionary<string, int>()
            {
                { "G/L Accounts",1 },
                    { "Business Partner",2 },
                    { "Bank Codes",3 },
                    { "Items",4},
                    { "Tax Definition",5},
                    { "Price Lists",6},
                    { "Special Prices",7},
                    { "Item Properties",8},
                    { "Rate Differences",9},
                    { "Card Groups",10},
                    { "Contact Persons",11},
                    { "Users",12},
                    { "A/R Invoice",13},
                    { "A/R Credit Memo",14},
                    { "Delivery",15},
                    { "Returns",16},
                    { "Sales Order",17},
                    { "A/P Invoice",18},
                    { "A/P Credit Memo",19},
                    { "Goods Receipt PO",20},
                    { "Goods Return",21},
                    { "Purchase Order",22},
                    { "Sales Quotation",23},
                    { "Incoming Payment",24},
                    { "Deposit",25},
                    { "Reconciliation History",26},
                    { "Journal Entry",30 },
                    { "Goods Receipt",59 },
                    { "Goods Issue",60 },
                    { "Inventory Transfer",67},
                    { "Production Order",202},
                    { "Inventory Transfer Request",1250000001},
                    { "Return Request",234000031},
                    { "Purchase Request",1470000113},
                    { "Goods Return Request",234000032}
            };

            BaseTable = new Dictionary<int, string>()
            {
                 { 1,"OACT"},
                    { 2,"OCRD"},
                    { 3,"ODSC"},
                    { 4,"OITM"},
                    { 5,"OVTG"},
                    { 6,"OPLN"},
                    { 7,"OSPP"},
                    { 8,"OITG"},
                    { 9,"ORTM"},
                    { 10,"OCRG"},
                    { 11,"OCPR"},
                    { 12,"OUSR"},
                    { 13,"OINV"},
                    { 14,"ORIN"},
                    { 15,"ODLN"},
                    { 16,"ORDN"},
                    { 17,"ORDR"},
                    { 18,"OPCH"},
                    { 19,"ORPC"},
                    { 20,"OPDN"},
                    { 21,"ORPD"},
                    { 22,"OPOR"},
                    { 23,"OQUT"},
                    { 24,"ORCT"},
                    { 25,"ODPS"},
                    { 26,"OMTH"},
                    { 67,"OWTR" },
                    { 1470000113,"OPRQ"},
                    { 1250000001,"OWTQ" },
                    { 234000031,"ORRR" },
                    { 234000032,"OPRR" }
            };
        }
        public static string GetTable(int Teble)
        {
            RunDicTionry();
            return BaseTable[Teble];
        }
        public static dynamic DoQuery(string sql, string Value)
        {
            try
            {
                SAPbobsCOM.Recordset oRec = (SAPbobsCOM.Recordset)oCompany.GetBusinessObject(SAPbobsCOM.BoObjectTypes.BoRecordset);
                oRec.DoQuery(sql);
                return oRec.Fields.Item(Value).Value;
            }
            catch (Exception ex)
            {
                return ex.Message.ToString();
            }
        }

        public static dynamic QuerySql(string sqlhana, string Values, TempOrSap TempOrSap)
        {
            string _Values = "";
            //HanaConnection ConnHana = new HanaConnection(constrSap);
            SqlConnection ConnHana;
            if (TempOrSap == TempOrSap.DB_UAT_2)
                ConnHana = new SqlConnection(constrDB_UAT_2);
            else
                ConnHana = new SqlConnection(constrtemplog);
            try
            {
                ConnHana.Open();
                var sqlCmd = sqlhana;
                SqlCommand command = new SqlCommand(sqlCmd, ConnHana);
                command.CommandTimeout = Convert.ToInt32(Configuration["Sql:CommandTimeoutSeconds"]);
                SqlDataReader dr = command.ExecuteReader();
                while (dr.Read())
                {
                    _Values = $"{dr[Values]}";
                }
            }
            catch (Exception ex)
            {
                return ex.Message.ToString();
            }
            finally
            {
                if (ConnHana.State == ConnectionState.Open)
                {
                    ConnHana.Close();
                }
            }
            return _Values;
        }

        public static void ExecuteSql(string Sql, TempOrSap TempOrSap)
        {
            SqlConnection Connection = null;
            try
            {
                if (TempOrSap == TempOrSap.DB_UAT_2)
                    Connection = new SqlConnection(constrDB_UAT_2);
                else
                    Connection = new SqlConnection(constrtemplog);
                Connection.Open();
                SqlCommand MyCommand = new SqlCommand(Sql, Connection);
                MyCommand.CommandTimeout = Convert.ToInt32(Configuration["Sql:CommandTimeoutSeconds"]);
                int errCode = MyCommand.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message.ToString());
            }
            finally
            {
                if (Connection.State == System.Data.ConnectionState.Open)
                {
                    Connection.Close();
                }
            }
        }

        public enum TempOrSap
        {
            templog = 0,
            DB_UAT_2 = 2
        }
    }
}

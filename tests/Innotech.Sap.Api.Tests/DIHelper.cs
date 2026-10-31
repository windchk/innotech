using System.Text.RegularExpressions;
using Innotech.Sap.Api.Tests;
using Microsoft.Extensions.Configuration;

namespace Innotech.Sap.Api.Models;

// This replacement is compiled only in the test project with the linked controller.
// The application DIHelper contains no test hooks or factories.
public class DIHelper
{
    public static IConfiguration Configuration;
    public static SAPbobsCOM.Company oCompany;
    public static int retCode;
    public static string retMsg;
    public static FakeSapCompany Sap;
    public static List<LogRow> Logs;
    public static List<string> Statements;
    public static bool FailLogInsert;
    private static int NextLogId;

    public static void Reset()
    {
        Sap = new FakeSapCompany();
        retCode = 0;
        retMsg = "";
        Logs = [];
        Statements = [];
        FailLogInsert = false;
        NextLogId = 1;
        Configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["Interface:LogTable"] = "Log_ORDR"
        }).Build();
        oCompany = (SAPbobsCOM.Company)SapMockProxy.Wrap(typeof(SAPbobsCOM.Company), Sap);
    }

    public static void Connect()
    {
        Sap.Events.Add("connect");
        if (Sap.ConnectException != null) throw Sap.ConnectException;
        retCode = Sap.ConnectError;
        retMsg = retCode == 0 ? "" : "SAP login failed.";
        Sap.Connected = retCode == 0;
        Sap.LastError = 0;
    }

    public static dynamic DoQuery(string sql, string Value)
    {
        int id = int.Parse(Regex.Match(sql, @"= '(\d+)'").Groups[1].Value);
        bool byEntry = sql.Contains("Where DocEntry", StringComparison.Ordinal);
        var order = Sap.Orders.Values.FirstOrDefault(item => byEntry ? item.DocEntry == id : item.DocNum == id);
        return order == null ? 0 : Value == "DocEntry" ? order.DocEntry : order.DocNum;
    }

    public static dynamic QuerySql(string sqlhana, string Values, TempOrSap TempOrSap)
    {
        Assert.Equal(TempOrSap.templog, TempOrSap);
        Statements.Add(sqlhana);
        Assert.Equal("Log_ID", Values);
        Assert.Contains("OUTPUT INSERTED.Log_ID", sqlhana);
        Sap.Events.Add("log-insert");
        if (FailLogInsert) return "Interface log insert failed.";
        Match match = Regex.Match(sqlhana, @"VALUES\s*\(N'(-?\d+)'\s*,N'(\d+)'\s*,N'[^']*'\s*,N'((?:''|[^'])*)'\s*\)");
        Assert.True(match.Success, sqlhana);
        var row = new LogRow
        {
            LogId = NextLogId++,
            DocEntry = int.Parse(match.Groups[1].Value),
            DocNum = match.Groups[2].Value,
            Json = match.Groups[3].Value.Replace("''", "'")
        };
        Logs.Add(row);
        return row.LogId.ToString();
    }

    public static void ExecuteSql(string Sql, TempOrSap TempOrSap)
    {
        Assert.Equal(TempOrSap.templog, TempOrSap);
        Statements.Add(Sql);
        Sap.Events.Add("log-update");
        Match match = Regex.Match(Sql, @"Where Log_ID = '(\d+)'");
        Assert.True(match.Success, Sql);
        LogRow row = Logs.Single(item => item.LogId == int.Parse(match.Groups[1].Value));
        row.DocEntry = int.Parse(Regex.Match(Sql, @"DocEntry = '(-?\d+)'").Groups[1].Value);
        row.State = Regex.Match(Sql, @"F_SAP = '([^']*)'").Groups[1].Value;
        row.Message = Regex.Match(Sql, @"F_MESSAGE = N'((?:''|[^'])*)'").Groups[1].Value.Replace("''", "'");
        row.DocNum = Regex.Match(Sql, @"DocNum_ORDR = '([^']*)'").Groups[1].Value;
    }

    public enum TempOrSap { templog = 0, DB_UAT_2 = 2 }
}

public class LogRow
{
    public int LogId { get; set; }
    public int DocEntry { get; set; }
    public string Json { get; set; }
    public string State { get; set; }
    public string Message { get; set; }
    public string DocNum { get; set; }
}

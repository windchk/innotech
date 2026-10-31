using Innotech.Sap.Api.Controllers;
using Innotech.Sap.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Innotech.Sap.Api.Tests;

public class SalesOrderTests
{
    public SalesOrderTests() => DIHelper.Reset();

    private static Sales_Order Request() => new()
    {
        DocEntry = 1,
        U_OLDCODE = "OLD-CODE-PAYLOAD",
        U_SO_Ref = "PAYLOAD-REF",
        U_So_Line_Ref = 17
    };

    private static List<ORDR_Response> Run(params Sales_Order[] requests)
    {
        using var controller = new ORDRController();
        var result = Assert.IsType<OkObjectResult>(controller.PostSalesOrder(requests.ToList()));
        return Assert.IsType<List<ORDR_Response>>(result.Value);
    }

    [Fact]
    public void Connection_test_returns_company_and_disconnects_without_changing_orders()
    {
        using var controller = new ORDRController();
        var result = Assert.IsType<OkObjectResult>(controller.TestConnectSAP());
        var response = JObject.FromObject(result.Value);

        Assert.Equal("Success", response["Status"].Value<string>());
        Assert.Equal("TEST_COMPANY", response["CompanyDB"].Value<string>());
        Assert.Equal("Test Company", response["CompanyName"].Value<string>());
        Assert.Equal(0, response["errCode"].Value<int>());
        Assert.Equal(new[] { "connect", "disconnect" }, DIHelper.Sap.Events);
        Assert.False(DIHelper.Sap.Connected);
        Assert.Empty(DIHelper.Logs);
        Assert.All(DIHelper.Sap.Orders.Values, order =>
        {
            Assert.False(order.Cancelled);
            Assert.Equal("HEADER_OLD", order.HeaderValue);
            Assert.All(order.Lines, line => Assert.Equal("OLD_REF", line.U_SO_Ref));
        });
    }

    [Fact]
    public void Connection_test_returns_sap_login_error()
    {
        DIHelper.Sap.ConnectError = -4008;
        using var controller = new ORDRController();
        var result = Assert.IsType<OkObjectResult>(controller.TestConnectSAP());
        var response = JObject.FromObject(result.Value);

        Assert.Equal("Error", response["Status"].Value<string>());
        Assert.Equal(-4008, response["errCode"].Value<int>());
        Assert.Equal("SAP login failed.", response["errMsg"].Value<string>());
        Assert.Equal(new[] { "connect" }, DIHelper.Sap.Events);
    }

    [Fact]
    public void Connection_test_returns_connection_exception()
    {
        DIHelper.Sap.ConnectException = new InvalidOperationException("DI API unavailable.");
        using var controller = new ORDRController();
        var result = Assert.IsType<OkObjectResult>(controller.TestConnectSAP());
        var response = JObject.FromObject(result.Value);

        Assert.Equal("Exception Error", response["Status"].Value<string>());
        Assert.Equal("DI API unavailable.", response["errMsg"].Value<string>());
        Assert.Equal(new[] { "connect" }, DIHelper.Sap.Events);
    }

    [Fact]
    public void Updates_header_and_all_lines_then_cancels_the_same_so_and_logs_payload()
    {
        var request = Request();
        var response = Assert.Single(Run(request));
        Assert.Equal("Success", response.Status);
        Assert.Equal("1001", response.SO_DocNum);
        Assert.Equal(0, response.errCode);
        Assert.Equal(request.U_OLDCODE, DIHelper.Sap.Orders[1].HeaderValue);
        Assert.All(DIHelper.Sap.Orders[1].Lines, line =>
        {
            Assert.Equal(request.U_SO_Ref, line.U_SO_Ref);
            Assert.Equal(17, line.U_So_Line_Ref);
        });
        Assert.True(DIHelper.Sap.Orders[1].Cancelled);
        Assert.Equal(new[] { "log-insert", "connect", "get:1", "update:1", "cancel:1", "log-update" }, DIHelper.Sap.Events);
        Assert.False(DIHelper.Sap.Orders[2].Cancelled);
        Assert.Equal("HEADER_OLD", DIHelper.Sap.Orders[2].HeaderValue);
        Assert.All(DIHelper.Sap.Orders[2].Lines, line => Assert.Equal("OLD_REF", line.U_SO_Ref));

        var log = Assert.Single(DIHelper.Logs);
        Assert.Equal("Y", log.State);
        Assert.Equal("1001", log.DocNum);
        Assert.Equal("Success", log.Message);
        Assert.Equal(1, log.LogId);
        Assert.Equal(1, log.DocEntry);
        var logged = JArray.Parse(log.Json)[0];
        Assert.Equal(request.U_OLDCODE, logged["U_OLDCODE"].Value<string>());
        Assert.Equal(request.U_SO_Ref, logged["U_SO_Ref"].Value<string>());
        Assert.Equal(request.U_So_Line_Ref, logged["U_So_Line_Ref"].Value<short?>());
        Assert.Null(logged["Lines"]);
        Assert.Null(logged["LineNum"]);
        Assert.Null(logged["U_Web_Entry"]);
        Assert.Null(logged["DocNum"]);
    }

    [Fact]
    public void Reads_docnum_from_sap_for_response_and_log_with_docentry_only_payload()
    {
        var request = Request();
        DIHelper.Sap.Orders[1].DocNum = 987654;
        var response = Assert.Single(Run(request));
        Assert.Equal("Success", response.Status);
        Assert.Equal("987654", response.SO_DocNum);
        Assert.Equal(request.U_OLDCODE, DIHelper.Sap.Orders[1].HeaderValue);
        Assert.All(DIHelper.Sap.Orders[1].Lines, line => Assert.Equal(request.U_SO_Ref, line.U_SO_Ref));
        Assert.True(DIHelper.Sap.Orders[1].Cancelled);
        Assert.Equal(1, Assert.Single(DIHelper.Logs).DocEntry);
        Assert.Equal("987654", DIHelper.Logs[0].DocNum);
        Assert.Null(JObject.FromObject(request)["DocNum"]);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"DocEntry\":0}")]
    [InlineData("{\"DocEntry\":-1}")]
    [InlineData("{\"DocNum\":1001}")]
    public void Requires_a_positive_docentry_and_never_looks_up_by_docnum(string keyJson)
    {
        var payload = JObject.FromObject(Request());
        payload.Remove("DocEntry");
        payload.Merge(JObject.Parse(keyJson));
        var request = JsonConvert.DeserializeObject<Sales_Order>(payload.ToString());
        var response = Assert.Single(Run(request));

        Assert.Equal("Exception Error", response.Status);
        Assert.Contains("DocEntry", response.errMsg);
        Assert.DoesNotContain("connect", DIHelper.Sap.Events);
        Assert.DoesNotContain("update:1", DIHelper.Sap.Events);
        Assert.DoesNotContain("cancel:1", DIHelper.Sap.Events);
        Assert.Equal("N", Assert.Single(DIHelper.Logs).State);
        Assert.Equal(request.DocEntry, DIHelper.Logs[0].DocEntry);
    }

    [Fact]
    public void Updates_every_line_even_when_sap_line_numbers_have_gaps()
    {
        DIHelper.Sap.Orders[1].Lines[1].LineNum = 7;
        DIHelper.Sap.Orders[1].Lines[2].LineNum = 15;
        var request = Request();
        Assert.Equal("Success", Assert.Single(Run(request)).Status);
        Assert.All(DIHelper.Sap.Orders[1].Lines, line =>
        {
            Assert.Equal(request.U_SO_Ref, line.U_SO_Ref);
            Assert.Equal(17, line.U_So_Line_Ref);
        });
        Assert.Equal(new[] { 0, 7, 15 }, DIHelper.Sap.Orders[1].Lines.Select(line => line.LineNum));
    }

    [Theory]
    [InlineData("", 0, "")]
    [InlineData("0", 0, "0")]
    [InlineData("00042", 7, "000OLD")]
    public void Uses_text_and_numeric_values_from_payload(string reference, short lineReference, string oldCode)
    {
        var request = Request();
        request.U_SO_Ref = reference;
        request.U_So_Line_Ref = lineReference;
        request.U_OLDCODE = oldCode;
        Assert.Equal("Success", Assert.Single(Run(request)).Status);
        Assert.Equal(oldCode, DIHelper.Sap.Orders[1].HeaderValue);
        Assert.All(DIHelper.Sap.Orders[1].Lines, line =>
        {
            Assert.Equal(reference, line.U_SO_Ref);
            Assert.Equal((int)lineReference, line.U_So_Line_Ref);
        });
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"U_SO_Ref\":\"REF\",\"U_So_Line_Ref\":\"5\"}")]
    [InlineData("{\"U_OLDCODE\":\"OLD\",\"U_So_Line_Ref\":\"5\"}")]
    [InlineData("{\"U_OLDCODE\":\"OLD\",\"U_SO_Ref\":\"REF\"}")]
    [InlineData("{\"U_OLDCODE\":null,\"U_SO_Ref\":\"REF\",\"U_So_Line_Ref\":\"5\"}")]
    [InlineData("{\"U_OLDCODE\":\"OLD\",\"U_SO_Ref\":null,\"U_So_Line_Ref\":\"5\"}")]
    [InlineData("{\"U_OLDCODE\":\"OLD\",\"U_SO_Ref\":\"REF\",\"U_So_Line_Ref\":null}")]
    [InlineData("{\"Lines\":[{\"LineNum\":0,\"U_SO_Ref\":\"REF\",\"U_So_Line_Ref\":\"5\"}]}")]
    public void Missing_udf_values_never_update_or_cancel(string fieldsJson)
    {
        var payload = JObject.Parse(fieldsJson);
        payload["DocEntry"] = 1;
        var request = JsonConvert.DeserializeObject<Sales_Order>(payload.ToString());
        var response = Assert.Single(Run(request));
        Assert.Equal("Exception Error", response.Status);
        Assert.DoesNotContain("update:1", DIHelper.Sap.Events);
        Assert.DoesNotContain("cancel:1", DIHelper.Sap.Events);
        Assert.False(DIHelper.Sap.Orders[1].Cancelled);
        Assert.Equal("HEADER_OLD", DIHelper.Sap.Orders[1].HeaderValue);
        Assert.All(DIHelper.Sap.Orders[1].Lines, line => Assert.Equal("OLD_REF", line.U_SO_Ref));
        Assert.Equal("N", Assert.Single(DIHelper.Logs).State);
    }

    [Fact]
    public void Update_error_preserves_header_and_lines_and_prevents_cancel()
    {
        DIHelper.Sap.FailUpdateEntry = 1;
        var response = Assert.Single(Run(Request()));
        Assert.Equal("Error", response.Status);
        Assert.Equal(-5002, response.errCode);
        Assert.False(DIHelper.Sap.Orders[1].Cancelled);
        Assert.Equal("HEADER_OLD", DIHelper.Sap.Orders[1].HeaderValue);
        Assert.All(DIHelper.Sap.Orders[1].Lines, line =>
        {
            Assert.Equal("OLD_REF", line.U_SO_Ref);
            Assert.Equal(-1, line.U_So_Line_Ref);
        });
        Assert.DoesNotContain("cancel:1", DIHelper.Sap.Events);
        Assert.Equal("N", Assert.Single(DIHelper.Logs).State);
        Assert.Equal(response.errMsg, DIHelper.Logs[0].Message);
    }

    [Fact]
    public void Cancel_error_is_logged_and_the_previous_header_and_line_updates_remain()
    {
        DIHelper.Sap.FailCancel = true;
        var request = Request();
        var response = Assert.Single(Run(request));
        Assert.Equal("Error", response.Status);
        Assert.Equal(request.U_OLDCODE, DIHelper.Sap.Orders[1].HeaderValue);
        Assert.All(DIHelper.Sap.Orders[1].Lines, line =>
        {
            Assert.Equal(request.U_SO_Ref, line.U_SO_Ref);
            Assert.Equal(17, line.U_So_Line_Ref);
        });
        Assert.False(DIHelper.Sap.Orders[1].Cancelled);
        Assert.Equal("N", Assert.Single(DIHelper.Logs).State);
    }

    [Fact]
    public void Missing_order_returns_exception_and_stops_batch()
    {
        var request = Request();
        request.DocEntry = 999;
        var response = Assert.Single(Run(request, Request()));
        Assert.Equal("Exception Error", response.Status);
        Assert.Single(DIHelper.Logs);
        Assert.Equal("N", DIHelper.Logs[0].State);
        Assert.DoesNotContain("update:1", DIHelper.Sap.Events);
        Assert.DoesNotContain("cancel:1", DIHelper.Sap.Events);
    }

    [Fact]
    public void Sap_error_allows_the_next_head_and_keeps_each_log_payload_separate()
    {
        DIHelper.Sap.FailUpdateEntry = 1;
        var second = Request();
        second.DocEntry = 3;
        second.U_OLDCODE = "SECOND-OLD";
        second.U_SO_Ref = "SECOND-REF";
        var responses = Run(Request(), second);
        Assert.Equal(new[] { "Error", "Success" }, responses.Select(item => item.Status));
        Assert.Equal(new[] { 1, 2 }, DIHelper.Logs.Select(item => item.LogId));
        Assert.Equal(new[] { 1, 3 }, DIHelper.Logs.Select(item => item.DocEntry));
        Assert.Equal(new[] { "N", "Y" }, DIHelper.Logs.Select(item => item.State));
        Assert.True(DIHelper.Sap.Orders[3].Cancelled);
        Assert.Equal(second.U_OLDCODE, DIHelper.Sap.Orders[3].HeaderValue);
        Assert.All(DIHelper.Sap.Orders[3].Lines, line => Assert.Equal(second.U_SO_Ref, line.U_SO_Ref));
        var secondPayload = JArray.Parse(DIHelper.Logs[1].Json)[0];
        Assert.Equal(3, secondPayload["DocEntry"].Value<int>());
        Assert.Equal(second.U_SO_Ref, secondPayload["U_SO_Ref"].Value<string>());
    }

    [Fact]
    public void Log_sql_preserves_apostrophes_and_thai_for_header_and_line_values()
    {
        var request = Request();
        request.U_OLDCODE = "เก่า'O'Brien";
        request.U_SO_Ref = "O'Brien ไทย";
        request.U_So_Line_Ref = 17;
        Assert.Equal("Success", Assert.Single(Run(request)).Status);
        var log = Assert.Single(DIHelper.Logs);
        var logged = JArray.Parse(log.Json)[0];
        Assert.Equal(request.U_OLDCODE, logged["U_OLDCODE"].Value<string>());
        Assert.Equal(request.U_SO_Ref, logged["U_SO_Ref"].Value<string>());
        Assert.Equal(request.U_So_Line_Ref, logged["U_So_Line_Ref"].Value<short?>());
        Assert.Contains("O''Brien", DIHelper.Statements[0]);
    }

    [Fact]
    public void Response_uses_four_fields_without_a_web_identifier()
    {
        var response = Assert.Single(Run(Request()));
        Assert.Equal(new[] { "SO_DocNum", "Status", "errCode", "errMsg" },
            JObject.FromObject(response).Properties().Select(property => property.Name));
    }

    [Fact]
    public void Repeated_requests_for_the_same_so_get_different_log_ids()
    {
        DIHelper.Sap.FailUpdateEntry = 1;
        var first = Request();
        var second = Request();
        second.U_OLDCODE = "RETRY";
        var responses = Run(first, second);
        Assert.Equal(2, responses.Count);
        Assert.Equal(new[] { 1, 2 }, DIHelper.Logs.Select(row => row.LogId));
        Assert.All(DIHelper.Logs, row => Assert.Equal(1, row.DocEntry));
        Assert.Equal("RETRY", JArray.Parse(DIHelper.Logs[1].Json)[0]["U_OLDCODE"].Value<string>());
    }

    [Fact]
    public void Failed_log_insert_stops_before_connecting_to_sap()
    {
        DIHelper.FailLogInsert = true;
        Assert.Equal("Exception Error", Assert.Single(Run(Request())).Status);
        Assert.Empty(DIHelper.Logs);
        Assert.DoesNotContain("connect", DIHelper.Sap.Events);
        Assert.DoesNotContain("log-update", DIHelper.Sap.Events);
        Assert.False(DIHelper.Sap.Orders[1].Cancelled);
    }

    [Fact]
    public void Exception_before_a_new_log_does_not_overwrite_the_previous_log()
    {
        var responses = Run(Request(), null);
        Assert.Equal(new[] { "Success", "Exception Error" }, responses.Select(row => row.Status));
        Assert.Equal("Y", Assert.Single(DIHelper.Logs).State);
        Assert.Single(DIHelper.Sap.Events, value => value == "log-update");
    }
}

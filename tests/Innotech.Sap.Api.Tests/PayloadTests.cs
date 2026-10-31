using System.Net;
using System.Text;
using System.Globalization;
using Innotech.Sap.Api.Controllers;
using Innotech.Sap.Api.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Serialization;
using Newtonsoft.Json.Linq;

namespace Innotech.Sap.Api.Tests;

public class PayloadTests
{
    [Fact]
    public async Task Text_in_numeric_line_reference_is_rejected_before_sap()
    {
        await using var app = await CreateApplication();
        using var client = app.GetTestClient();
        using var body = new StringContent("""
            [{"DocEntry":1,"U_OLDCODE":"TEST-001","U_SO_Ref":"TEST-001","U_So_Line_Ref":"TEST-001"}]
            """, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/API/SalesOrder/Cancel", body);
        string json = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest,
            $"Expected HTTP 400; actual {(int)response.StatusCode}: {json}");
        Assert.Contains("U_So_Line_Ref", json);
        Assert.DoesNotContain("connect", DIHelper.Sap.Events);
        Assert.False(DIHelper.Sap.Orders[1].Cancelled);
    }

    [Theory]
    [InlineData("32768")]
    [InlineData("-32769")]
    [InlineData("1.5")]
    [InlineData("true")]
    public async Task Invalid_smallint_values_are_rejected_before_sap(string valueJson)
    {
        await using var app = await CreateApplication();
        using var client = app.GetTestClient();
        using var body = new StringContent(
            "[{\"DocEntry\":1,\"U_OLDCODE\":\"TEST-001\",\"U_SO_Ref\":\"TEST-001\",\"U_So_Line_Ref\":" + valueJson + "}]",
            Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/API/SalesOrder/Cancel", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("U_So_Line_Ref", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain("connect", DIHelper.Sap.Events);
        Assert.Empty(DIHelper.Logs);
    }

    [Theory]
    [InlineData(-32768)]
    [InlineData(0)]
    [InlineData(32767)]
    public async Task Numeric_payload_is_written_as_integer_to_every_line_before_cancel(int reference)
    {
        await using var app = await CreateApplication();
        using var client = app.GetTestClient();
        string number = reference.ToString(CultureInfo.InvariantCulture);
        using var body = new StringContent(
            "[{\"DocEntry\":1,\"U_OLDCODE\":\"TEST-001\",\"U_SO_Ref\":\"12345678901234567890\",\"U_So_Line_Ref\":" + number + "}]",
            Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/API/SalesOrder/Cancel", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = JArray.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Success", result[0]["Status"].Value<string>());
        Assert.All(DIHelper.Sap.Orders[1].Lines, line => Assert.Equal(reference, line.U_So_Line_Ref));
        Assert.True(DIHelper.Sap.Orders[1].Cancelled);
        Assert.True(DIHelper.Sap.Events.IndexOf("update:1") < DIHelper.Sap.Events.IndexOf("cancel:1"));
        Assert.Equal(number, JArray.Parse(DIHelper.Logs[0].Json)[0]["U_So_Line_Ref"].ToString());
    }

    [Fact]
    public async Task So_reference_longer_than_twenty_characters_is_rejected_before_sap()
    {
        await using var app = await CreateApplication();
        using var client = app.GetTestClient();
        using var body = new StringContent("""
            [{"DocEntry":1,"U_OLDCODE":"TEST-001","U_SO_Ref":"123456789012345678901","U_So_Line_Ref":0}]
            """, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/API/SalesOrder/Cancel", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("U_SO_Ref", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain("connect", DIHelper.Sap.Events);
    }

    private static async Task<WebApplication> CreateApplication()
    {
        DIHelper.Reset();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(ORDRController).Assembly)
            .AddNewtonsoftJson(options => options.SerializerSettings.ContractResolver = new DefaultContractResolver());
        var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }
}

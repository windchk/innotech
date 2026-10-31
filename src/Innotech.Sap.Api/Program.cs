using Innotech.Sap.Api.Models;
using Newtonsoft.Json.Serialization;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Configuration.Sources.Clear();
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);

DIHelper.Configuration = builder.Configuration;
DIHelper.constrDB_UAT_2 = builder.Configuration.GetConnectionString("DB_UAT_2");
DIHelper.constrtemplog = builder.Configuration.GetConnectionString("templog");

builder.Services.AddControllers().AddNewtonsoftJson(options =>
{
    options.SerializerSettings.ContractResolver = new DefaultContractResolver();
});

WebApplication app = builder.Build();
app.MapControllers();
app.Run();

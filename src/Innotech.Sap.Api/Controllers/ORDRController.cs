using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Innotech.Sap.Api.Models;
using static Innotech.Sap.Api.Models.DIHelper;
using static Innotech.Sap.Api.Models.ORDR;
using static Innotech.Sap.Api.Models.JSON_Response;

namespace Innotech.Sap.Api.Controllers
{
    [ApiController]
    [Route("API")]
    public class ORDRController : Controller
    {
        public SAPbobsCOM.Company oCompany = DIHelper.oCompany;

        [HttpGet, Route("TestConnectSAP")]
        public IActionResult TestConnectSAP()
        {
            try
            {
                DIHelper.Connect();
                SAPbobsCOM.Company oCompany = DIHelper.oCompany;

                if (DIHelper.retCode != 0)
                {
                    return Ok(new
                    {
                        Status = "Error",
                        errCode = DIHelper.retCode,
                        errMsg = DIHelper.retMsg
                    });
                }
                else
                {
                    var response = new
                    {
                        Status = "Success",
                        CompanyDB = oCompany.CompanyDB,
                        CompanyName = oCompany.CompanyName,
                        errCode = 0,
                        errMsg = ""
                    };
                    oCompany.Disconnect();
                    return Ok(response);
                }
            }
            catch (Exception ex)
            {
                return Ok(new
                {
                    Status = "Exception Error",
                    errCode = ex.HResult,
                    errMsg = ex.Message.ToString()
                });
            }
            finally
            {
                GC.Collect();
                Dispose();
            }
        }

        [HttpPost, Route("SalesOrder/Cancel")]
        public IActionResult PostSalesOrder(List<Sales_Order> ORDR)
        {
            string LogHeadTable = Configuration["Interface:LogTable"];
            List<ORDR_Response> Lresponse = new List<ORDR_Response>();
            string errMsg;
            int errCode, Log_ID = 0, DocEntry = 0, SAPDocNum = 0;
            try
            {
                foreach (var Head in ORDR)
                {
                    Log_ID = 0;
                    DocEntry = Head.DocEntry;
                    SAPDocNum = 0;
                    string Json = JsonConvert.SerializeObject(Head);
                    var sql = $@"INSERT INTO {LogHeadTable}
(DocEntry
,DocNum_ORDR
,F_WebDate
,Json
)
OUTPUT INSERTED.Log_ID
VALUES
(N'{DocEntry}'
,N'{SAPDocNum}'
,N'{DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss")}'
,N'[{Json.Replace("'", "''")}]'
)";
                    Log_ID = Convert.ToInt32(QuerySql(sql, "Log_ID", TempOrSap.templog));

                    if (DocEntry <= 0)
                    {
                        throw new Exception("กรุณาส่ง DocEntry มากกว่า 0 จาก payload");
                    }

                    DIHelper.Connect();
                    SAPbobsCOM.Company oCompany = DIHelper.oCompany;

                    if (Head.U_OLDCODE == null || Head.U_SO_Ref == null || Head.U_So_Line_Ref == null)
                    {
                        throw new Exception("กรุณาส่ง U_OLDCODE, U_SO_Ref และ U_So_Line_Ref จาก payload");
                    }

                    SAPbobsCOM.Documents oDoc = (SAPbobsCOM.Documents)oCompany.GetBusinessObject(SAPbobsCOM.BoObjectTypes.oOrders);
                    if (oDoc.GetByKey(DocEntry))
                    {
                        SAPDocNum = oDoc.DocNum;
                        //UDF HEAD
                        oDoc.UserFields.Fields.Item("U_OLDCODE").Value = Head.U_OLDCODE;

                        //UDF LINE
                        for (int i = 0; i < oDoc.Lines.Count; i++)
                        {
                            oDoc.Lines.SetCurrentLine(i);
                            oDoc.Lines.UserFields.Fields.Item("U_SO_Ref").Value = Head.U_SO_Ref;
                            oDoc.Lines.UserFields.Fields.Item("U_So_Line_Ref").Value = Convert.ToInt32(Head.U_So_Line_Ref);
                        }

                        int updateRet = oDoc.Update();
                        if (updateRet == 0)
                        {
                            oDoc.Cancel();
                        }

                        oCompany.GetLastError(out errCode, out errMsg);
                        if (errCode != 0)
                        {
                            var response = new ORDR_Response()
                            {
                                SO_DocNum = "",
                                Status = "Error",
                                errCode = errCode,
                                errMsg = errMsg
                            };
                            Lresponse.Add(response);
                            ExecuteSql($@"Update {LogHeadTable} Set DocEntry = '{DocEntry}',DocNum_ORDR = '{SAPDocNum}',F_SAP = 'N',F_SAPDate = '{DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss")}',F_MESSAGE = N'{errMsg.Replace("'", "''")}' Where Log_ID = '{Log_ID}'", TempOrSap.templog);
                        }
                        else
                        {
                            var response = new ORDR_Response()
                            {
                                SO_DocNum = SAPDocNum.ToString(),
                                Status = "Success",
                                errCode = 0,
                                errMsg = ""
                            };
                            Lresponse.Add(response);
                            ExecuteSql($@"Update {LogHeadTable} Set DocEntry = '{DocEntry}',DocNum_ORDR = '{SAPDocNum}',F_SAP = 'Y',F_SAPDate = '{DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss")}',F_MESSAGE = N'Success' Where Log_ID = '{Log_ID}'", TempOrSap.templog);
                        }
                    }
                    else
                    {
                        throw new Exception("ไม่พบ Sales Order ที่ต้องการ Update UDF และยกเลิก");
                    }
                }
            }
            catch (Exception ex)
            {
                var response = new ORDR_Response()
                {
                    SO_DocNum = "",
                    Status = "Exception Error",
                    errCode = ex.HResult,
                    errMsg = ex.Message.ToString()
                };
                Lresponse.Add(response);
                if (Log_ID != 0)
                {
                    ExecuteSql($@"Update {LogHeadTable} Set DocEntry = '{DocEntry}',DocNum_ORDR = '{SAPDocNum}',F_SAP = 'N',F_SAPDate = '{DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss")}',F_MESSAGE = N'{ex.Message.Replace("'", "''")}' Where Log_ID = '{Log_ID}'", TempOrSap.templog);
                }
            }
            finally
            {
                GC.Collect();
                Dispose();
            }
            return Ok(Lresponse);
        }
    }
}
